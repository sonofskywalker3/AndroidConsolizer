# v4.0 "The Right Stick Update" — Design Spec (#12 + #62)

**Date:** 2026-06-04
**Status:** Design approved; ready for implementation plan.
**Feeds:** TODO #12 (Right Joystick — Full Console Behavior), #62 (Right-Stick Furniture Ghost).
**Research basis:** `docs/superpowers/specs/2026-06-04-right-stick-console-behavior-research.md`.
**Decompile basis:** `…/decompiler/stardew-valley-android/decompiled/StardewValley/StardewValley/Game1.cs` (verified ranges cited inline).

---

## Scope (decided with user 2026-06-04)

v4.0 ships **two** of the five right-stick behavior classes:

1. **Overworld cursor + tool targeting** (the headline / foundation).
2. **Placement ghost (#62)** — furniture / carpenter building ghost follows the cursor.

**Explicitly out of v4.0** (may become later patches or stay deferred):
- Menu scroll (right stick scrolls scrollable menus). Partially exists already (ShopMenu buy-list, Social tab via `RawRightStickY`); not extended here.
- R3 click = chat / hold = emote wheel.
- Minigame right-stick → D-pad translation.

These exclusions are deliberate: the overworld cursor is the foundation the placement ghost builds on, and the user wants those two shipped as the v4.0 core. The others add surface area without serving the headline.

---

## Headline engineering finding (decompile-verified)

The console overworld cursor **already exists, complete, in the Android build** and is **engine-native, not the #18 menu trap**:

- `Game1.UpdateControlInput` (`Game1.cs:13298-13334`): when `options.gamepadControls`, if `|Right.X|>0 || |Right.Y|>0` it calls `setMousePositionRaw(mouseX + Right.X*thumbstickToMouseModifier, mouseY - Right.Y*thumbstickToMouseModifier)` and sets `timerUntilMouseFade = 4000`. This is the entire overworld cursor-move behavior.
- `_mobileUpdateControlInput` is called **from inside** `UpdateControlInput` (`Game1.cs:13614`), *after* the cursor block — they are **not** mutually exclusive. The cursor block runs every tick `UpdateControlInput` runs (`Game1.cs:5076`).
- `Game1.drawMouseCursor` (`Game1.cs:15618-15692`) draws the overworld cursor and fades it natively: it decrements `timerUntilMouseFade` while no menu, and `if (options.gamepadControls && timerUntilMouseFade <= 0 && activeClickableMenu == null …) mouseCursorTransparency = 0f` hides it once the 4s expires. This is a **different** path from the menu-context `drawMouse` that the #18 museum cursor had to fight (that one read control type = TOUCH). The overworld cursor is gated only on `options.gamepadControls` + `timerUntilMouseFade`.

**Why it doesn't work today:** AC's `GameplayButtonPatches.GetState_Postfix` zeros the right thumbstick whenever `SuppressRightStickInOverworld == true && Game1.activeClickableMenu == null` (`GameplayButtonPatches.cs:405-411`). `input.GetGamePadState()` routes through that patch, so `UpdateControlInput` sees a zeroed stick and never moves the cursor. The original suppression was added because cursor drift made "interact/sickle target tiles many squares away" — **that distant-tile targeting *is* the console cursor-targeting behavior**; it only felt broken because the cursor was invisible/suppressed. Make the cursor visible + auto-hiding and the "bug" becomes the feature.

**Consequence:** this is "stop starving the existing engine path + confirm it draws," not "build a cursor system." Aligns with the project philosophy: *fix the data, don't fight the engine.*

---

## Approach (decided: C — diagnose first, lean on the engine, patch only gaps)

Diagnostic-first always (user directive). Lean on the native path; add mod code only where a runtime diagnostic proves the engine path falls short.

### Phase 0 — Diagnostic (own commit)
- New `Patches/RightStickCursorPatches.cs`.
- A `VerboseLogging`-gated, once-per-tick line emitted while the right stick is non-zero in the overworld, logging: `options.gamepadControls`, `timerUntilMouseFade`, `mouseCursorTransparency`, `lastCursorMotionWasMouse`, live `getMouseX()/getMouseY()`, and whether the suppression lift is active.
- Temporarily relax the overworld right-stick suppression (gate it so the diagnostic build lets the engine path run) so we can observe whether the engine moves + draws the cursor on the G Cloud.
- **Goal / specific questions answered:**
  1. Is `options.gamepadControls` actually `true` at runtime on the G Cloud? (If false, the whole engine path no-ops and Phase 1 must force it true.)
  2. Once the stick is un-zeroed, does `getMouseX/Y` actually move?
  3. Does `mouseCursorTransparency` go > 0 (cursor visible) and fade to 0 after ~4s?
- **No user playtest** — engineering correctness read from the log.

### Phase 1 — Enable the overworld cursor (own commit, or a small handful of `0.0.1` commits)
Driven by Phase 0 results. Expected to collapse to:
1. **Lift suppression:** in `GameplayButtonPatches.GetState_Postfix`, do **not** zero the right thumbstick in the overworld when `EnableRightStickCursor == true` (let the engine move the cursor). Keep zeroing when the toggle is off (preserves the old drift-suppression as the opt-out).
2. **Force `options.gamepadControls = true`** only if Phase 0 shows it's off (guarded per-tick enforce in `ModEntry.OnUpdateTicked`, same pattern as #76/#25b's data-forcing). If Phase 0 shows it's already true, skip this.
3. **Self-draw fallback** (#18 pattern) **only if** Phase 0 shows the engine draw is suppressed despite the above. Otherwise rely on native `drawMouseCursor`.
4. Confirm: cursor visible while stick active, auto-hides after 4s reverting tool targeting to the facing tile (native behavior), tools (hoe/can/pickaxe/axe) + interact aim at the cursor tile when active.

### Phase 2 — Placement ghost (#62) (own commit)
- **Verify-first:** with the cursor live, furniture / craftable / seed placement already draws its single ghost/box at the *target tile*, and the target tile is derived from the mouse position — so the ghost should follow the cursor automatically once Phase 1 lands. Confirm on device before writing any ghost code.
- **Wire only if it doesn't fall out:** the CarpenterMenu building ghost uses a `GetMouseState` override (`CarpenterMenuPatches`, the gold-standard ghost-follow pattern). If the building ghost doesn't track the right stick after Phase 1, feed the right-stick cursor into that override.
- **Buttons:** A = place, B = cancel (return furniture to inventory) — console parity, matches existing behavior.

---

## Config

- **Rename** `ModConfig.SuppressRightStickInOverworld` (default `true`) → **`EnableRightStickCursor`** (default **`true`**). Semantics **flip**:
  - `EnableRightStickCursor = true` → console cursor active (right stick un-suppressed, cursor drives targeting).
  - `EnableRightStickCursor = false` → old behavior (right stick zeroed in overworld; no cursor drift).
- **Migration:** existing configs with `SuppressRightStickInOverworld = true` (the old default) map to `EnableRightStickCursor = true` (cursor **on**) — this is the parity intent for the update. If a user had explicitly set `SuppressRightStickInOverworld = false` (they *wanted* the drift/cursor), that also maps to `EnableRightStickCursor = true`. Net: everyone gets the cursor on after update, which is the intended v4.0 default; the toggle lets them turn it off. (Because both old values converge on "cursor on," a literal value-preserving migration isn't meaningful — we just default the new flag to `true`. Note the rename in the changelog so power users know the toggle moved.)
- **GMCM wording:** *"Enable Right-Stick Cursor — Right stick moves an on-screen cursor that aims your tools, matching Switch. Auto-hides after a few seconds of no input. Turn off to keep the right stick from moving the cursor."*
- **Cursor speed:** mirror console `Game1.ComputeCursorSpeed` / `thumbstickToMouseModifier` exactly. **No sensitivity slider in v4.0** (deferred to a follow-up patch if device testing shows a need).

---

## Carve-outs / non-regression

- **Slingshot (#25b):** when the active tool is a `Slingshot` being aimed, the right stick must **not** drive the cursor (aim is the left stick on console + in AC #25b). The suppression-lift early-outs (keeps zeroing the right stick) when `Game1.player.CurrentTool is Slingshot` in the aiming state, so `SlingshotAimPatches` is untouched.
- **Menus:** the overworld lift is gated on `Game1.activeClickableMenu == null`, so the existing menu right-stick uses (`ShopMenuPatches` buy-list scroll, `GameMenuPatches` Social tab — both poll `RawRightStickY` and apply their own suppression) are unaffected. Menu scroll is explicitly out of v4.0 scope.
- **`RawRightStickX`:** only `RawRightStickY` is cached today (`GameplayButtonPatches.cs:22`). Add `RawRightStickX` caching (mirror the Y caching at `:351` and the cached-state restore sites) for any self-driven fallback or ghost math. Harmless if unused.

---

## Files

| File | Change |
|------|--------|
| `Patches/RightStickCursorPatches.cs` (new) | Phase 0 diagnostic; Phase 1 enforcement/fallback if needed. |
| `Patches/GameplayButtonPatches.cs` | Gate the overworld right-stick zeroing behind `!EnableRightStickCursor` (lift suppression when cursor on); add `RawRightStickX` caching. |
| `ModConfig.cs` | Rename flag → `EnableRightStickCursor` (default `true`). |
| `ModEntry.cs` | GMCM entry reworded; per-tick `gamepadControls` enforce **only if** Phase 0 requires it. |
| `Patches/FurniturePlacementPatches.cs` / `Patches/CarpenterMenuPatches.cs` | Phase 2, **only if** the ghost doesn't follow the cursor automatically. |

---

## Testing

- **Phase 0:** engineering-only. Read `test-output/SMAPI-latest.txt` (pulled via `../SyncdewValley/sync.ps1 logs`) — confirm `gamepadControls`, cursor motion, transparency/fade.
- **Phase 1:** engineering correctness from the log (cursor moves, fades after 4s, targeting follows). **One meaningful playtest** reserved for feel: does cursor-tile tool targeting + 4s auto-hide feel like the Switch? (Subjective, not yes/no.)
- **Phase 2:** furniture / building / seed placement ghost tracks the right-stick cursor; A places, B cancels; regression-check that placement still looks right with the cursor faded.
- **Cross-cutting regression:** slingshot aim still uses the left stick (no cursor interference); menus unchanged; turning `EnableRightStickCursor` off restores the no-drift behavior.

---

## Decompile reference (Android, verified)

All in `…/decompiled/StardewValley/StardewValley/Game1.cs`:
- `UpdateControlInput` cursor block: **13303-13334** (gate `options.gamepadControls`; `setMousePositionRaw` at 13308; `timerUntilMouseFade = 4000` at 13332).
- `UpdateControlInput` called at **5076**; it calls `_mobileUpdateControlInput` at **13614** (nested, after the cursor block).
- `thumbstickToMouseModifier`: **1986-1996** (`_cursorSpeed/720f * viewport.Height * ElapsedGameTime.TotalSeconds`; private static → reflect if driving manually).
- `drawMouseCursor`: **15618-15692** (fade decrement 15620-15624; hide-on-expire 15625-15628).
- `setMousePositionRaw`: public static (research: `~5532`).
- Constants: `timerUntilMouseFade` (public static int), `lastCursorMotionWasMouse` (public static bool), `emoteMenuShowTime = 250` (unused in v4.0), `rightStickHoldTime` (unused in v4.0).

## Open questions resolved into the plan
- **gamepadControls at runtime?** → Phase 0 diagnostic answers it; Phase 1 forces it only if false.
- **Does the engine draw the overworld cursor or is it suppressed like #18?** → decompile says it's a separate native path (drawMouseCursor, not menu drawMouse); Phase 0 confirms on device; self-draw fallback held in reserve.
- **Does #62 fall out of #12?** → Phase 2 verifies before writing ghost code.
