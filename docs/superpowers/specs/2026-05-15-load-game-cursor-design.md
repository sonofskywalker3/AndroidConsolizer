# Design: Load Game Screen Cursor / Navigation (#35)

**Date:** 2026-05-15
**Milestone:** v3.8.0 — Console Parity: Quick Wins
**TODO item:** #35 — Load Game Screen Cursor/Navigation
**Target version (this phase):** v3.7.5 (diagnostic)
**Test device for this phase:** G Cloud

> This spec covers **Phase 1 only — the diagnostic build (v3.7.5)**.
> Phase 2 (the fix, target v3.7.6) will be appended as a "Revision"
> section below after the device test, in the same shape as the
> v3.7.4 revision of `2026-05-14-title-menu-cursor-design.md`.

---

## Problem

`TODO.md` #35: "Cursor should start on the top save slot when the Load
Game menu opens, instead of in the free space below the saves.
Navigation has issues (details TBD — needs investigation)."

The TODO is from an earlier device session and the "TBD" is honest:
we do not yet know which symptoms manifest on the active test device
(G Cloud).

## What the Android decompile already tells us (universal Android)

These two are written into the Android source itself
(`StardewValley.Menus.LoadGameMenu`, decompile reference: line numbers
from `decompiled/StardewValley/StardewValley.Menus/LoadGameMenu.cs`)
and therefore present on every Android device, G Cloud included:

1. **No `A` handler.** `receiveGamePadButton` (line 512–565) handles
   only `DPadUp/Down`, `LeftThumbstickUp/Down`, and `B` (which
   redirects to the close button). There is no `case Buttons.A`. So
   on a controller, A on a highlighted save does literally nothing —
   the player must touch-tap the slot to load it.
2. **First stick press wasted.** `_joypadSelectedItemIndex` (line
   276) initializes to `-1`. The first D-pad/stick press hits the
   early-return at line 524 (`if (_joypadSelectedItemIndex == -1 …)`)
   which sets the index to `currentItemIndex` (typically 0) and plays
   "shwip" without moving anything. Until the first press, no slot is
   highlighted (`drawSlotBackground`, line 899–902, only colours a
   slot Wheat when `_joypadSelectedItemIndex == i`).

These two are confirmed without device evidence.

## What we don't know yet (device-variable)

The **visual** layer is unknown for G Cloud. On the Ayaneo (#17,
v3.7.3 diagnostic) `drawMouse` produced no visible output on the
title-menu stack despite correct input state, and `snappyMenus = True`
(overturning the project-wide assumption). The G Cloud has never been
diagnosed for either:

- Is `Game1.options.snappyMenus` `True` or `False` on the G Cloud
  inside `LoadGameMenu`?
- Is the cursor visible on `LoadGameMenu` at all? If yes, where does
  it sit on entry?
