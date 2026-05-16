# Design: Monster Eradication Tracking Page — Cursor / Navigation (#39)

**Date:** 2026-05-16
**Milestone:** v3.8.0 — Console Parity: Quick Wins
**TODO item:** #39 — Monster Eradication Tracking Page
**Target version (this phase):** v3.7.15 (diagnostic)
**Test device for this phase:** G Cloud (same device #35 was verified on)

> This spec covers **Phase 1 only — the diagnostic build (v3.7.15)**.
> Phase 2 (the fix, target v3.7.16) will be appended as a "Revision"
> section below after the device test, in the same shape as the
> #17 (v3.7.3 → v3.7.4) and #35 (v3.7.5 → v3.7.6+) specs.

---

## Problem

`TODO.md` #39: "No cursor visible on the monster eradication goals
page (Adventurer's Guild tracking). Can't switch pages with controller."

The "page" is `LetterViewerMenu`, instantiated by
`Game1.drawLetterMessage(...)` from
`AdventureGuild.showMonsterKillList()`
(`decompiled/StardewValley.Locations/AdventureGuild.cs` line 80).
The in-game trigger is interacting with the Adventure Guild's kill
list board — tile index `1306` in the `Buildings` layer
(`AdventureGuild.checkAction`, line 28-46).

The TODO is from an earlier device session and the user no longer
remembers exactly which inputs were tried — so both halves of the
report ("no cursor" and "can't switch pages") are unconfirmed at the
input level.

## What the Android decompile already tells us (universal Android)

These are written into the Android source itself
(`StardewValley.Menus.LetterViewerMenu`, decompile reference:
`decompiled/StardewValley/StardewValley.Menus/LetterViewerMenu.cs`)
and therefore present on every Android device, G Cloud included:

1. **DPad page-turn is wired** (line 565-578). `receiveGamePadButton`
   handles `Buttons.DPadLeft` (`page--`) and `Buttons.DPadRight`
   (`page++`) when bounds allow. There is no `case Buttons.A`,
   `Buttons.B`, or stick handling — vanilla page nav is DPad-only.
2. **Forward arrow is the default snap target** for multi-page mail
   with no accept/note button (line 524-538):
   `snapToDefaultClickableComponent` picks `getComponentWithID(102)`
   (the `forwardButton`, `region_forwardButton = 102` constant at line
   17). So on entry, `currentlySnappedComponent` should point at the
   forward arrow.
3. **`forwardButton.visible = page < mailMessage.Count - 1`** and
   `backButton.visible = page > 0` are set in the constructor and
   refreshed on `update` / `receiveLeftClick` / `receiveKeyPress`
   (lines 96-97, 117-118, 174-175, 708-709).
4. **Constructor overload asymmetry — likely a vanilla root cause.**
   `LetterViewerMenu` has three constructor overloads (lines 89, 100,
   121). The kill list takes the `(string)` overload via
   `Game1.drawLetterMessage` (`Game1.cs` line 10332). That overload
   **never calls `populateClickableComponentList()` or
   `snapToDefaultClickableComponent()`** (compare lines 89-98 to lines
   121-186 — the `(string, string, bool)` overload that mailbox
   letters use). `update()` doesn't snap either (lines 705-722). So
   on entry the kill list almost certainly has
   `currentlySnappedComponent = null`. That alone is sufficient to
   explain the "no cursor" symptom — there's no anchor for the cursor
   to be at. It does NOT explain "can't switch pages" by itself
   (DPad page-turn is hardcoded in `receiveGamePadButton`, no
   component required), so the diagnostic still has work to do on the
   page-nav half.

If this hypothesis holds, the mailbox letter test in Phase 1 should
produce cursor + nav working (because that constructor does snap),
giving immediate scope evidence that the fix should be gated to the
no-snap overload.

## What we don't know yet (device-variable)

For G Cloud (or any device, really — kill list cursor has never been
diagnosed):

- Does `LetterViewerMenu.receiveGamePadButton` actually receive
  `DPadLeft` / `DPadRight` on Android? (Vanilla wiring exists, but
  Android's input pipeline may eat it before the menu sees it.)
- Does `Buttons.A` reach the menu? (Vanilla doesn't handle it; if it
  does reach, that's free information for whether we'd want an
  A-to-advance handler.)
- Is `Game1.options.snappyMenus` `True` or `False` on G Cloud inside
  `LetterViewerMenu`? (Per #17 / #35, the legacy "snappyMenus is
  False on Android" assumption is no longer safe.)
- Is the cursor visible at all? If `drawMouse()` is suppressed on this
  menu the same way it is on the title screen (#17) and elsewhere,
  state will be correct but nothing will render.
- Is `currentlySnappedComponent` actually set to `forwardButton`
  (id 102) on entry, or is it null / something else?
- Does the kill-list mailbox-letter shape reproduce both symptoms, or
  is one device-/letter-specific?

Per `.claude/CLAUDE.md` ("MANDATORY: Diagnostic-First Development"),
we build a logging patch before designing the fix.

## Decision

**Approach A — minimal diagnostic with a side channel for scope.**
Three Harmony patches on `LetterViewerMenu`. No behaviour change, log
only. Removed in the follow-up fix commit (same lifecycle as #17
v3.7.3 → v3.7.4 and #35 v3.7.5 → v3.7.6+).

Approaches B (skip diagnostic, apply the #17 cursor-draw pattern
blindly) and C (combined diagnostic + cursor-draw in one build) were
considered and rejected:

- **B** repeats the v3.7.2 mistake — guessing a fix that matches the
  symptom shape without confirming the mechanism. Also doesn't
  address the "can't switch pages" half of the report.
- **C** muddies the diagnostic: if the cursor appears, we don't know
  whether the draw-override saved us or vanilla worked all along. The
  project rule "each diagnostic answers a SPECIFIC question" exists
  to prevent exactly this.

## Implementation

New patch file: `Patches/LetterViewerMenuPatches.cs`. Registered in
`ModEntry.cs` next to the other `*Patches.Apply(...)` calls.

### Patch 1 — `Constructor_Postfix` on the two relevant `LetterViewerMenu` overloads

Two separate Harmony postfixes:
- `LetterViewerMenu(string)` (line 89) — the `drawLetterMessage` path
  the kill list uses.
- `LetterViewerMenu(string, string, bool)` (line 121) — the mailbox /
  collection-viewer path. Patching this is what makes the Phase 1
  mailbox test produce log evidence at all; without it, Test 2 logs
  nothing on open and we can't compare.

The `(int secretNoteIndex)` overload (line 100) is out of scope —
secret notes aren't part of #39's reported symptoms and there's no
plan to test them in Phase 1.

Both postfixes log the same multi-line snapshot, with an `overload`
field so the log clearly tells the two paths apart without relying
on `firstPagePrefix` heuristics:

```
[LetterDiag] open overload=<"(string)"|"(string,string,bool)">
  mailMessage.Count=<int> firstPagePrefix="<first 80 chars of mailMessage[0]>"
  forwardButton.visible=<bool> backButton.visible=<bool>
  snapped=<id=N,name=...,bounds=(x,y,w,h) | null>
  snappy=<bool> gamepad=<bool> lastMotionMouse=<bool> gamepadConnected=<bool>
  mouse=(<x>,<y>)
```

`firstPagePrefix` is a secondary scope-decision channel — it lets us
identify the kill list specifically (header text begins with the
localized `AdventureGuild_KillList_Header` string) within the
`(string)` overload, in case the future fix needs to be even more
narrowly gated. Truncated to 80 chars so the log doesn't bloat for
long letters.

`gamepadConnected` records `GamePad.GetState(PlayerIndex.One).IsConnected`
— per the v3.7.3 (#17) finding, this is unreliable on Ayaneo
handhelds. Still worth recording on G Cloud as a baseline.

**Expected difference if the Section "Universal Android" hypothesis
holds:** the `(string)` snapshot will show `snapped=null`, the
`(string,string,bool)` snapshot will show `snapped=id=102,...` (or
103/104 depending on which side branch fires). That's the smoking
gun for the constructor-asymmetry root cause.

### Patch 2 — `ReceiveGamePadButton_Prefix` on `LetterViewerMenu.receiveGamePadButton(Buttons)`

Log every call. Cap at **40 entries** so a stuck button can't fill
the log.

```
[LetterDiag] receiveGamePadButton b=<Buttons> page=<int>/<count-1> forwardVisible=<bool> backVisible=<bool>
```

This is the patch that answers the single biggest functional unknown:
does DPad/A/anything actually reach the menu on Android? If yes, the
"can't switch pages" symptom is something downstream of dispatch
(state not visibly updating, page render bug, etc.). If no, the fix
lives upstream in the input pipeline.

### Patch 3 — `Update_Postfix` on `LetterViewerMenu.update(GameTime)`

State snapshot, **on-change-only**. State hash = tuple of
`(page, currentlySnappedComponent?.myID, forwardButton.visible,
backButton.visible, Game1.lastCursorMotionWasMouse)`. Re-log only when
the hash differs from the last logged snapshot. Cap at **20 unique
snapshots** so a frozen state can't fill the log.

```
[LetterDiag] update page=<int>/<count-1>
  snapped=<id=N,name=... | null>
  forwardVisible=<bool> backVisible=<bool>
  lastMotionMouse=<bool> mouse=(<x>,<y>)
```

Together, Patch 2 + Patch 3 form an input→state evidence chain: every
button press logs first (Patch 2), then any resulting state change
logs next (Patch 3, on-change-only). A button that fires but produces
no state change is immediately visible as a Patch 2 entry with no
Patch 3 entry behind it.

### Reflection access

No private/protected fields needed. Everything logged is public:

- `mailMessage` (`List<string>`, public, line 47)
- `page` (public, line 53)
- `forwardButton`, `backButton` (public `ClickableTextureComponent`,
  lines 69-71)
- `currentlySnappedComponent` (public, inherited from `IClickableMenu`)
- `Game1.options.snappyMenus`, `Game1.options.gamepadControls`,
  `Game1.lastCursorMotionWasMouse`, `Game1.getMouseX/Y` —
  public statics.

No reflection cache needed at `Apply()`.

### Robustness

All three patches wrap their entire body in `try/catch`, log the
exception at `LogLevel.Error` with a `[LetterDiag]` prefix, and
swallow. Diagnostic patches must never break mail reading.

## Test plan (Phase 1)

User runs on **G Cloud** (the device #35 was verified on; matches
recent diagnostic context):

### Test 1 — Adventure Guild kill list
1. Build v3.7.15, deploy via `SyncdewValley/sync.ps1 deploy`.
2. Load any save. Travel to the Adventurer's Guild (left side of the
   Mines area).
3. Interact with the kill list board on the wall (the one with the
   bouncing arrow indicator if it's the first visit).
4. **Observe:** is a cursor visible on entry? Where?
5. Press **DPad Right** once. Then **DPad Left** once.
6. Press **left thumbstick Right** once. Then **Left** once.
7. Press **A** once.
8. Press **B** to close.

### Test 2 — Regular mailbox letter (scope evidence)
9. Walk to farmhouse, open mailbox if a letter is pending. If none,
   skip and note this in the test report.
10. **Observe:** cursor visible? DPad turns pages? Same set of inputs
    as Test 1, steps 4-8.

### Pull logs
11. `cd ../SyncdewValley && .\sync.ps1 logs` — auto-archives the prior
    log and drops the new one in `test-output/SMAPI-latest.txt`.

### Expected log content

- Two `[LetterDiag] open` lines (one per menu open).
- ~8-14 `[LetterDiag] receiveGamePadButton` entries (varies with
  what reaches the menu).
- A handful of `[LetterDiag] update` on-change snapshots.

### What the log will tell us

| Pattern observed | Interpretation |
|---|---|
| `(string)` `open` shows `snapped=null`, `(string,string,bool)` `open` shows `snapped=<non-null id>` (102, 103, or 104 per `snapToDefaultClickableComponent` branches at lines 524-538) | **Predicted outcome from the constructor-asymmetry hypothesis.** Phase 2 fix: `Constructor_Postfix` on the `(string)` overload that calls `populateClickableComponentList()` then `snapToDefaultClickableComponent()` — make the kill-list overload behave like the mailbox overload. "Fix the data," not the engine. |
| `receiveGamePadButton b=DPadRight` followed by no `update` page change | Dispatch works, state doesn't update — page render or visibility bug. |
| No `receiveGamePadButton` entries at all when user pressed DPad | DPad isn't reaching the menu — upstream input-pipeline issue. |
| Both overloads snap correctly but user reports no visible cursor | Pure #17 drawMouse-suppression pattern. Fix is `draw` postfix to render cursor ourselves. |
| Kill list reproduces page-nav failure but mailbox doesn't | Investigate why DPad reaches one and not the other. (Less likely — both flow through the same `receiveGamePadButton`.) |
| Both reproduce both symptoms | Apply both fixes unconditionally to all relevant overloads. |
| `gamepadConnected=False` on G Cloud's external controller | Confirms the v3.7.3 finding generalizes beyond Ayaneo handhelds; affects any future patch gates. |

## Non-goals (this phase)

- No behaviour change. Diagnostic only.
- No GMCM toggle.
- No design of the v3.7.16 fix — that comes after the device-test
  results, as a Revision section appended to this same spec file.
- No work on the `LetterViewerMenu(int secretNoteIndex)` overload
  (line 100). Secret notes aren't part of #39's reported symptoms
  and aren't on the Phase 1 test plan. The two overloads we DO patch
  (`(string)` for kill list, `(string, string, bool)` for mailbox)
  cover both Phase 1 tests.

## Files

| File | Change |
|---|---|
| `Patches/LetterViewerMenuPatches.cs` | New — three Harmony patches as above |
| `ModEntry.cs` | One line — register the new patch class |
| `manifest.json` | Bump `Version` to `3.7.15` |

Single commit:
`v3.7.15: #39 LetterViewerMenu diagnostic — log construction state, gamepad routing, and page state`.

---

(Phase 2 — fix design — will be appended here as a Revision after
device test, mirroring the #17 and #35 spec lifecycle.)
