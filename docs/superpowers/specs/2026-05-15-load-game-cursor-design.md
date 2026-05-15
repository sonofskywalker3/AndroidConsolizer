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

## Revision 2 — 2026-05-15 (Phase 3: v3.7.7 diagnostic, v3.7.6 fix only partly worked)

### What v3.7.6 device test on G Cloud showed

The user's report after v3.7.6 deploy (15:33 G Cloud):

1. **Slot 0 highlight on entry: ✓.** `_joypadSelectedItemIndex = 0` is taking effect.
2. **Cursor invisible on entry: ✗.** `snapToDefaultClickableComponent()` moved the system mouse to slot 0 center, but `drawMouse` apparently skips drawing — likely the same `lastCursorMotionWasMouse=True` after touch entry that #17 v3.7.4 hit on TitleMenu.
3. **DPadDown moves cursor to slot 1 BUT slot 0 stays highlighted: ✗.** The user has to navigate back to slot 0 then forward to slot 1 to fix the highlight. New regression introduced by v3.7.6.

### Hypothesis

`LoadGameMenu` has two parallel state systems:

| State | Drives | Updated by |
|---|---|---|
| `_joypadSelectedItemIndex` | Slot **highlight** (`drawSlotBackground` colors slot Wheat at `_joypadSelectedItemIndex == i`, decompile line 899–902) | `LoadGameMenu.receiveGamePadButton` switch path (line 543–555) |
| `currentlySnappedComponent` | Cursor **position** (snappy nav reads neighbour IDs) | `IClickableMenu` snappy nav (`applyMovementKey`) |

Vanilla Stardew likely has input dispatch logic of the form: "if `snappyMenus && currentlySnappedComponent != null && input has a valid neighbour direction`, snappy nav handles the input AND `receiveGamePadButton` is skipped." The v3.7.5 diagnostic saw `receiveGamePadButton` fire for DPadUp when `currentlySnappedComponent` was still null (lines 283 in archived log) — but it might NOT fire when `currentlySnappedComponent` is non-null with valid neighbours.

v3.7.6 pre-sets both `_joypadSelectedItemIndex = 0` AND `currentlySnappedComponent = slot 0` (via `snapToDefaultClickableComponent`). That re-routes DPadDown through snappy nav only, which advances `currentlySnappedComponent` to slot 1 (cursor moves) but **never touches `_joypadSelectedItemIndex`** (highlight stays on slot 0). Matches the user's report exactly.

### What the v3.7.7 diagnostic must answer

1. **Does `LoadGameMenu.receiveGamePadButton` fire for DPadDown after our snap?**
   - YES → hypothesis wrong; receiveGamePadButton is being called but not incrementing — re-think.
   - NO → hypothesis confirmed; snappy nav consumed it. v3.7.8 fix: don't pre-set `currentlySnappedComponent`, only set `_joypadSelectedItemIndex` (and possibly `Game1.setMousePosition` directly).
2. **What is `lastCursorMotionWasMouse` and `mouseCursorTransparency` at the moment the user expects to see the cursor on entry?** Confirms whether the cursor invisibility is the same `lastMotionMouse` family of issues as #17.

### Diagnostic design (v3.7.7)

**Keep the v3.7.6 fix code intact** so we observe the buggy state — vanilla state was already captured in the v3.7.5 diagnostic.

Extend `Patches/LoadGameMenuPatches.cs` with two diagnostic patches (added to the same file; will be removed in v3.7.8 when we ship the real fix). Same shape as the v3.7.5 diagnostic:

1. **Extend `Update_Postfix`** with on-change-only state logging (state hash compare), capped at 30 unique snapshots. Logged fields: `snappy`, `gamepad`, `lastMotionMouse`, `cursorAlpha`, `mouse(x,y)`, `snapped` (id/region/bounds or null), `_joypadSelectedItemIndex` (reflection), `currentItemIndex` (reflection), `slotCount`, AND `weSnapped` (one-shot bool: did our v3.7.6 snap path execute this tick?).
2. **New `ReceiveGamePadButton_Prefix`** logs every gamepad button press + the current `_joypadSelectedItemIndex` and `currentlySnappedComponent` id. Capped at 50 entries. **This is the key data point** — its presence/absence for DPadDown tells us whether snappy nav consumes the input.

