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

## Revision — 2026-05-15 (Phase 2: Fix, target v3.7.6)

The v3.7.5 diagnostic ran on the G Cloud. The pulled log
(`AndroidConsolizer/test-output/SMAPI-latest.txt`,
`[LoadGameDiag]` lines 184–291) overturned three of this spec's
pre-test assumptions and reduced the fix scope from a
multi-symptom rework to a single one-line snap.

### What the device showed

| When | snappy | gamepad | lastMotionMouse | mouse | snapped | _joypadIdx | slotCount |
|---|---|---|---|---|---|---|---|
| Touch-tap entry, list loading | True | False | True | (973,836) | null | -1 | 0 |
| List populated | True | False | True | (973,836) | null | -1 | 2 |
| DPadUp | True | True | False | (1262,245) | id=0 slot bounds | 0 | 2 |
| DPadDown (mid-frame) | True | True | False | (1262,245) | id=1 slot bounds | 1 | 2 |
| DPadDown (cursor caught up) | True | True | False | (1262,**445**) | id=1 | 1 | 2 |
| A → save loaded | — | — | — | — | — | 1 | 2 |

### What this overturns

1. **`snappyMenus = True` on G Cloud as well as Ayaneo.** The
   project-wide "snappyMenus is False on Android" assumption is dead
   on two devices now.
2. **The "no `case Buttons.A` handler" finding from the decompile is
   correct, but Android's touch-sim layer hides it.**
   `Game1.updateActiveMenu` calls `receiveLeftClick(mouseX, mouseY)`
   after every A press, and with the cursor sitting on a snapped
   slot's bounds, that hits vanilla `releaseLeftClick`'s slot-load
   path. So an explicit A handler is *not* needed on G Cloud — once
   the cursor is in the right place, A loads the save automatically.
3. **The "first stick press wasted" line in the original spec is
   half-wrong.** The vanilla early-return at `LoadGameMenu.cs:524`
   sets `_joypadSelectedItemIndex = 0` AND triggers a snap that moves
   the cursor visibly to slot 0. The press isn't navigationally
   useless — it claims slot 0 in one shot.

### What is *not* claimed by this revision

The diagnostic ran with v3.7.4's `TitleMenuPatches` active. That
patch's gate ostensibly skips when `TitleMenu.subMenu != null` (i.e.
when LoadGameMenu is open), but the test cannot independently
confirm whether the cursor visibility on `LoadGameMenu`
(`cursorAlpha=1` from frame 0) comes from the vanilla `drawMouse`,
some interaction with v3.7.4, or both. **Do not touch
`Patches/TitleMenuPatches.cs`.** It works; leave it.

### What is actually broken

