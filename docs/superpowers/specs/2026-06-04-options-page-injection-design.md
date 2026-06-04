# Design — Restore stripped vanilla Options to the Android Options page

**Date:** 2026-06-04
**Status:** Design (awaiting user review)
**Related:** TODO #76 (tool-hit), TODO #12 (zoom), handoff `2026-06-04-handoff-options-page-audit-next.md`

## Problem

The Android port strips many vanilla options out of its in-game `OptionsPage` list, even though the `Options` engine still fully handles them (apply + display-sync switches). Controller users on Android therefore can't reach options that exist on console — notably the tool-hit-location checkboxes and screen zoom. Separately, two usability issues affect the page itself: touch-only "joypad" options clutter it for controller users, and the scroll extent is computed once at construction so late-added entries (GMCM's button, our injected options) fall below the scrollable floor and can't be reached with a controller.

## Audit result (what's injectable)

Every `whichOption` in `Options.changeCheckBoxOption` / `changeSliderOption` / `changeDropDownOption` was diffed against the 24 options the Android `OptionsPage` ctor adds. Decisions locked with the user:

- **Inject:** tool-hit `11` (alwaysShowToolHitLocation), `12` (hideToolHitLocationWhenInMotion), and `18` (zoom). All three are handled by the engine and self-sync on construction.
- **Do NOT inject** UI scale (39) — the user already has per-element toolbar/date scaling. Placement-tile (27) and the broader console-parity extras (auto-run, portraits, etc.) were considered and declined for this pass.

## Scope (four features)

### Feature 1 — Replace the #76 force with native tool-hit checkboxes (11, 12)

**Today:** `ModEntry.EnforceToolHitLocationOptions()` force-sets `alwaysShowToolHitLocation=true` / `hideToolHitLocationWhenInMotion=false` **every tick** (gated by `Config.EnableToolHitLocation`). Once these become toggleable checkboxes, the per-tick force fights the user.

