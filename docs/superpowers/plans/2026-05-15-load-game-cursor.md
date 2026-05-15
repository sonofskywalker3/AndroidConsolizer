# v3.7.5 LoadGameMenu Diagnostic Implementation Plan (#35)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship v3.7.5, a log-only diagnostic Harmony patch on `LoadGameMenu` that captures the device's cursor / navigation state on the G Cloud, so the v3.7.6 fix design has the actual gate values to reason from.

**Architecture:** Two Harmony patches in a new file `Patches/LoadGameMenuDiagnosticPatches.cs`. (1) Postfix on `LoadGameMenu.draw(SpriteBatch)` snapshots gate state on change only (state hash compare, capped at 30 unique snapshots). (2) Prefix on `LoadGameMenu.receiveGamePadButton(Buttons)` logs every button press with the current `_joypadSelectedItemIndex`, capped at 50 entries. Reflection is used for the `private` `_joypadSelectedItemIndex` and `protected` `currentItemIndex` fields, with a `-999` sentinel on lookup failure. No GMCM toggle, no behaviour change. Pulled in v3.7.6.

**Tech Stack:** C# 9 (net6.0), HarmonyLib 2.x (via SMAPI), MonoGame XNA (`Microsoft.Xna.Framework.*`), SMAPI 4.0+ (`StardewModdingAPI.IMonitor`), reflection via `HarmonyLib.AccessTools`.

**Spec:** [`docs/superpowers/specs/2026-05-15-load-game-cursor-design.md`](../specs/2026-05-15-load-game-cursor-design.md).

**Project rules that override the skill defaults:**
- **One 0.0.1 commit per change.** All changes in this plan land in a *single* commit at Task 5. Do NOT commit between tasks. (Per [`.claude/CLAUDE.md`](../../../.claude/CLAUDE.md) → "MANDATORY: One Change Per Version" + "MANDATORY: No Bundling Changes".)
- **No unit tests for patches.** The project's MSTest scaffold (`AndroidControllerFix.Tests/`) is excluded from the build (commit `2fead04`). Verification is via the on-device test in the spec, not via TDD. Do not add a test project.
- **Never push.** Local commit only. Pushing to `origin/master` requires the user's explicit "yes, push." (Per workspace memory `feedback_no_auto_publish.md`.)
- **No `/sdcard/` paths anywhere.**

---

## File Structure

| Path | Action | Responsibility |
|---|---|---|
| `Patches/LoadGameMenuDiagnosticPatches.cs` | **Create** | Two Harmony patches + reflection helpers + on-change state hash + log caps. Self-contained, ~120 lines. |
| `ModEntry.cs` | **Modify** (insert one line at 166) | Register the new patch class via `Apply(harmony, this.Monitor)` next to `TitleMenuPatches.Apply(...)`. |
| `manifest.json` | **Modify** (line 4) | Bump `Version` from `3.7.4` to `3.7.5`. |

No other files touched. No existing code refactored. The `<EnableModDeploy>false</EnableModDeploy>` change in `AndroidConsolizer.csproj` is pre-existing and intentional (per `STATUS.md`) — leave it alone.

---

### Task 1: Create `Patches/LoadGameMenuDiagnosticPatches.cs`

**Files:**
- Create: `Patches/LoadGameMenuDiagnosticPatches.cs`

- [ ] **Step 1: Create the file with the full content below**