Just one thing: **the cursor lands wherever the user last touched —
typically the Load Game button area at (973,836) — instead of on
slot 0.** Matches the original TODO line ("cursor in free space
below saves"). The user has to press DPad once to jog the cursor
onto slot 0.

### Decision (v3.7.6)

Single change: snap `_joypadSelectedItemIndex` to `0` and call
`snapToDefaultClickableComponent()` on the first
`LoadGameMenu.update()` tick where `currentlySnappedComponent ==
null` and `slotButtons.Count > 0` (i.e. the menu is open and the
async save scan completed).

The vanilla `snapToDefaultClickableComponent` (decompile line
567–571) reads `_joypadSelectedItemIndex` and calls
`Game1.setMousePosition(slot.bounds.Center)` — pure "fix the data,
let the game's own code work."

### Why these gates and no others

- **`currentlySnappedComponent == null`** is the natural "fresh menu
  instance" signal. Once we snap, it becomes non-null and our patch
  stops firing for that instance. Each new `LoadGameMenu`
  construction resets it back to null, so the snap fires again —
  no static "did we snap" flag needed.
- **`slotButtons.Count > 0`** waits for the async `_initTask` save
  scan to complete (snapshot 0 → 1 in the diagnostic showed the
  populate transition). Snapping to a non-existent `slotButtons[0]`
  would no-op or crash.
- **No `gamepadControls` gate.** The diagnostic showed `gamepad =
  False` on touch entry (snapshots 0–1) — gating on it would skip
  the snap entirely, which is the opposite of what we want.
- **No `lastCursorMotionWasMouse` gate.** Touch entry sets it to
  True, and the user may then pick up the controller. Moving the
  cursor to slot 0 is harmless to pure-touch users (they don't read
  the cursor anyway).
- **No GMCM toggle.** Same precedent as #17 (v3.7.4) and #22b: a
  pure parity bug fix doesn't need an opt-out.

### Why this also fixes the "first stick press wasted" concern

After our snap, `_joypadSelectedItemIndex = 0` and
`currentlySnappedComponent = slotButtons[0]`. The controller user's
first DPadDown press now hits the switch (line 543 in the
decompile) instead of the early-return at line 524 — it advances
straight to slot 1 with no "claim slot 0" intermediate gesture.

### Implementation

**Replace the v3.7.5 diagnostic file with a fix file**, since the
diagnostic is no longer needed:

| File | Change |
|---|---|
| `Patches/LoadGameMenuDiagnosticPatches.cs` | **Delete** |
| `Patches/LoadGameMenuPatches.cs` | **New** — Harmony postfix on `LoadGameMenu.update(GameTime)` |
| `ModEntry.cs` | Replace `LoadGameMenuDiagnosticPatches.Apply(...)` line with `LoadGameMenuPatches.Apply(...)` |
| `manifest.json` | Bump `Version` to `3.7.6` |

Single commit:
`v3.7.6: #35 LoadGameMenu cursor on slot 0 — snap on first update with slots loaded`.

The new patch:

- Single Harmony **postfix** on `LoadGameMenu.update(GameTime)`.
- Reflection lookup for `_joypadSelectedItemIndex` (private), cached
  at `Apply()`. If the lookup fails, log a `Warn` and the postfix
  becomes a no-op (the snap call would be useless without setting
  the index first).
- Postfix body, wrapped in try/catch (errors logged at `Error`,
  swallowed):
  1. Return if `__instance.currentlySnappedComponent != null`.
  2. Return if `__instance.slotButtons == null || __instance.slotButtons.Count == 0`.
  3. Return if the cached `_joypadSelectedItemIndex` `FieldInfo` is
     null.
  4. Set `_joypadSelectedItemIndex = 0` via reflection.
  5. Call `__instance.snapToDefaultClickableComponent()`.

No new public surface, no new fields, no GMCM, no behaviour change
for any other menu.

### Test plan (v3.7.6)

1. Build, deploy via `SyncdewValley/sync.ps1 deploy`.
2. Boot to title.
3. **Touch-tap** Load Game. Wait ~2 sec for the save list.
4. Confirm the cursor appears **on slot 0** (the top save) without
   any controller input — not at the touch position.
5. Press DPadDown once and confirm the cursor jumps straight to
   slot 1 (no "first press snaps to slot 0" intermediate gesture).
6. Press A and confirm slot 1 loads.
7. Bonus check: Re-open Load Game from the title screen via touch
   and confirm step 4 still happens (each fresh instance re-snaps).

### Out of scope for v3.7.6

- Other `LoadGameMenu` subclasses (`MobileFarmChooser`,
  `CoopGameMenu`) — different screens, not part of #35.
- Ayaneo testing for `LoadGameMenu` cursor draw — if Ayaneo's
  vanilla `drawMouse` is broken on `LoadGameMenu` the way it was on
  `TitleMenu` (per v3.7.4 spec), that's a separate diagnostic and a
  separate patch.
- The delete-confirmation dialog — already works on Android via
  `confirmBox.receiveGamePadButton(b)` delegation (decompile line
  516–518) and the focus-snap-to-cancel-button on open (line 763).