**Decision (user): retire the force and the GMCM toggle entirely — pure vanilla.** Expose the bare checkboxes at vanilla defaults; the user toggles them in-menu. (Options persist globally via `StartupPreferences`, so it's a one-time set, not per-save.)

**Changes:**
- Remove `EnforceToolHitLocationOptions()` and its call in `OnUpdateTicked` (`ModEntry.cs`).
- Remove `Config.EnableToolHitLocation` (`ModConfig.cs`) and its GMCM entry (`ModEntry.cs`).
- Inject `new OptionsCheckbox("Always show tool hit location", 11)` and `new OptionsCheckbox("Hide tool hit location while moving", 12)` into the `OptionsPage` `options` list via a ctor postfix.

**Labels:** the PC string keys (`Options_AlwaysShowToolHitLocation`, etc.) returned zero matches in the Android content, so labels are **hardcoded English**. (A `LoadString`-with-fallback is possible but unnecessary for a controller-parity mod.)

**Self-sync:** `OptionsCheckbox`'s ctor calls `setCheckBoxToProperValue(this)`, and `Options` handles 11/12 there (`Options.cs:1680/1683`) — so the checkboxes show the correct current state with no extra wiring. Toggling routes through `OptionsCheckbox.receiveLeftClick → changeCheckBoxOption` (handled at `:1068/1071`).

### Feature 2 — Inject the zoom control (18) — **device-gated**

**Inject** a zoom control into the list. It self-syncs (`OptionsSlider` ctor → `setSliderToProperValue` case 18 → `value = desiredBaseZoomLevel*100`, i.e. 50–100).

**Apply path — do NOT use `changeSliderOption(18)`.** That case is a degenerate ±10 stepper whose `value*100` math expects a 0-1 fraction, not the slider's 0-100 int (decompile `Options.cs:1235-1256`). The clean **absolute** setter is `changeDropDownOption(18, "<pct>%")` (`:1506-1518`), which sets `desiredBaseZoomLevel` directly and raises `Game1.forceSnapOnNextViewportUpdate`. So the injected control is an `OptionsSlider` (min 50, max 100, step 10), and `OptionsPagePatches`' Left/Right handler special-cases `whichOption == 18` to apply via `changeDropDownOption($"{newVal}%")` instead of `changeSliderOption`.

**The make-or-break device test (determines 3.9.0 vs 4.0):** does setting `desiredBaseZoomLevel` actually change the rendered zoom on mobile, or does pinch-zoom / `PinchZoom.Instance` / `Game1.NativeZoomLevel` override it? (TODO #12 warns it may.) After implementing, the user moves the zoom slider with the controller and reports whether the world zooms.
- **If it zooms:** ships in 3.9.0.
- **If pinch-zoom overrides it:** zoom moves to the 4.0 "Right Stick + Zoom" item, where it gets the custom wiring (e.g. also setting `PinchZoom.Instance.ZoomLevel` / forcing `pinchZoom=false`). Tool-hit (Feature 1) is unaffected and still ships in 3.9.0.

### Feature 3 — Hide the 5 touch-control options when a controller is active

**Hide set (locked):** `whichOption` `139` (Controls dropdown / virtual-joystick mode), `140` (show on-screen controls toggle), `146` (invisible-button width), `147` (pinch-zoom), and the **Adjust-joypad-controls** `OptionsButton` (matched by the page's public `optionsButtonAdjustControls` reference, not by label).

**Gate:** a new GMCM toggle `HideTouchOptionsWithController` (default **true**), AND `Game1.options.gamepadControls` is true. Gating on `gamepadControls` (not `GamePad.IsConnected`) matches the rest of `OptionsPagePatches` and sidesteps the Ayaneo `IsConnected=false` quirk.

**Mechanism:** the ctor postfix removes the matching entries from the `options` list. Removal shrinks the list, which **requires Feature 4** to fix the scroll extent.

### Feature 4 — Dynamic scroll-fit (the GMCM-button-below-the-floor bug)

**Root cause:** `OptionsPage` builds its `MobileScrollbox scrollArea` once in the ctor with `maxYOffset = ContentHeight - (height-16) + 50` (`OptionsPage.cs:257-260`). Entries added **after** the ctor (GMCM appends its button via its own patch) or removed by Feature 3 aren't reflected, so the scroll floor is wrong — GMCM's button sits below it and can't be reached by stick/scroll (only by dragging the scrollbar past the end).

**Fix:** a postfix on `OptionsPage.update` recomputes the scroll extent from the **current** `options` list and calls `scrollArea.setMaxYOffset(...)` when it changes. `ContentHeight` is replicated as `20 + Σ(options[i].ItemHeight + 20)` (`ItemHeight` is public on each element type); `maxYOffset = max(0, ContentHeight - (height-16) + 50)`. Recompute only when `options.Count` changes (cheap; catches GMCM's late append on the first frame it appears). `MobileScrollbox` is Android-only → reflected off the runtime object (same pattern already in `OptionsPagePatches`). This fix is always-on (a pure scroll-extent correctness fix; benefits touch users too).

## Architecture / file layout

New file **`Patches/OptionsPageInjectionPatches.cs`** owns "what's in the list and how far it scrolls":
- `OptionsPage` **ctor postfix** → inject 11/12 (+18 if shipping) and remove the hidden touch options.
- `OptionsPage.update` **postfix** → Feature 4 scroll-fit.

This stays separate from the existing **`Patches/OptionsPagePatches.cs`**, which owns "how the controller navigates the list" (snap-nav, A/B, Left/Right value adjust, stick scroll). They compose cleanly: Harmony allows multiple postfixes; the nav reads the `options` list live, so injected/removed entries are automatically reachable (`BuildInteractiveIndices` includes any `whichOption != -1`). The only cross-file touch is the zoom special-case in `OptionsPagePatches`' Left/Right handler (Feature 2).

All patch bodies wrapped in try/catch that falls through to vanilla; all Android-differing members via `AccessTools` with null-checks.

## Commit breakdown (one change each, PATCH 0.0.1)

1. **Retire #76 force + inject tool-hit checkboxes 11/12.** `ModEntry.cs`, `ModConfig.cs`, new `OptionsPageInjectionPatches.cs`. (Coherent single feature: "tool-hit becomes a native, user-controlled checkbox.")
2. **Dynamic scroll-fit.** `OptionsPageInjectionPatches.cs` update postfix. (Do this early — Features 1 and 3 both depend on correct scroll bounds.)
3. **Hide the 5 touch options + GMCM toggle.** `OptionsPageInjectionPatches.cs`, `ModConfig.cs`, `ModEntry.cs`.
4. **Inject zoom slider (18) + zoom apply special-case.** `OptionsPageInjectionPatches.cs`, `OptionsPagePatches.cs`. Then device-gate per Feature 2.

(Implementation order will likely run 1 → 2 so the injected checkboxes are reachable when first tested; 2 could also lead. The plan step will finalize.)

## Risks / open verifications

- **Zoom render override (Feature 2)** — the central unknown; device test decides 3.9.0 vs 4.0. Tool-hit is independent and safe.
- **GMCM append timing (Feature 4)** — recompute-on-count-change must catch GMCM's button whenever it's added; if GMCM adds it before our first update postfix, the count-change still fires on the frame it appears. Verify GMCM's button is reachable after the fix.
- **Snap-nav reachability** — confirm on device that injected entries are focusable with the D-pad/stick and that hidden entries are skipped (they're gone from the list, so `BuildInteractiveIndices` naturally excludes them).
- **Ctor postfix ordering vs other ShopMenu/OptionsPage mods** — low risk; we only append/remove on our own criteria.

## Out of scope (captured, not built)

- UI scale (39), placement-tile (27), and the console-parity extras (auto-run, portraits, menu bg, dialogue font, audio toggles, stowing mode) — declined this pass.
- Localized labels for injected options.