```csharp
using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// v3.7.5 diagnostic — #35 LoadGameMenu cursor / navigation.
    ///
    /// Two Harmony patches, log only, no behaviour change:
    ///   1. Postfix on LoadGameMenu.draw — snapshot gate state on change
    ///      (capped at MaxStateSnapshots).
    ///   2. Prefix on LoadGameMenu.receiveGamePadButton — log every press
    ///      (capped at MaxButtonLogs).
    ///
    /// Removed in the v3.7.6 fix commit. See
    /// docs/superpowers/specs/2026-05-15-load-game-cursor-design.md.
    /// </summary>
    internal static class LoadGameMenuDiagnosticPatches
    {
        private const int MaxStateSnapshots = 30;
        private const int MaxButtonLogs = 50;

        // -999 is the sentinel returned by ReadIntField when reflection failed
        // or the field can't be read. Chosen so a missing field is visually
        // distinct from a legitimate -1 ("no selection").
        private const int FieldUnavailable = -999;

        private static IMonitor Monitor;
        private static FieldInfo _joypadSelectedItemIndexField;
        private static FieldInfo _currentItemIndexField;

        private static int _stateSnapshotCount;
        private static int _buttonLogCount;
        private static string _lastStateHash;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                _joypadSelectedItemIndexField = AccessTools.Field(typeof(LoadGameMenu), "_joypadSelectedItemIndex");
                if (_joypadSelectedItemIndexField == null)
                {
                    Monitor.Log("[LoadGameDiag] Reflection: _joypadSelectedItemIndex not found.", LogLevel.Warn);
                }

                _currentItemIndexField = AccessTools.Field(typeof(LoadGameMenu), "currentItemIndex");
                if (_currentItemIndexField == null)
                {
                    Monitor.Log("[LoadGameDiag] Reflection: currentItemIndex not found.", LogLevel.Warn);
                }

                var draw = AccessTools.Method(
                    typeof(LoadGameMenu),
                    nameof(LoadGameMenu.draw),
                    new[] { typeof(SpriteBatch) });
                if (draw != null)
                {
                    harmony.Patch(
                        original: draw,
                        postfix: new HarmonyMethod(typeof(LoadGameMenuDiagnosticPatches), nameof(Draw_Postfix))
                    );
                }
                else
                {
                    Monitor.Log("[LoadGameDiag] LoadGameMenu.draw not found — Draw_Postfix patch skipped.", LogLevel.Warn);
                }

                var receiveGamePad = AccessTools.Method(
                    typeof(LoadGameMenu),
                    nameof(LoadGameMenu.receiveGamePadButton),
                    new[] { typeof(Buttons) });
                if (receiveGamePad != null)
                {
                    harmony.Patch(
                        original: receiveGamePad,
                        prefix: new HarmonyMethod(typeof(LoadGameMenuDiagnosticPatches), nameof(ReceiveGamePadButton_Prefix))
                    );
                }
                else
                {
                    Monitor.Log("[LoadGameDiag] LoadGameMenu.receiveGamePadButton not found — prefix patch skipped.", LogLevel.Warn);
                }

                Monitor.Log("[LoadGameDiag] Patches applied (v3.7.5 diagnostic).", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[LoadGameDiag] Failed to apply patches: {ex.Message}", LogLevel.Error);
            }
        }

        private static void Draw_Postfix(LoadGameMenu __instance, SpriteBatch b)
        {
            try
            {
                if (_stateSnapshotCount >= MaxStateSnapshots) return;

                int joypadIdx = ReadIntField(_joypadSelectedItemIndexField, __instance);
                int currentIdx = ReadIntField(_currentItemIndexField, __instance);

                var snapped = __instance.currentlySnappedComponent;
                string snappedDesc = snapped != null
                    ? $"id={snapped.myID},region={snapped.region},bounds=({snapped.bounds.X},{snapped.bounds.Y},{snapped.bounds.Width},{snapped.bounds.Height})"
                    : "null";

                int slotCount = __instance.MenuSlots?.Count ?? -1;
                int mouseX = Game1.getMouseX();
                int mouseY = Game1.getMouseY();

                string hash =
                    $"{Game1.options.snappyMenus}|{Game1.options.gamepadControls}|{Game1.lastCursorMotionWasMouse}"
                    + $"|{Game1.mouseCursorTransparency}|{mouseX}|{mouseY}|{snappedDesc}"
                    + $"|{joypadIdx}|{currentIdx}|{slotCount}";

                if (hash == _lastStateHash) return;
                _lastStateHash = hash;

                Monitor.Log(
                    $"[LoadGameDiag] frame={_stateSnapshotCount} "
                    + $"snappy={Game1.options.snappyMenus} gamepad={Game1.options.gamepadControls} "
                    + $"lastMotionMouse={Game1.lastCursorMotionWasMouse} cursorAlpha={Game1.mouseCursorTransparency} "
                    + $"mouse=({mouseX},{mouseY}) snapped={snappedDesc} "
                    + $"_joypadSelectedItemIndex={joypadIdx} currentItemIndex={currentIdx} slotCount={slotCount}",
                    LogLevel.Info);

                _stateSnapshotCount++;
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LoadGameDiag] Draw_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }

        private static void ReceiveGamePadButton_Prefix(LoadGameMenu __instance, Buttons b)
        {
            try
            {
                if (_buttonLogCount >= MaxButtonLogs) return;

                int joypadIdx = ReadIntField(_joypadSelectedItemIndexField, __instance);
                int slotCount = __instance.MenuSlots?.Count ?? -1;

                Monitor.Log(
                    $"[LoadGameDiag] receiveGamePadButton b={b} "
                    + $"_joypadSelectedItemIndex={joypadIdx} slotCount={slotCount}",
                    LogLevel.Info);

                _buttonLogCount++;
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LoadGameDiag] ReceiveGamePadButton_Prefix error: {ex.Message}", LogLevel.Error);
            }
        }

        private static int ReadIntField(FieldInfo field, object instance)
        {
            if (field == null) return FieldUnavailable;
            try
            {
                return (int)field.GetValue(instance);
            }
            catch
            {
                return FieldUnavailable;
            }
        }
    }
}
```

