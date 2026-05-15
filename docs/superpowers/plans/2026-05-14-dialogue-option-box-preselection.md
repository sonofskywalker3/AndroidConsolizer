# Dialogue Option Box Pre-Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pre-select the top option in Stardew Valley dialogue question boxes when a controller is in use, so the first up/down press behaves intuitively instead of jumping to the bottom option.

**Architecture:** A single Harmony postfix on the private `DialogueBox.setUpQuestions()` method. The game leaves `DialogueBox.selectedResponse` at `-1` ("nothing selected") when a question box opens; the postfix sets it to `0` (top option) under gamepad control. `setUpQuestions()` runs in both code paths that build a question box (the `DialogueBox(string, Response[])` constructor and `checkDialogue()`), so one patch point covers every case. Touch/mouse users are untouched.

**Tech Stack:** C# / .NET 6, SMAPI 4.x mod, HarmonyLib for runtime patching. Built with `dotnet build`. Verified by on-device testing (no unit-test suite — this project verifies patches by building + device test, per `.claude/CLAUDE.md`).

**Spec:** `docs/superpowers/specs/2026-05-14-dialogue-option-box-design.md`

---

## File Structure

| File | Responsibility | Change |
|------|----------------|--------|
| `Patches/DialogueBoxPatches.cs` | Owns the `DialogueBox` question-box pre-selection patch. Self-contained — one `Apply` method, one postfix. | Create |
| `ModEntry.cs` | Registers all Harmony patch classes during mod startup. | Modify (one line added) |
| `manifest.json` | Mod metadata, including version. | Modify (version bump) |

The `.csproj` uses SDK-style glob includes, so the new `Patches/*.cs` file is picked up automatically — no `.csproj` edit needed.

This is one feature = one `0.0.1` patch (`v3.7.0` → `v3.7.1`). All three files are committed together in a single commit (Task 4), per the project's "one patch = one change, commit every change" rule.

---

### Task 1: Create the DialogueBox patch class

**Files:**
- Create: `Patches/DialogueBoxPatches.cs`

- [ ] **Step 1: Create `Patches/DialogueBoxPatches.cs` with the full patch class**

Write this exact file content:

```csharp
using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Harmony patch for DialogueBox question boxes (NPC questions, Yes/No prompts,
    /// event dialogue questions).
    ///
    /// Vanilla Android leaves DialogueBox.selectedResponse at -1 ("nothing selected")
    /// when a question box opens. receiveGamePadButton's wrap-around math then sends the
    /// first "up" press from -1 to responses.Length - 1 (the bottom option) and the first
    /// "down" press from -1 to 0 (the top option), so the initial selection feels backwards.
    ///
    /// Fix: a postfix on setUpQuestions() pre-selects the top option (selectedResponse = 0)
    /// under gamepad control. setUpQuestions() runs in both paths that build a question box
    /// — the DialogueBox(string, Response[]) constructor and checkDialogue() — so one patch
    /// point covers all cases. Touch/mouse users keep vanilla behavior.
    /// </summary>
    internal static class DialogueBoxPatches
    {
        private static IMonitor Monitor;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                // setUpQuestions is private on DialogueBox — AccessTools resolves non-public methods.
                var setUpQuestions = AccessTools.Method(typeof(DialogueBox), "setUpQuestions");
                if (setUpQuestions != null)
                {
                    harmony.Patch(
                        original: setUpQuestions,
                        postfix: new HarmonyMethod(typeof(DialogueBoxPatches), nameof(SetUpQuestions_Postfix))
                    );
                    Monitor.Log("DialogueBox patches applied.", LogLevel.Trace);
                }
                else
                {
                    Monitor.Log("DialogueBoxPatches: 'setUpQuestions' method not found — patch skipped.", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply DialogueBox patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Postfix on DialogueBox.setUpQuestions — after the game's setup runs (which leaves
        /// selectedResponse at -1), pre-select the top option so something is visibly
        /// highlighted and the game's up/down navigation behaves intuitively. Gated on the
        /// same condition the game's own setUpForGamePadMode() uses, so touch/mouse users
        /// keep vanilla behavior.
        /// </summary>
        private static void SetUpQuestions_Postfix(DialogueBox __instance)
        {
            try
            {
                if (!Game1.options.gamepadControls || Game1.lastCursorMotionWasMouse)
                    return;
                if (__instance.responses == null || __instance.responses.Length == 0)
                    return;

                __instance.selectedResponse = 0;
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[DialogueBox] SetUpQuestions_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
```

