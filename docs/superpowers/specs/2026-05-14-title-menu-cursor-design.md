# Design: Title/Main Menu Cursor Fix (#17)

**Date:** 2026-05-14
**Milestone:** v3.8.0 — Console Parity: Quick Wins
**TODO item:** #17 — Title/Main Menu Cursor Fix
**Target version:** v3.7.2

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