- [ ] **Step 2: Confirm the file exists and the namespace matches**

The namespace must be exactly `AndroidConsolizer.Patches` (matches every other file in `Patches/`, e.g. `Patches/TitleMenuPatches.cs:9`). The class must be `internal static`. Both are true in the code above — just verify after writing.

---

### Task 2: Register the patch in `ModEntry.cs`

**Files:**
- Modify: `ModEntry.cs:166` (insert one new line; do not delete or reorder existing lines)

**Current content** (`ModEntry.cs:163-167`):

```csharp
            Patches.OptionsPagePatches.Apply(harmony, this.Monitor);
            Patches.DialogueBoxPatches.Apply(harmony, this.Monitor);
            Patches.TitleMenuPatches.Apply(harmony, this.Monitor);
            Patches.BootDiagnosticPatches.Apply(harmony, this.Monitor);
            Patches.BootDiagnosticPatches.ApplyAdditionalPatches(harmony, this.Monitor);
```

- [ ] **Step 1: Insert the new registration line immediately after `Patches.TitleMenuPatches.Apply(...)`**

After the edit, lines 163-168 read:

```csharp
            Patches.OptionsPagePatches.Apply(harmony, this.Monitor);
            Patches.DialogueBoxPatches.Apply(harmony, this.Monitor);
            Patches.TitleMenuPatches.Apply(harmony, this.Monitor);
            Patches.LoadGameMenuDiagnosticPatches.Apply(harmony, this.Monitor);
            Patches.BootDiagnosticPatches.Apply(harmony, this.Monitor);
            Patches.BootDiagnosticPatches.ApplyAdditionalPatches(harmony, this.Monitor);
```

Use the `Edit` tool with `old_string = "            Patches.TitleMenuPatches.Apply(harmony, this.Monitor);\n            Patches.BootDiagnosticPatches.Apply(harmony, this.Monitor);"` and `new_string = "            Patches.TitleMenuPatches.Apply(harmony, this.Monitor);\n            Patches.LoadGameMenuDiagnosticPatches.Apply(harmony, this.Monitor);\n            Patches.BootDiagnosticPatches.Apply(harmony, this.Monitor);"`. The two-line `old_string` makes the match unique without re-reading the surrounding context.

---

### Task 3: Bump version in `manifest.json`

**Files:**
- Modify: `manifest.json:4`

**Current content** (line 4):

```json
    "Version": "3.7.4",
```

- [ ] **Step 1: Change the version string**

Use `Edit` with `old_string = "    \"Version\": \"3.7.4\","` and `new_string = "    \"Version\": \"3.7.5\","`.

After the edit, line 4 reads:

```json
    "Version": "3.7.5",
```

---

### Task 4: Build and verify

**Files:** None modified.

- [ ] **Step 1: Run the release build**

```powershell
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"; dotnet build AndroidConsolizer.csproj -c Release
```

Expected: build completes with `Build succeeded.` and `0 Error(s)`. Warnings about `LF will be replaced by CRLF` are normal and ignorable. If the build reports compilation errors, fix them before continuing — do **not** commit a broken build.

- [ ] **Step 2: Verify the output zip exists with the correct version**