Notes for the engineer:
- `DialogueBox.selectedResponse` (`public int`) and `DialogueBox.responses` (`public Response[]`) are public fields on both the Android and PC builds — confirmed against the decompiled Android source at `C:\Users\Jeff\Documents\Projects\decompiler\stardew-valley-android\decompiled\StardewValley\StardewValley.Menus\DialogueBox.cs` (lines 78 and 18). Direct field access is safe here; this is **not** one of the Android/PC-divergent fields that require reflection.
- `setUpQuestions()` is `private` — `AccessTools.Method` resolves non-public methods, which is why we don't use a `[HarmonyPatch]` attribute.
- The `Apply(Harmony, IMonitor)` signature, the `try/catch` around patch application, and the `Monitor.Log(...)` trace line all match the existing pattern in `Patches/OptionsPagePatches.cs`.

- [ ] **Step 2: Verify the file compiles in isolation**

Run:
```bash
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"
dotnet build AndroidConsolizer.csproj -c Release
```
Expected: `Build succeeded.` with 0 errors. (The new patch class is not yet registered in `ModEntry.cs`, so it compiles but does nothing at runtime yet — that is fine. If the build fails, fix the compile error before continuing.)

---

### Task 2: Register the patch in ModEntry

**Files:**
- Modify: `ModEntry.cs` (the Harmony patch registration block, around lines 148–165)

- [ ] **Step 1: Add the registration line**

In `ModEntry.cs`, find this block (around line 163):

```csharp
            Patches.GameMenuPatches.Apply(harmony, this.Monitor);
            Patches.OptionsPagePatches.Apply(harmony, this.Monitor);
            Patches.BootDiagnosticPatches.Apply(harmony, this.Monitor);
```

Insert the `DialogueBoxPatches` line immediately after the `OptionsPagePatches` line:

```csharp
            Patches.GameMenuPatches.Apply(harmony, this.Monitor);
            Patches.OptionsPagePatches.Apply(harmony, this.Monitor);
            Patches.DialogueBoxPatches.Apply(harmony, this.Monitor);
            Patches.BootDiagnosticPatches.Apply(harmony, this.Monitor);
```

- [ ] **Step 2: Build to confirm registration compiles**

Run:
```bash
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"
dotnet build AndroidConsolizer.csproj -c Release
```
Expected: `Build succeeded.` with 0 errors.

---

### Task 3: Bump the version

**Files:**
- Modify: `manifest.json:3`

- [ ] **Step 1: Update the version field**

In `manifest.json`, change line 3 from:
```json
    "Version": "3.7.0",
```
to:
```json
    "Version": "3.7.1",
```

- [ ] **Step 2: Build the release artifact**

Run:
```bash
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"
dotnet build AndroidConsolizer.csproj -c Release
```
Expected: `Build succeeded.` with 0 errors, and the output ZIP `bin/Release/net6.0/AndroidConsolizer 3.7.1.zip` is produced (note the new `3.7.1` filename — confirms the version bump took effect).

---

### Task 4: Commit

**Files:**
- Commit: `Patches/DialogueBoxPatches.cs`, `ModEntry.cs`, `manifest.json`

- [ ] **Step 1: Stage the three changed files by name**

Run:
```bash
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"
git add Patches/DialogueBoxPatches.cs ModEntry.cs manifest.json
```

Do **not** use `git add .` or `git add -A` — the working tree has unrelated untracked files and a pre-existing `.csproj` edit that must stay out of this commit.

- [ ] **Step 2: Create the commit**

