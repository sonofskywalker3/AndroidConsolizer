# Design: Title/Main Menu Cursor Fix (#17)

**Date:** 2026-05-14
**Milestone:** v3.8.0 — Console Parity: Quick Wins
**TODO item:** #17 — Title/Main Menu Cursor Fix
**Target version:** v3.7.4 (originally v3.7.2; revised after device diagnostic)

> ## Revision — 2026-05-15
>
> The v3.7.2 fix shipped from this spec did **not** work on device. A v3.7.3
> diagnostic build (state-change log + unconditional `mouseCursorTransparency = 1f`
> with no gates and no cursor-move) revealed that the spec's root cause was
> wrong in two big ways:
>
> 1. **`Game1.options.snappyMenus = True` on the Ayaneo.** The "snap to default
>    never runs" theory is dead — `currentlySnappedComponent = 81116` (Load
>    button) is set from frame 1 of the title menu. The position layer was never
>    broken on this device. (The project-wide "snappyMenus is False on Android"
>    assumption baked into `OptionsPagePatches.cs` comments evidently doesn't
>    hold for Ayaneo handhelds — likely a per-device or settings difference.)
> 2. **`Game1.mouseCursorTransparency = 1f` from the first frame.** The
>    "transparency starts at 0 until first stick input" theory is also dead —
>    transparency is already 1 at title load. The DONE.md #40a precedent does
>    not apply *to this trigger*; v3.7.2's force-set was a no-op against an
>    already-correct value.
>
> Yet the cursor is still invisible until first stick input. At the moment the
> player is sitting on the title screen: transparency = 1, mouse positioned on
> the Load button, `gamepadControls = True`, `snappyMenus = True`, no sub-menu —
> all the input data is correct and the cursor is still not rendered. That is a
> **draw-path problem**: the game's own `drawMouse` on the Android title screen
> is producing no visible output despite correct input state. (Why is unclear
> from static reading — `ShouldDrawCursor()` should return true with the
> observed state. Likely either some hidden timer/state we didn't capture,
> Android-specific suppression inside `drawMouse`, or a draw at a position the
> player can't perceive as a cursor.)
>
> Side finding from the diagnostic: `GamePad.GetState(PlayerIndex.One).IsConnected
> = False` on the Ayaneo despite the built-in controller. The Ayaneo's gamepad
> doesn't surface through XInput; `Game1.options.gamepadControls` is `True` via
> the `gamepadMode = Auto` setting. **`GamePad.IsConnected` is not a reliable
> gate signal on this device** — use `Game1.options.gamepadControls`.
>
> ### Corrected approach (replaces "Approach A" below)
>
> Use **Approach B** (originally listed as a fallback): draw the mouse cursor
> sprite ourselves in a `TitleMenu.draw(SpriteBatch)` postfix, bypassing the
> game's broken-on-title-Android `drawMouse`. The state-supply work from
> Approach A is removed — it was solving non-problems. Same pattern as
> `Patches/ShopMenuPatches.cs` (DONE.md #40a, shop sell-tab cursor) but
> triggered for a different reason: there it was transparency = 0; here it is
> `drawMouse` producing no output.
>
> Implementation summary:
>
> - Replace the v3.7.2 `TitleMenu.update()` postfix entirely (it was ineffective).
> - New Harmony **postfix** on `TitleMenu.draw(SpriteBatch)`.
> - Gate: `Game1.options.gamepadControls && !Game1.lastCursorMotionWasMouse`
>   AND `__instance.titleInPosition` AND `TitleMenu.subMenu == null`.
>   (`GamePad.IsConnected` deliberately NOT used — unreliable on Ayaneo.)
>   (`buttonsToShow >= numberOfButtons` deliberately NOT a gate — the v3.7.3
>   log showed the title can be in `titleInPosition = True` while
>   `buttonsToShow < numberOfButtons`; rejecting that state would leave the
>   cursor invisible even when the player can see the menu.)
> - Action: `b.Draw(Game1.mouseCursors, new Vector2(Game1.getMouseX(), Game1.getMouseY()), Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, cursorTile, 16, 16), Color.White, 0f, Vector2.Zero, 4f + Game1.dialogueButtonScale / 150f, SpriteEffects.None, 1f);`
>   where `cursorTile = Game1.options.snappyMenus ? 44 : Game1.mouseCursor`
>   (matches the `Patches/ShopMenuPatches.cs:1207` precedent).
> - Target version: **v3.7.4** (v3.7.3 was the diagnostic).
>
> The rest of the original spec below is retained for historical context but
> should be read as **superseded** by this revision.

## Problem

When the title/main menu loads with a controller in use, no cursor is visible.
The cursor only appears after the player moves the stick or presses a button.
Expected (console) behavior: the cursor is visible on the **Load** button by
default the moment the title screen settles (or **New** for a fresh save).

Confirmed on-device (Ayaneo Pocket Air Mini): the cursor is *completely
invisible* until first stick input, and once it appears, navigation between the
title buttons works correctly. So this is purely an initial-state bug — the
navigation system itself is fine.

## Root cause

Confirmed against the decompiled Android source
(`StardewValley.Menus.TitleMenu`, `StardewValley.Menus.IClickableMenu`,
`StardewValley.Game1`). Two layers, both stemming from `Game1.options.snappyMenus`
being `false` on Android:

1. **Position.** `TitleMenu.snapToDefaultClickableComponent()` sets
   `currentlySnappedComponent` to the Load button (`region_loadButton`, ID
   `81116`) if `startupPreferences.timesPlayed > 0`, otherwise the New button
   (`region_newButton`, ID `81115`), then calls
   `snapCursorToCurrentSnappedComponent()` to move the mouse there. Every call
   site that runs this at title-menu setup is gated behind
   `Game1.options.snappyMenus && Game1.options.gamepadControls`. Because
   `snappyMenus` is `false` on Android, it never fires — `currentlySnappedComponent`
   stays null and the cursor is never positioned on a button.
2. **Visibility.** `mouseCursorTransparency` sits at `0` on the title screen
   until the player's first stick input. `TitleMenu.ShouldDrawCursor()` returns
   `true` early on Android (the `!Game1.options.snappyMenus` branch), so the
   cursor *is* drawn — but `drawMouse(b, ignore_transparency: false, cursor)`
   respects `mouseCursorTransparency`, so it renders at 0% opacity. This is the
   same transparency trap documented in `DONE.md` #40a (shop sell-tab cursor).

`IClickableMenu.snapCursorToCurrentSnappedComponent()` calls
`Game1.setMousePosition()`, which only moves the cursor — it does **not** touch
`mouseCursorTransparency`. So the two layers are independent and both must be
addressed.

## Decision

**Approach A — supply the missing state every frame from a `TitleMenu.update()`
postfix.** This is a "fix the data" change: we provide exactly the state the
game would have if `snappyMenus` were true, then let the game's own draw code
render normally. No input interception, no custom cursor rendering, no
navigation override.

### Resolved questions

1. **Detection signal:** `Game1.options.gamepadControls && !Game1.lastCursorMotionWasMouse`
   — a controller is active and the last input was not touch/mouse. Same gate
   used by #22b.
2. **Which button:** Whatever the game's own `snapToDefaultClickableComponent()`
   chooses (Load if `timesPlayed > 0`, else New). We call the game's method
   rather than reimplementing the choice.
3. **GMCM toggle:** None. No existing toggle fits the title menu. Straight bug
   fix; add a toggle later only if a user complains. (Same call as #22b.)
4. **Scope of effect:** Gamepad only. Pure-touch users keep vanilla behavior
   (no cursor on the title screen).
5. **When the cursor appears:** Only once the title intro animation has settled
   and the main button row is interactive — not during the logo swipe / viewport
   rise.

## Implementation

New patch file: `Patches/TitleMenuPatches.cs`.

Single Harmony **postfix** on `TitleMenu.update(GameTime)`. Each frame the
postfix runs:

1. **Gate — controller active, not touch:** return early unless
   `Game1.options.gamepadControls && !Game1.lastCursorMotionWasMouse`.
2. **Gate — title settled, main button row only:** return early unless
   `__instance.titleInPosition` is true, `__instance.buttonsToShow >= TitleMenu.numberOfButtons`,
   and `TitleMenu.subMenu == null`.
3. **Position:** if `__instance.currentlySnappedComponent == null`, call
   `__instance.snapToDefaultClickableComponent()` (the game's own method —
   snaps to Load/New and moves the cursor there).
4. **Visibility:** set `Game1.mouseCursorTransparency = 1f`.

Running every frame means that if Game1's update loop resets
`mouseCursorTransparency`, the postfix re-sets it the same frame. The position
snap fires only once (guarded on `currentlySnappedComponent == null`), so it
never fights the player's own navigation once they start moving. The
`!lastCursorMotionWasMouse` gate means a screen tap immediately stops the
postfix from forcing the cursor, so vanilla touch behavior is preserved.

All fields and methods involved are **public** — no reflection needed:

| Member | Type | Declared on |
|--------|------|-------------|
| `TitleMenu.titleInPosition` | `public bool` | `TitleMenu` |
| `TitleMenu.buttonsToShow` | `public int` | `TitleMenu` |
| `TitleMenu.numberOfButtons` | `public static int` | `TitleMenu` |
| `TitleMenu.subMenu` | `public static IClickableMenu` | `TitleMenu` |
| `currentlySnappedComponent` | `public ClickableComponent` | `IClickableMenu` |
| `snapToDefaultClickableComponent()` | `public override void` | `TitleMenu` |
| `Game1.mouseCursorTransparency` | `public static float` | `Game1` |

Register the patch in `ModEntry.cs` alongside the other patch classes.

### Alternatives considered and rejected

- **Approach B — split fix: one-time snap + draw the cursor ourselves** (the
  `DONE.md` #40a pattern). More code, and only needed if forcing
  `mouseCursorTransparency = 1f` every frame fails to "stick" against Game1's
  own update logic. Kept as the fallback if the device test reveals flicker.
- **Approach C — diagnostic build first.** The root cause is already understood
  from the decompile; a logging-only build would add a round trip without new
  information. Approach A's every-frame re-set already sidesteps the only real
  uncertainty (whether setting transparency sticks).

## Non-goals

- No change to title-menu navigation, button neighbor wiring, or the intro
  animation — navigation already works once the cursor is visible.
- No custom cursor rendering — we rely on the game's own `drawMouse`.
- No effect on title sub-menus (Load Game is #35; Co-op, About, Language, etc.
  are not in scope).
- No GMCM toggle.
- No new behavior for pure-touch input.

## Testing

Device test on the Ayaneo Pocket Air Mini:

1. Cold-launch the game to the title screen with an **existing save**. Once the
   intro animation settles, confirm the cursor is visible on the **Load** button
   with no stick input.
2. If a fresh-save state is testable, confirm the cursor defaults to the **New**
   button instead.
3. Navigate the main row (New / Load / Co-op / Exit) and the corner About /
   Language buttons with the stick, and confirm A still selects — navigation is
   unchanged.
4. Tap the touchscreen and confirm the cursor hides (vanilla touch behavior
   preserved), and touch selection still works.
5. Confirm the cursor does **not** appear during the title intro animation —
   only after it settles.
6. Pull the SMAPI log and confirm `TitleMenu patches applied.` appears at trace
   level with no patch-application or postfix errors.

## Files

| File | Change |
|------|--------|
| `Patches/TitleMenuPatches.cs` | New — Harmony postfix on `TitleMenu.update(GameTime)` |
| `ModEntry.cs` | Register the new patch class |
| `manifest.json` | Version bump to 3.7.2 |