```powershell
Get-ChildItem "bin/Release/net6.0/AndroidConsolizer 3.7.5.zip"
```

Expected: one file listed, modified time within the last minute. If `AndroidConsolizer 3.7.4.zip` is the only file present, the manifest bump (Task 3) didn't happen — go fix it.

- [ ] **Step 3: Sanity-check the patch class compiled in**

```powershell
Test-Path "bin/Release/net6.0/AndroidConsolizer.dll"
```

Expected: `True`. (A compile error would have failed Step 1 first; this is a belt-and-braces check that the DLL was emitted.)

---

### Task 5: Commit

**Files:** Stage exactly the three files this patch touches. Do NOT use `git add .` or `git add -A`.

- [ ] **Step 1: Stage the three changed files**

```powershell
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"; git add Patches/LoadGameMenuDiagnosticPatches.cs ModEntry.cs manifest.json
```

- [ ] **Step 2: Confirm only those three files are staged**

```powershell
git status --short
```

Expected output (the leading `A` is "added", `M` is "modified" — the order may vary):

```
A  Patches/LoadGameMenuDiagnosticPatches.cs
M  ModEntry.cs
M  manifest.json
 M AndroidConsolizer.csproj
 D chest_layout_debug.html
?? <other untracked dirs>
```

The pre-existing unstaged changes (` M AndroidConsolizer.csproj`, ` D chest_layout_debug.html`, `??` lines) MUST stay unstaged — they belong to the user's broader working tree and are not part of v3.7.5. If any of those appear under the staged section (no leading space), unstage them with `git restore --staged <path>` and re-check.

- [ ] **Step 3: Create the commit**

```powershell
git commit -m @'
v3.7.5: #35 LoadGameMenu diagnostic — log gate state and gamepad button routing

Adds Patches/LoadGameMenuDiagnosticPatches.cs with two log-only Harmony
patches against StardewValley.Menus.LoadGameMenu:

- Postfix on draw(SpriteBatch): snapshots gate state (snappyMenus,
  gamepadControls, lastCursorMotionWasMouse, mouseCursorTransparency,
  mouse XY, currentlySnappedComponent, _joypadSelectedItemIndex,
  currentItemIndex, MenuSlots.Count) on change only, capped at 30
  unique snapshots.
- Prefix on receiveGamePadButton(Buttons): logs every press with the
  current _joypadSelectedItemIndex, capped at 50 entries.

No behaviour change. Reflection is used for the private
_joypadSelectedItemIndex and protected currentItemIndex fields;
sentinel -999 distinguishes a missing field from a legitimate -1
"no selection". To be removed in the v3.7.6 fix.

Spec: docs/superpowers/specs/2026-05-15-load-game-cursor-design.md
'@
```

- [ ] **Step 4: Verify the commit landed**

```powershell
git log --oneline -1
```

Expected: top line is `<sha> v3.7.5: #35 LoadGameMenu diagnostic — log gate state and gamepad button routing`.

```powershell
git show --stat HEAD
```

Expected file list:

```
 ModEntry.cs                                                  | 1 +
 Patches/LoadGameMenuDiagnosticPatches.cs                     | <NN> ++++++
 manifest.json                                                | 2 +-
```

If any other file appears in this list, the commit is wrong — do NOT push, and instead inspect with `git show HEAD` and decide whether to amend or revert.

- [ ] **Step 5: STOP. Do not push. Do not deploy. Do not move to v3.7.6 design.**

The user explicitly performs the device deploy + test for #35. Per workspace memory `feedback_no_auto_publish.md`: anything beyond a local commit requires the user's explicit yes-push. Hand back at this point with: build artifact path (`bin/Release/net6.0/AndroidConsolizer 3.7.5.zip`), commit SHA, and the test sequence quoted from the spec for the user's convenience:

> 1. Boot to title.
> 2. Tap Load Game (touch).
> 3. Wait ~2 seconds for save list to populate.
> 4. D-pad up, then down, then A, then B (or close button).
> 5. Pull log via `SyncdewValley/sync.ps1 logs`.

The user runs `SyncdewValley/sync.ps1 deploy` themselves and reports back with the pulled log. Phase 2 (the v3.7.6 fix design) gets appended to the same spec file once that log is in.