All log lines prefixed `[LoadGameDiag]` for grep continuity with v3.7.5. Both patches in try/catch, error-swallowed.

### Files

| File | Change |
|---|---|
| `Patches/LoadGameMenuPatches.cs` | Extend with diagnostic logging in `Update_Postfix` + new `ReceiveGamePadButton_Prefix` patch + reflection lookups for `_joypadSelectedItemIndex` and `currentItemIndex` (the latter is new — was only in the old diagnostic file). v3.7.6 fix code stays intact. |
| `manifest.json` | Bump `Version` to `3.7.7`. |

Single commit:
`v3.7.7: #35 LoadGameMenu diagnostic — log dispatch path after v3.7.6 partial fix`.

### Test plan (G Cloud)

1. Build, deploy via `SyncdewValley/sync.ps1 deploy`.
2. Boot to title.
3. **Touch-tap** Load Game.
4. Wait ~2 sec for save list.
5. **DPadDown once.** (This is the one that exposes the dispatch question.)
6. **DPadDown a second time** (to confirm whether the desync is one-shot or persistent).
7. **DPadUp once.** (To compare with v3.7.5 vanilla DPadUp data.)
8. **A** to load (or **B** to close — either works for ending the test).
9. Log pull is automatic (Claude pulls).

Phase 4 (v3.7.8 real fix) gets designed from the result.

## Revision 3 — 2026-05-15 (Phase 4: v3.7.8 real fix)

### What v3.7.7 device test on G Cloud showed (definitive)

The user ran the test sequence twice with markedly different results:

- **Test 1 (DPadDown first):** Cursor on slot 1, highlight on slot 0 (desync — matches v3.7.6 report).
- **Test 2 (DPadUp first):** Cursor on slot 0, highlight on slot 0 (aligned). Subsequent DPadDown works correctly.

The diagnostic log explains both. Critical finding from snapshot 2 (line 283 of test log):

```
weSnapped=True _joypadSelectedItemIndex=0 snapped=null mouse=(986,866)
```

**Our v3.7.6 `snapToDefaultClickableComponent()` call silently failed.** `weSnapped=True` confirms our patch ran and `_joypadSelectedItemIndex` was set to 0, but `currentlySnappedComponent` stayed null and the cursor stayed at the touch position. The likely cause: `IClickableMenu.getComponentWithID(0)` walks `allClickableComponents`, which hasn't been populated with `slotButtons` yet at the moment our postfix runs (`populateClickableComponentList` runs lazily, typically on first navigation input).

The downstream consequence (snapshot 3, line 286 — after DPadDown):

```
weSnapped=True _joypadSelectedItemIndex=0 snapped=null
```

`receiveGamePadButton` for DPadDown fired (logged at line 285) and incremented `_joypadIdx` to 1. **Then our postfix ran in the same frame, saw `currentlySnappedComponent == null` (still), and clobbered `_joypadIdx` back to 0.** `weSnapped=True` again. Our `currentlySnappedComponent == null` gate never closes — we keep re-firing every tick and resetting `_joypadIdx`. By the time `currentlySnappedComponent` finally became slot 1 (snapshot 4, set by snappy nav fallback after the controller activity flipped `lastMotionMouse=False`), `_joypadIdx` was stuck at 0 = slot 0 highlighted, cursor at slot 1 = the desync.

Test 2 worked because DPadUp on `_joypadIdx=0` does `_joypadIdx-- → -1 → clamp to 0` — the clobber back to 0 was a no-op. By the time DPadDown was pressed, `currentlySnappedComponent` was non-null (snappy nav had set it during the DPadUp activity), so our postfix had stopped clobbering. `_joypadIdx` correctly incremented to 1.

