# Design: Dialogue Option Box Pre-Selection (#22b)

**Date:** 2026-05-14
**Milestone:** v3.8.0 — Console Parity: Quick Wins
**TODO item:** #22b — Dialogue Option Box counter-intuitive initial selection
**Target version:** v3.7.1

## Problem

When a dialogue choice box appears (NPC questions, Yes/No prompts, event
dialogue questions), nothing is visually selected. Pressing **down** selects the
**top** option; pressing **up** selects the **bottom** option. Both are
counter-intuitive — the player expects an option to already be highlighted, and
expects up/down to move relative to it.

## Root cause

Confirmed against the decompiled Android source
(`StardewValley.Menus.DialogueBox`):

- `DialogueBox.selectedResponse` initializes to `-1` ("nothing selected").
- The game's `snapToDefaultClickableComponent()` does set
  `currentlySnappedComponent` to component ID 0, but for question boxes that is
  vestigial — both the visual highlight (`draw()`) and the committed choice
  (`releaseLeftClick()`) read `selectedResponse`, **not** the snapped component.
- In `receiveGamePadButton`:
  - DPadDown / LeftThumbstickDown → `selectedResponse++` → from `-1` becomes `0`
    (top option).
  - DPadUp / LeftThumbstickUp → `selectedResponse--` → from `-1` becomes `-2`,
    which is `< 0`, so it wraps to `responses.Length - 1` (bottom option).

So the bug is purely that `selectedResponse` starts at an invalid value and the
wrap-around math sends the first "up" press to the bottom.

## Decision

**Pre-select the top option** when a question box opens under gamepad control.
This is a "fix the data" change — set `selectedResponse` to a valid index and
let the game's own navigation and rendering work correctly. No input
interception, no navigation override.

### Resolved questions

1. **Initial state:** Pre-select the top option (`selectedResponse = 0`).
   Something is visibly highlighted immediately; the game's existing up/down
   logic then behaves intuitively (up from `0` wraps to bottom, down from `0`
   goes to `1`).
2. **GMCM toggle:** None. There is no existing general menu-navigation toggle
   that fits — `EnableGameMenuNavigation` is scoped specifically to GameMenu
   *tabs*, and dialogue boxes are a separate menu. Treat this as a straight bug
   fix. Add a toggle later only if a user complains.
3. **Scope of effect:** Gamepad only. Touch/mouse users keep vanilla behavior
   (nothing highlighted until they tap). Gate on
   `Game1.options.gamepadControls && !Game1.lastCursorMotionWasMouse` — the same
   condition the game's own `setUpForGamePadMode()` uses.

## Implementation

New patch file: `Patches/DialogueBoxPatches.cs`.

Single Harmony **postfix** on `DialogueBox.setUpQuestions()`. This method runs in
both paths that build a question box:

- the `DialogueBox(string, Response[])` constructor (standalone questions), and
- `checkDialogue()` (when an NPC conversation turns into a question).

One patch point covers all cases.

Postfix logic:

```csharp
// after the game's setUpQuestions() runs, selectedResponse is still -1
if (Game1.options.gamepadControls && !Game1.lastCursorMotionWasMouse
    && __instance.responses != null && __instance.responses.Length > 0)
{
    __instance.selectedResponse = 0;
}
```

Register the patch in `ModEntry.cs` alongside the other patch classes.

### Patch-point alternatives considered and rejected

- **Postfix on the `DialogueBox(string, Response[])` constructor** — misses the
  NPC-dialogue-becomes-question path (`checkDialogue`). Incomplete.
- **Postfix on `setUpForGamePadMode()`** — called repeatedly via `setUpIcons()`;
  would re-fire every frame and fight the player's own navigation.

## Non-goals

- No change to the wrap-around math in `receiveGamePadButton`.
- No change to the mouse hover selection path (`leftClickHeld`).
- No GMCM toggle.
- No new behavior for touch/mouse input.

## Testing

Device test on a gamepad:

1. Trigger a dialogue with 2+ options (talk to an NPC until a question appears,
   or hit a Yes/No prompt).
2. Confirm the **top** option is highlighted as soon as the box opens.
3. Press **down** — selection moves to the next option.
4. Press **up** from the top — selection wraps to the **bottom** option.
5. Press **A** — the highlighted option is committed.
6. Confirm with touch input that **nothing** is pre-selected until the player
   taps.

Also sanity-check an event-dialogue question (e.g. a festival prompt) to confirm
the `checkDialogue` path is covered.

## Files

| File | Change |
|------|--------|
| `Patches/DialogueBoxPatches.cs` | New — Harmony postfix on `setUpQuestions()` |
| `ModEntry.cs` | Register the new patch class |
| `manifest.json` | Version bump to 3.7.1 |
