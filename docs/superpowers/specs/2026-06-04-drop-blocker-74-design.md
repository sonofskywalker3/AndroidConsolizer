# #74 Console Drop-Blocker (don't instantly re-grab your own drops)

**Date:** 2026-06-04
**Item:** TODO #74 — part of the 3.9.0 console-parity bundle.
**Status:** design approved; Phase 1 implementing.

## Problem

On Android, dropping an item and immediately re-collecting it is instant — often before you've even left the menu. On console there's a blocker: you can't re-grab your own drop right away.

## Root cause (verified from the Android decompile)

The console blocker mechanism is fully present on Android but **dormant**: `Debris.DroppedByPlayerID` (`Debris.cs:89`) is declared and read by the exclusion logic (`Debris.cs:682` — while `timeBeforeReturnToDroppingPlayer > 0` (1200ms, `:111`), a farmer whose ID equals `DroppedByPlayerID` is rejected as a pickup/magnet target), but it is **assigned nowhere in the entire decompile** (grep: one declaration, zero writes). So it stays `0`, the dropper is never excluded, and re-pickup is immediate. Pure Android omission; `Game1.createItemDebris` doesn't set it either.

## Design

### Phase 1 — minimal parity fix (ship + feel-test)

Re-engage the dormant mechanism by tagging deliberate player drops with the dropper's ID.

- A small helper in `Patches/InventoryManagementPatches.cs`: `TagAsPlayerDrop(Debris d)` → if `EnableConsoleDropBlocker` and `d != null`, set `d.DroppedByPlayerID.Value = Game1.player.UniqueMultiplayerID`. Wrapped in try/catch (never break a drop).
- Apply at the player-drop sites in that file: `DropHeldItem` (the deliberate controller drop — the reported bug) and the inventory-full failsafe drop in `CancelHold`. Capture the `Debris` returned by `Game1.createItemDebris`.
- **Explicitly NOT a blanket `Game1.createItemDebris` postfix** — that method also creates monster loot, harvest, and tree/rock-break debris; tagging those would wrongly block the player from collecting loot/harvest for 1.2s. Only deliberate player inventory-drops get tagged.
- `DroppedByPlayerID` is a vanilla `NetLong` present on the PC DLL — direct access compiles (no reflection).

**Toggle:** new `EnableConsoleDropBlocker` (default true).

### Phase 2 — only if Phase 1 doesn't match the Switch feel

The user reports that on Switch, with 2 Iridium Bands (huge magnetic radius), they cannot leave the pickup radius within 1.2s yet still never re-grab their own drops — which suggests the *real* console uses a stronger **"must leave the pickup radius once before re-collecting"** gate that the Android port simplified down to the 1200ms timer. If the Phase 1 device test shows the drop still returns too eagerly, add a per-drop "has the dropper exited `playerInRange` since dropping?" gate and block re-collection until then. If that's the true console behavior, it is still **parity** (in-scope for AC), not beyond-console.

## Scope

- Phase 1 sites: `DropHeldItem` + `CancelHold` inventory-full failsafe (both in `InventoryManagementPatches.cs`).
- Deferred unless reported: the deeper `ItemGrabMenu` chest-swap spill drop sites.
- Out of scope: touch drops (mod is controller-only); blanket loot/harvest debris (must stay instantly collectable).

## Verification

Device playtest: drop an item → it does NOT instantly fly back; after the bounce + ~1.2s (or after walking away and back) it returns. Regression check: monster loot / crop harvest / rock + tree drops still magnetize and collect instantly. Compare the feel to Switch; if it returns too eagerly, escalate to Phase 2.