### Decision

Drop `snapToDefaultClickableComponent()` entirely — it doesn't work in the update postfix context. Replace with two cleaner mechanisms, both in the same `Update_Postfix`:

1. **One-shot on entry:** when `_joypadSelectedItemIndex == -1` and `slotButtons.Count > 0`, set `_joypadSelectedItemIndex = 0`. Self-resetting via vanilla state — a fresh `LoadGameMenu` instance has `_joypadSelectedItemIndex = -1` by construction, so the gate naturally closes after the snap and re-opens on the next instance. No static "did we snap" flag needed.
2. **Auto-sync every tick:** when `currentlySnappedComponent != null` and `currentlySnappedComponent.region == 900` (a slot button — region is set in `recalculateSlots()` line 995) and `currentlySnappedComponent.myID != _joypadSelectedItemIndex`, set `_joypadSelectedItemIndex = currentlySnappedComponent.myID`. The highlight always follows the cursor, regardless of which path moved it (snappy nav, `receiveGamePadButton` switch, manual `setMousePosition`).

No `currentlySnappedComponent` manipulation, no cursor positioning, no `snapToDefaultClickableComponent()` calls. We don't fight either input system — we just keep the displayed highlight in sync with the actual snapped component.

### What this delivers / does not deliver

**Delivers:**

