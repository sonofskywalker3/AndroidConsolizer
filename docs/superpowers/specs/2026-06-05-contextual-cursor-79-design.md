# #79 — Contextual Right-Stick Cursor Sprite — Design

**Date:** 2026-06-05
**Milestone:** v4.0 "The Right Stick Update" (follows the #12 cursor core, dev v3.9.10)
**File touched:** `Patches/RightStickCursorPatches.cs` (only)
**Toggle:** none new — part of the existing `EnableRightStickCursor` cursor.

## Problem

On console, the overworld cursor sprite changes by what it hovers: a **hand/grab** over interactables (chests, mailbox, shipping bin), a **speech bubble** over talkable NPCs, a **gift box** over an NPC when holding a giftable item, a **magnifying glass** over inspectable tiles, the **plain pointer** otherwise.

AC's self-drawn right-stick cursor (`RightStickCursorPatches.DrawCursor`) always draws the default pointer (`Game1.mouseCursor`), so it never reflects context.

## Why the default pointer is all we ever see

The Android engine **already computes** the full contextual cursor every frame inside `Game1.drawMouseCursor` (`Game1.cs:15618-15692`), then discards it:

1. `Utility.canGrabSomethingFromHere` (called at `15645`) internally calls `Utility.checkForCharacterInteractionAtTile`, which sets `Game1.mouseCursor` to `cursor_talk`/`cursor_gift` over an NPC (side effect).
2. Tile hints at `15647-15649`: when `mouseCursor == cursor_default && isActionAtCurrentCursorTile`, sets `mouseCursor = isSpeechAtCurrentCursorTile ? cursor_talk : isInspectionAtCurrentCursorTile ? cursor_look : cursor_grab`.
3. The animal loop at `15653-15671` sets `cursor_grab` over an un-petted farm animal.
4. **Line `15688`: `mouseCursor = cursor_default;`** — wipes the computed value.

Our cursor is drawn from a `Display.RenderedHud` handler, which runs **after** the engine's draw — i.e. after the `15688` reset — so `Game1.mouseCursor` always reads `cursor_default` for us.

### Where each input comes from (Android decompile)

- **Tile flags** — `GameLocation.isActionableTile` (`GameLocation.cs:14009`) sets `Game1.isInspectionAtCurrentCursorTile` (`Dialogue`/`Message`/`MessageOnce`/`NPCMessage` tile Actions, `:14029`) and `Game1.isSpeechAtCurrentCursorTile` (`MessageSpeech`, `:14033`); `isActionAtCurrentCursorTile` is the overall "actionable here" result (covers buildings like the shipping bin at `:14011-14017`, tile `Action` properties, and grabbable `Object`s like chests at `:14042`). These three `Game1` statics are set during `updateCursorTileHint` (`Game1.cs:7436`) / `isActionableTile` each tick and **persist** until cleared at the start of the next tick's `updateCursorTileHint` (`:7443-7445`). They are therefore valid to read in our post-draw `RenderedHud` handler.
- **NPC talk/gift** — `Utility.checkForCharacterInteractionAtTile` (`Utility.cs:4925`). Sets `Game1.mouseCursor` to `cursor_talk` (talkable / movie theater / simple NPC) or `cursor_gift` (giftable held item the NPC accepts). Its only side effects are writing `Game1.mouseCursor` and `Game1.mouseCursorTransparency` → safe to call if we snapshot/restore both. (We call this directly rather than `canGrabSomethingFromHere`, which additionally calls `obj.hoverAction()` — a side effect we must not double-fire.)
- **Animals** — `currentLocation.animals` + `FarmAnimal.GetCursorPetBoundingBox()` + `wasPet`. Side-effect-free to read.

### Cursor index constants (Android, `Game1.cs:2943-2948`)

`cursor_default = 0`, `cursor_grab = 2`, `cursor_gift = 3`, `cursor_talk = 4`, `cursor_look = 5`.
These are `public static readonly int` on `Game1` — read directly (present on both PC and Android DLLs, so `nameof`/direct ref is fine, no string reflection needed).

## Approach (chosen): replicate the pick at draw time

Add `ResolveContextualCursor()` to `RightStickCursorPatches`, called from `DrawCursor` to pick the sprite index. Resolution order mirrors the engine's effective priority (NPC → tile → animal → default):

1. **NPC** — snapshot `Game1.mouseCursor` + `Game1.mouseCursorTransparency`; set `mouseCursor = cursor_default`; call `Utility.checkForCharacterInteractionAtTile(cursorTile, player)` then, if still default, `checkForCharacterInteractionAtTile(cursorTile + (0,1), player)` (the engine probes the tile and the one below, matching `canGrabSomethingFromHere` at `Utility.cs:5009-5013`); read the resulting `mouseCursor`; restore both snapshots. If the read value is `cursor_talk`/`cursor_gift`, use it.
2. **Tile hint** — else if `Game1.isActionAtCurrentCursorTile`: `isSpeechAtCurrentCursorTile ? cursor_talk : isInspectionAtCurrentCursorTile ? cursor_look : cursor_grab`.
3. **Animal** — else loop `currentLocation.animals`; if the cursor is inside an un-petted animal's `GetCursorPetBoundingBox()`, `cursor_grab`.
4. Else `cursor_default`.

`cursorTile` is the cursor's world tile: `new Vector2((Game1.viewport.X + Game1.getOldMouseX()) / 64, (Game1.viewport.Y + Game1.getOldMouseY()) / 64)` — same formula `updateCursorTileHint` uses (`Game1.cs:7446-7447`).

`DrawCursor` then draws `Game1.mouseCursors` with `getSourceRectForStandardTileSheet(..., resolvedIndex, 16, 16)` (currently it uses `Game1.mouseCursor`).

### Why not capture the engine value via transpiler

A transpiler inserted before `15688` would give perfect parity for free, but transpilers are the riskiest patch type on the Android mono runtime, hard to verify, and against the project's "simple patches / fix-the-data" grain. Replication uses only read-only field access plus one well-bounded, side-effect-restored helper call — the patch types this project already trusts.

## Scope & guards

- Runs only inside the existing `DrawCursor` path: `EnableRightStickCursor` on, world ready, no active menu, not `eventUp`, not a slingshot, and `timerUntilMouseFade > 0` (cursor visible). The contextual resolution adds no new gate.
- Wrap `ResolveContextualCursor` in try/catch; on any error fall back to `cursor_default` so the cursor still draws.
- The NPC probe's snapshot/restore guarantees no net change to `Game1.mouseCursor` / `mouseCursorTransparency`.

## Diagnostic-first verification

The first build ships the real feature **plus** a `VerboseLogging`-gated line (rate-limited per tick, only when the right stick is moving) logging: cursor tile, `isAction/isSpeech/isInspection`, the NPC-probe result, and the final resolved index. This one deploy both confirms the inputs are populated on the G Cloud controller path **and** is testable in-game.

**Playtest (meaningful — sprites are visual):** hover the cursor over a chest → hand/grab; a villager → speech bubble (and gift box while holding a giftable); a sign/inspectable → magnifying glass; empty ground → plain pointer. Confirm from the `[RStickCtx]` log lines + on-screen sprite.

## Out of scope

- No new GMCM toggle.
- No change to interaction/tool targeting (that's the already-done #12 core).
- Menu cursors, R3 emote/chat, minigame mapping (explicitly out of v4.0).

## Files

- `Patches/RightStickCursorPatches.cs` — add `ResolveContextualCursor()`, call it from `DrawCursor`, add the `[RStickCtx]` verbose diagnostic. No other files.