- Is `Game1.lastCursorMotionWasMouse` `True` (so a future cursor
  patch's gate would skip the draw) or `False` on a controller cold
  boot?
- Is `currentlySnappedComponent` set on entry, and to what?

Per `.claude/CLAUDE.md` ("Mandatory: Diagnostic-First Development"),
we build a logging patch before designing the fix.

## Decision

**Approach A — Minimal & focused diagnostic.** Two Harmony patches on
`LoadGameMenu`. No behaviour change, log only. Removed in the
follow-up fix commit (same lifecycle as v3.7.3 → v3.7.4).

Approaches B (comprehensive — also patch `update`,
`snapToDefaultClickableComponent`, `receiveLeftClick`,
`releaseLeftClick`) and C (visual-only — drop the gamepad-button
prefix) were considered and rejected: B is ~3× the log noise for
information we don't yet need, and C leaves the single biggest
unknown (does `Buttons.A` reach the menu at all?) unanswered.

## Implementation

New patch file: `Patches/LoadGameMenuDiagnosticPatches.cs`. Registered
in `ModEntry.cs` next to `TitleMenuPatches.Apply(...)` (line 165).

### Patch 1 — `Draw_Postfix` on `LoadGameMenu.draw(SpriteBatch)`

State snapshot, **on-change-only**. State hash = tuple of every field
below; re-log only when the hash differs from the last logged
snapshot. Cap at **30 unique snapshots** so a frozen state can't fill
the log.

Logged fields, single line per snapshot at `LogLevel.Info`:

```
[LoadGameDiag] frame=N
  snappy=<bool> gamepad=<bool> lastMotionMouse=<bool>
  cursorAlpha=<float> mouse=(<x>,<y>)
  snapped=<id=N,region=N,bounds=(x,y,w,h) | null>
  _joypadSelectedItemIndex=<int> currentItemIndex=<int>
  slotCount=<int>
```

`frame` is the snapshot index (0, 1, 2 …), not the game tick — same
convention as the v3.7.3 `[TitleDiag]` output and enough to order
events.

### Patch 2 — `ReceiveGamePadButton_Prefix` on `LoadGameMenu.receiveGamePadButton(Buttons)`

Log every call. Cap at **50 entries**.

```
[LoadGameDiag] receiveGamePadButton b=<Buttons> _joypadSelectedItemIndex=<int> slotCount=<int>
```

This is the patch that answers the single biggest unknown for the
functional fix: does `Buttons.A` actually reach the menu? If yes, the
fix is "add a `case Buttons.A` handler that activates the highlighted
slot." If no, the fix lives upstream in the SMAPI input pipeline and
is a much bigger problem.

### Reflection access

Two private/protected fields. Lookups cached at `Apply()`, not
per-frame.

| Field | Access | Type | Decompile line |
|---|---|---|---|
| `_joypadSelectedItemIndex` | `private` | `int` | `LoadGameMenu.cs:276` |
| `currentItemIndex` | `protected` | `int` | `LoadGameMenu.cs:298` |

If reflection fails on either field, log `Warn` and use sentinel
value `-999` in the snapshot output (so a missing field is visually
distinct from a legitimate `-1` "no selection"). Reflection failure
on one field does not abort registration of the other patch.

`MenuSlots` is public (line 342), `currentlySnappedComponent` is
public (inherited from `IClickableMenu`), and the `Game1.options.*` /
`Game1.lastCursorMotionWasMouse` / `Game1.mouseCursorTransparency` /
`Game1.getMouseX/Y` / `Game1.options.snappyMenus` members are all
public statics — no reflection needed.

### Robustness

Both patches wrap their entire body in `try/catch`, log the exception
at `LogLevel.Error` with a `[LoadGameDiag]` prefix, and swallow.
Diagnostic patches must never crash the title screen.

## Test plan (G Cloud)

1. Build v3.7.5, deploy via `SyncdewValley/sync.ps1 deploy`.
2. Boot to title screen.
3. Tap **Load Game** with touch (controller A would fail anyway
   pre-fix — want to enter the menu unambiguously).
4. Wait ~2 seconds for the load list to populate (the `_initTask`
   async path completes; `MenuSlots.Count` jumps from 0 to N — should
   appear as an on-change snapshot).
5. Press **D-pad up** once.
6. Press **D-pad down** once.
7. Press **A** once on the highlighted slot.
8. Press **B** to close (or tap the close button if B doesn't work).
9. Pull log via `SyncdewValley/sync.ps1 logs`.

Expected log content: a small handful of `[LoadGameDiag]` snapshot
lines from `Draw_Postfix` (initial state + each interaction-driven
change), plus 4 entries from `ReceiveGamePadButton_Prefix` (Up, Down,
A, B — assuming all reach the menu).

## Non-goals (this phase)

- No behaviour change. Diagnostic only.
- No GMCM toggle.
- No design of the v3.7.6 fix — that comes after the device-test
  results, as a Revision section appended to this same spec file.
- No work on other `TitleMenu` sub-menus (Co-op, About, Language,
  etc.) — out of scope for #35.

## Files

| File | Change |
|---|---|
| `Patches/LoadGameMenuDiagnosticPatches.cs` | New — two Harmony patches as above |
| `ModEntry.cs` | One line — register the new patch class |
| `manifest.json` | Bump `Version` to `3.7.5` |

Single commit:
`v3.7.5: #35 LoadGameMenu diagnostic — log gate state and gamepad button routing`.

---

## Phase 2 — Fix (to be appended)

*This section will be filled in after the v3.7.5 device test on the
G Cloud, with the corrected fix design and target version v3.7.6.*