- Slot 0 highlighted from the moment the save list loads (no controller input needed).
- Highlight follows the cursor on every navigation, regardless of which path the input took (snappy nav OR `receiveGamePadButton`'s switch).
- No more clobbering of `_joypadSelectedItemIndex` mid-press.

**Does not deliver:**

- A visible cursor on entry (touch state suppresses `drawMouse`). The cursor appears on the first controller press, same as vanilla. If we want the cursor visible from frame 1 (parallel to #17 v3.7.4 for TitleMenu), that's a separate `LoadGameMenu.draw` postfix patch — out of scope for v3.7.8 unless the user reports it as a needed UX fix.

### Implementation

Replace the body of `Patches/LoadGameMenuPatches.cs` with the v3.7.8 fix and remove all v3.7.7 diagnostic code:

- Single Harmony **postfix** on `LoadGameMenu.update(GameTime)`.
- Reflection lookup for `_joypadSelectedItemIndex` cached at `Apply()`. Failure path: log Warn, postfix becomes a no-op.
- Postfix body, wrapped in try/catch:
  1. If reflection FieldInfo is null, return.
  2. Read `_joypadSelectedItemIndex` via reflection. If `== -1` AND `slotButtons.Count > 0`, set it to `0` (one-shot snap).
  3. Otherwise, if `currentlySnappedComponent != null` AND `region == 900` AND `myID != _joypadSelectedItemIndex`, set `_joypadSelectedItemIndex = myID` (auto-sync).
- Constants: `DefaultSlotIndex = 0`, `SlotRegion = 900`.
- All `[LoadGameDiag]` logging removed.

### Files

| File | Change |
|---|---|
| `Patches/LoadGameMenuPatches.cs` | Replace v3.7.7 content with the v3.7.8 fix above (much smaller — no diagnostic counters, no state hash, no button prefix). |
| `manifest.json` | Bump `Version` to `3.7.8`. |

Single commit:
`v3.7.8: #35 LoadGameMenu — set _joypadIdx=0 on entry + auto-sync to snapped component`.

### Test plan (G Cloud)

1. Build, deploy.
2. Boot to title, touch-tap Load Game, wait for save list.
3. **Confirm slot 0 is highlighted (Wheat colour) without any input.** Cursor will not be visible — that's expected.
4. **DPadDown.** Cursor appears on slot 1, highlight follows to slot 1.
5. **DPadDown again.** Stays on slot 1 (only 2 saves; clamps).
6. **DPadUp.** Cursor and highlight both back to slot 0.
7. **A.** Loads slot 0.
8. Re-open Load Game and confirm step 3 still happens.

## Revision 4 — 2026-05-15 (Phase 5: v3.7.9 — also move cursor on entry)

### What v3.7.8 device test on G Cloud showed

User test result + a real-world consequence:

- **Highlight side: ✓.** v3.7.8's auto-sync correctly aligns `_joypadSelectedItemIndex` with `currentlySnappedComponent.myID`. DPadDown moves the highlight in step with the cursor.
- **Cursor side: ✗.** v3.7.8 deliberately did not touch the cursor — the design call was that the cursor would appear on the first controller press (vanilla behaviour). On G Cloud, that meant the cursor stayed at the touch-tap position from the title screen (~(986,866) in UI coords).
- **Real consequence:** that touch position landed on a slot's **delete button** (right-side trashcan). When the user pressed A to load the highlighted slot, Android's touch-sim layer fired `receiveLeftClick` at the cursor position, which hit the delete button → confirmation dialog → save deleted by accident.

The "cursor visibility on entry is a separate, deferrable concern" judgment from the v3.7.8 spec was wrong: the cursor *position* on entry is load-bearing because Android's touch-sim layer routes A presses through the cursor coords, not through `_joypadSelectedItemIndex`. Leaving the cursor at the touch position is actively dangerous.

### Decision

Restore cursor positioning on entry, but bypass the broken `snapToDefaultClickableComponent()` path. Direct assignment + direct cursor snap:

1. **Entry one-shot (extended from v3.7.8):** when `_joypadSelectedItemIndex == -1` and `slotButtons.Count > 0`:
   - Set `_joypadSelectedItemIndex = 0` (highlight).
   - Assign `currentlySnappedComponent = slotButtons[0]` directly. This bypasses `getComponentWithID(0)` (which returned null in the v3.7.7 diagnostic because `allClickableComponents` is not populated when our postfix runs).
   - Call `snapCursorToCurrentSnappedComponent()` (now non-null, so it actually moves the mouse).
2. **Auto-sync every tick (unchanged from v3.7.8):** keeps `_joypadSelectedItemIndex` in step with `currentlySnappedComponent.myID` whenever they drift.

The v3.7.6 failure mode (postfix re-firing because `currentlySnappedComponent` stayed null) cannot recur because we assign it directly — the gate genuinely closes after the first snap.

### Why this is safe with snappy nav

The v3.7.7 diagnostic showed `LoadGameMenu.receiveGamePadButton` *did* fire for DPadDown after our v3.7.6 snap (line 285 of test log) — snappy nav did not consume the input outright. So pre-setting `currentlySnappedComponent` does not break the receive path. Even if it did on some device, the auto-sync from v3.7.8 catches the desync: snappy nav advances `currentlySnappedComponent`, auto-sync mirrors `myID` into `_joypadSelectedItemIndex`. Both paths are now safety-netted.

### Files

| File | Change |
|---|---|
| `Patches/LoadGameMenuPatches.cs` | Extend Update_Postfix entry one-shot to also assign `currentlySnappedComponent = slotButtons[0]` and call `snapCursorToCurrentSnappedComponent()`. Auto-sync block unchanged. |
| `manifest.json` | Bump `Version` to `3.7.9`. |

Single commit:
`v3.7.9: #35 LoadGameMenu — also move cursor to slot 0 on entry (v3.7.8 left it on the trashcan)`.

### Test plan (G Cloud)

1. Build, deploy.
2. Boot to title, touch-tap Load Game, wait for save list.
3. **Confirm cursor is on slot 0** AND **slot 0 is highlighted**.
4. **A** → loads slot 0 (does NOT trigger a delete confirmation).
5. From a fresh entry: **DPadDown** → cursor + highlight both move to slot 1.
6. **A** on slot 1 → loads slot 1.
7. From a fresh entry: **B** → closes back to title.
