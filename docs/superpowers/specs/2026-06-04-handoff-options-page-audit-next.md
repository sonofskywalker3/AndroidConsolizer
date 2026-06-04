# Handoff — 3.9.0 parity bundle in progress; NEXT = audit + inject stripped vanilla Options into the Android Options page

**Date:** 2026-06-04
**Tip:** v3.8.21 (committed to `master`, local only — NOT pushed/released)
**Device:** G Cloud, **loaded with v3.8.21**, VerboseLogging ON. ADB can drop to wireless/idle between sessions — reconnect USB if `adb devices` is empty.

---

## Release-packaging plan (decided this session)
- **3.9.0 = the next public release**, bundling everything unreleased since 3.8.0: **#25** (charge while moving, done v3.8.13), **#25b** (slingshot, done v3.8.16), **#74** (drop-blocker, done v3.8.17), **#76** (tool-hit-location, done v3.8.18), **#73** (seed box, fix-attempt v3.8.20), **#75** (shop scroll, fix-attempt v3.8.21). We do NOT cut a release per 3.8.x patch.
- **4.0 = The Right Stick Update** (#12 right-stick cursor + zoom, #62 furniture ghost) — should land ~100% console parity.
- **Better-than-console extras (e.g. dual-stick slingshot) → their own separate mods**, NOT AC. See memory `feedback_ac_console_parity_only_extras_are_separate_mods`.

## REVISED push rule (this session) — IMPORTANT
- **Pushing commits to GitHub is now allowed anytime — `git push` does NOT need approval.** Only **RELEASES** (`gh release create`, Nexus publish/upload) require an explicit "yes." See memory `feedback_no_auto_publish` (revised 2026-06-04). The workspace CLAUDE.md files still say the older stricter wording — memory is authoritative.

## Status of the 3.9.0 items
| # | What | Status |
|---|------|--------|
| #25 | Charge tools while moving | ✅ done, device-verified |
| #25b | Slingshot combat (console aim) | ✅ done, device-verified |
| #74 | Console drop-blocker | ✅ done, device-verified ("same as switch") |
| #76 | Always-show-tool-hit-location | ✅ **device-verified GOOD this session** ("tool hit is good") |
| #73 | Seed single-box placement preview | 🔧 fix-attempt v3.8.20, **awaiting device test** |
| #75 | Shop buy-list jumps to top on purchase | 🔧 fix-attempt v3.8.21, **awaiting device test** |

**Pending device tests (user said "test after dinner"; #76 already confirmed good):**
- **#73:** hold seeds → single red/green box on the target tile, NO full-screen green map, NO seed ghost; box green on plantable dirt / red elsewhere. *If the box is always red even on good dirt → swap `Utility.playerCanPlaceItemHere` for the seed-specific validity check (`itemCanBePlaced` / HoeDirt).* File: `Patches/FurniturePlacementPatches.cs`.
- **#75:** open a shop, scroll DOWN the buy list, buy an item that's below the top → list should **stay put** (not snap to top), selection stays on-screen; buy out a stack → re-selects next item, stays on-screen. `[ShopScroll]` VerboseLogging diagnostic is in to verify from the log. Rule out a CartCatalog `ShopMenu` cross-mod interaction if it still misbehaves. File: `Patches/ShopMenuPatches.cs`.
- **Pull the log** after the user tests (`cd ../SyncdewValley; pwsh -NoProfile -File sync.ps1 logs`) and read `[ShopScroll]` + confirm no errors.

---

## NEXT TASK (why this handoff exists) — Restore stripped vanilla Options to the Android Options page

**User ask (verbatim, 2 messages):** "can you make those two options show up in the in game menu at all?" … then "any other missing options that should just work if they were visible? screen zoom maybe? That would be a huge find for controller." And: "they are on console, but I don't think I've seen them in mobile."

**The idea:** The Android port STRIPPED a bunch of vanilla options from its in-game `OptionsPage` list, but the `Options` engine still HANDLES many of them internally. Any option that's *handled but not shown* would "just work" if we inject its `OptionsCheckbox`/`OptionsSlider`/`OptionsDropDown` back into the page. This is pure console parity (these exist in the console Options menu). **Zoom is the prize** — a controller-usable zoom slider would be huge (Android only has pinch-to-zoom).

### What I already found (verify, don't re-derive blindly)
- **Android `OptionsPage` builds its list** with `options.Add(new OptionsCheckbox(label, whichOption))` / `OptionsSlider` / `OptionsDropDown` in the ctor/populate (`decompiled/StardewValley/StardewValley.Menus/OptionsPage.cs`, the block starting ~line 100; the `options.Add(...)` calls run ~line 145-250). The shown `whichOption`s I saw: 140, 146(slider), button, 1, 2, 20, 21, 42(dropdown), 43, 135, 137, 138, 141, 151, (46 ru-only), 34, 147, 23, 24, 133… — **NOT** 11/12 (tool-hit) and **NOT** 18 (zoom).
- **Tool-hit options ARE handled by `Options`:** whichOption **11** = `alwaysShowToolHitLocation`, **12** = `hideToolHitLocationWhenInMotion` (consts `Options.cs:63/65`; fields `:201/203`; apply switch `~:1069/1072`; display-sync `setCheckBoxToProperValue` `~:1681/1684`). So injecting `OptionsCheckbox(11)` + `OptionsCheckbox(12)` should just work.
- **Zoom (whichOption 18 = `Options.zoom`) IS referenced across `Options.cs`** — apply `case 18` at `~1235-1254` and `~1506-1511` (sets `desiredBaseZoomLevel`), display-sync `case 18` at `~1790-1792`, `~1828-1829`, `~1897`, `~2054`. **BUT TODO #12 warns zoom may NOT "just work"** on Android ("Must subclass `OptionsSlider` with own value management since Android may not wire game's zoom handling"; mobile uses pinch-zoom + `PinchZoom.Instance`, `FetchZoom`, `Game1.NativeZoomLevel`). **MUST verify on device** whether setting `desiredBaseZoomLevel` via the injected slider actually changes the render zoom on mobile, or whether the pinch-zoom/viewport system overrides it. This is the crux for the "huge find."
- **Label strings problem:** the PC label keys (e.g. `Strings\UI:Options_AlwaysShowToolHitLocation`) returned **zero matches** in the Android decompile — the mobile content likely stripped those strings. Injected options may need **hardcoded English labels** (or hunt for surviving keys). Check before assuming `LoadString` resolves.

### Open design decisions (brainstorm with the user)
1. **#76 force vs. native-menu conflict (MUST resolve):** #76 currently FORCES `alwaysShowToolHitLocation=true` + `hideToolHitLocationWhenInMotion=false` **every tick** via `ModEntry.EnforceToolHitLocationOptions()` (called in `OnUpdateTicked`). If those options become toggleable in the in-game menu, the per-tick force will fight the user (snap their toggle back). Reconcile: stop the per-tick force and apply a **one-time default-on** (tracked by a config flag), letting the native menu own them thereafter — and likely retire the GMCM `EnableToolHitLocation` toggle in favor of the native checkbox. Decide with the user.
2. **Scope for 3.9.0 vs 4.0:** tool-hit checkboxes (11/12) are cheap and clearly 3.9.0. **Zoom (18) overlaps the v4.0 "#12 Right Joystick Cursor + Zoom" item** — if zoom "just works" via injection it could be pulled into 3.9.0, but if it needs the custom-slider wiring it belongs in 4.0. Let the device verification decide.
3. **Audit completeness:** enumerate ALL `whichOption`s handled in `Options.cs` (the `changeCheckBoxOption`/slider/dropdown apply switches + the `setCheckBoxToProperValue`/`setSliderToProperValue`/etc. display-sync switches) and diff against the `OptionsPage` shown list. Present the user the full list of "stripped but functional" candidates before injecting (some stripped options were stripped *deliberately* because they're meaningless on mobile — judge each).

### Implementation notes
- Inject via a Harmony **postfix on the Android `OptionsPage` ctor** (or whatever method populates `options`) that appends the chosen `OptionsCheckbox`/`OptionsSlider` entries. Resolve the method via `AccessTools` (Android-vs-PC reflection safety).
- **`OptionsPagePatches.cs` already patches `OptionsPage`** for controller snap-navigation + left-stick suppression. Injected options must integrate with that snap nav (it walks the `options` list, so appended entries should be reachable — verify on device). Also watch scroll bounds (TODO #12 noted the page can clip long lists).
- One change per commit + version bump (PATCH 0.0.1). Build: `dotnet build AndroidConsolizer.csproj -c Release`. Deploy: `cd ../SyncdewValley; pwsh -NoProfile -File sync.ps1 deploy`. Pull logs: `… sync.ps1 logs`.

---

## Workspace rules (unchanged)
- **Diagnose-first:** read the decompile (`C:\Users\Jeff\Documents\Projects\decompiler\stardew-valley-android`) before any fix; state the root cause in plain English first.
- `AccessTools` + null-checks for Android-differing members; wrap patch bodies in try/catch that falls through to vanilla.
- Bump `manifest.json` + commit one change per patch; `git add <specific files>` (never `-A`). **Local commits OR pushes are fine now; only releases need a yes.**
- **NEVER `/sdcard/`** — use `/storage/emulated/0/`.
- **Deploy + log-pull are the agent's job.** The user only does in-game tests on the G Cloud (ControllerLayout **Xbox** + ControlStyle **Switch** → A/B swap active in gameplay, X/Y swap active **in menus only**). Reserve playtests for meaningful feedback; verify engineering correctness from the log yourself.
- Test save: `Cheatside_428888887` on the G Cloud has a basic Slingshot + stone ammo injected (backpack); fine to keep.