Run:
```bash
git commit -m "$(cat <<'EOF'
v3.7.1: Pre-select top option in dialogue question boxes (#22b)

Vanilla Android leaves DialogueBox.selectedResponse at -1 when a question
box opens, so nothing is highlighted and the receiveGamePadButton
wrap-around math sends the first "up" press to the bottom option and the
first "down" press to the top option — counter-intuitive.

Fix: new Patches/DialogueBoxPatches.cs adds a Harmony postfix on the
private DialogueBox.setUpQuestions() method that sets selectedResponse = 0
(top option) when a controller is in use. setUpQuestions() runs in both
question-box code paths (the DialogueBox(string, Response[]) constructor
and checkDialogue()), so one patch point covers NPC questions, Yes/No
prompts, and event dialogue questions. Gated on
Game1.options.gamepadControls && !Game1.lastCursorMotionWasMouse so
touch/mouse users keep vanilla behavior. No GMCM toggle.

Files: Patches/DialogueBoxPatches.cs (new), ModEntry.cs (register patch),
manifest.json (3.7.0 -> 3.7.1).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 3: Verify the commit**

Run:
```bash
git log --oneline -1
git status --short
```
Expected: the latest commit is `v3.7.1: Pre-select top option in dialogue question boxes (#22b)`, and `git status` shows the three files no longer listed as modified/untracked (the pre-existing unrelated changes remain).

---

### Task 5: Device test (manual — requires the user)

This project verifies patches on a physical device; there is no automated test for menu behavior.

- [ ] **Step 1: Deploy to the test device**

Run:
```bash
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\SyncdewValley"
.\sync.ps1 deploy
```
This pushes the DLL + manifest and restarts the game. (Auto-detects ADB vs MTP per device.)

- [ ] **Step 2: Test with a controller — pre-selection and navigation**

In-game, with a controller connected:
1. Trigger a dialogue with 2+ options — talk to an NPC until a question appears, or trigger a Yes/No prompt.
2. Confirm the **top** option is highlighted as soon as the box opens.
3. Press **down** — selection moves to the next option down.
4. Press **up** from the top option — selection wraps to the **bottom** option.
5. Press **A** — the highlighted option is committed.

- [ ] **Step 3: Test the event-dialogue path**

Trigger an event-dialogue question (e.g. a festival prompt, or any cutscene that asks a question) and confirm the top option is pre-selected there too. This exercises the `checkDialogue()` path rather than the direct constructor path.

- [ ] **Step 4: Test touch input is unaffected**

Using touch only (no controller input first — the gate checks `Game1.lastCursorMotionWasMouse`): open a dialogue question and confirm **nothing** is pre-highlighted until the player taps an option. Vanilla behavior must be preserved for touch.

- [ ] **Step 5: Pull logs and check for errors**

Run:
```bash
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\SyncdewValley"
.\sync.ps1 logs
```
Then read `AndroidConsolizer/test-output/SMAPI-latest.txt`. Expected: `DialogueBox patches applied.` appears at trace level during startup, and there are **no** `[DialogueBox] SetUpQuestions_Postfix error` lines and no Harmony patch-application errors.

- [ ] **Step 6: Mark the item done**

Once the user confirms all device tests pass:
- Move #22b from `TODO.md` (v3.8.0 section) to `DONE.md` with implementation notes (root cause, the `setUpQuestions` postfix, the gamepad gate).
- Update `STATUS.md` if appropriate (first v3.8.0 item complete).
- Commit the doc update as a separate `docs:` commit (documentation update, not a code change — does not get its own version bump).

---

## Self-Review

**1. Spec coverage:**
- "Pre-select the top option (`selectedResponse = 0`)" → Task 1, `SetUpQuestions_Postfix`. ✅
- "Postfix on `DialogueBox.setUpQuestions()`" → Task 1. ✅
- "Gate on `Game1.options.gamepadControls && !Game1.lastCursorMotionWasMouse`" → Task 1, postfix guard. ✅
- "No GMCM toggle" → no `ModConfig.cs` change anywhere in the plan. ✅
- "New patch file `Patches/DialogueBoxPatches.cs`" → Task 1. ✅
- "Register the patch in `ModEntry.cs`" → Task 2. ✅
- "Version bump to 3.7.1" → Task 3. ✅
- "Testing" section (gamepad pre-selection, up/down, event-dialogue path, touch unaffected) → Task 5, steps 2–4. ✅
- No gaps found.

**2. Placeholder scan:** No "TBD"/"TODO"/"implement later" in any step. Every code step shows complete code. Every command step shows the exact command and expected output. ✅

**3. Type consistency:** `DialogueBoxPatches`, `Apply`, `SetUpQuestions_Postfix`, `__instance.selectedResponse`, `__instance.responses` are used consistently across Task 1 (definition), Task 2 (registration call `Patches.DialogueBoxPatches.Apply`), and Task 4 (commit file list). The method name `setUpQuestions` (game method) matches the decompiled source. ✅
