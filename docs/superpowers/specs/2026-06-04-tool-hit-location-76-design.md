# #76 Console "Always Show Tool Hit Location" (red target box)

**Date:** 2026-06-04
**Item:** TODO #76 — part of the 3.9.0 console-parity bundle.
**Status:** design approved; Phase 1 implementing (device test pending reconnect).

## Problem

Console always shows a red box on the tile your tool will hit (hoe/water/pickaxe/axe). On Android with a controller this never appears, so you can't see what tile you're about to work.

## Root cause (verified from the Android decompile)

The marker and its draw path are fully present but never triggered for a controller:

- `Options.alwaysShowToolHitLocation` exists (`Options.cs:201`) and defaults **false** on Android (`:867`).
- The draw gate (`Farmer.draw`, `Farmer.cs:6364`) requires `LeftShift held || alwaysShowToolHitLocation`. A controller never holds LeftShift, so with the option off the marker never draws.
- When it does draw (`:6366-6368`), it renders `mouseCursors` tile 29 (the red target box) at `Utility.clampToTile(GetToolLocation(getMousePosition() + viewport))`.
- The sibling option `hideToolHitLocationWhenInMotion` defaults **true** (`:868`) — hides the marker while walking.
- The marker only shows for tools where `CurrentTool.doesShowTileLocationMarker()` is true (hoe, watering can, pickaxe, axe; slingshot etc. return false).

## Design

### Phase 1 — flip the options, let the engine draw

When `EnableToolHitLocation` is on, force:
- `Game1.options.alwaysShowToolHitLocation = true`
- `Game1.options.hideToolHitLocationWhenInMotion = false` (user wants it visible even while moving — pairs with #25 charge-while-moving)

Set via a guarded enforce method called from `ModEntry.OnUpdateTicked` (only writes when the current value differs, so it's cheap and also keeps the value stuck against the in-game Options page). Both are vanilla `Options` bools present on the PC DLL — direct access, no reflection. Same "fix-the-data, let the engine render" pattern as #25b's `useLegacySlingshotFiring` flip — no custom drawing.

**Toggle:** new GMCM `EnableToolHitLocation` (default true). When off, leave the options alone (vanilla Android).

### Device verification (the one real unknown)

The draw uses `GetToolLocation(getMousePosition() + viewport)` (the 2-arg overload, `ignoreClick: false`), **not** the gamepad-aware parameterless overload. On a controller the box position therefore depends on where `getMousePosition()` resolves:
- `Character.GetToolLocation(target, ignoreClick:false)` returns the **facing-adjacent tile** when `target` maps to the player's own tile (`Character.cs:1166-1179`), else it follows `target`.
- So if the Android virtual cursor parks on/near the player, the box tracks facing correctly; if it sits elsewhere, the box follows the phantom cursor.

**Verify on device:** red box appears for hoe/watering-can/pickaxe/axe, lands on the tile you're **facing**, follows as you turn, and stays visible while moving.

### Phase 2 — only if the box tracks a phantom cursor

Patch the marker draw (or feed the position) to use the facing tile explicitly — `GetToolLocation(ignoreClick: true)` resolves to the facing-adjacent tile when no mouse is visible / a gamepad button is held. Prefer the minimal intervention; avoid heavy patching of the hot `Farmer.draw` path unless the device test proves it necessary.

## Scope

- Tool-hit marker only (tools with `doesShowTileLocationMarker() == true`).
- **NOT seeds** — #73 (seed placement box) is a separate `Object.drawPlacementBounds` path, not this marker, despite the similar look. Corrected from the earlier "folds into #76" note.
- Out of scope: changing the marker sprite/color; per-tool customization.

## Verification

Device playtest: equip a hoe/watering can/pickaxe/axe → red box on the facing tile, turns with you, visible while moving; switch to a non-marker tool (slingshot) → no box. Toggle `EnableToolHitLocation` off → box gone.
