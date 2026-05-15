# v3.7.6 LoadGameMenu Cursor on Slot 0 Implementation Plan (#35)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship v3.7.6 — when `LoadGameMenu` opens and the async save scan completes, snap `_joypadSelectedItemIndex = 0` and call vanilla `snapToDefaultClickableComponent()` so the cursor lands on the top save slot. Replace (and delete) the v3.7.5 diagnostic patch.

**Architecture:** One Harmony postfix on `LoadGameMenu.update(GameTime)` in a new file `Patches/LoadGameMenuPatches.cs`. Reflection lookup for `_joypadSelectedItemIndex` (private, decompile line 276) cached at `Apply()`. Postfix gates on `currentlySnappedComponent == null && slotButtons.Count > 0` — naturally fires once per fresh menu instance, no static state. The v3.7.5 diagnostic file (`Patches/LoadGameMenuDiagnosticPatches.cs`) is deleted; `ModEntry.cs` registration line is updated to point at the new class.

**Tech Stack:** C# 9 (net6.0), HarmonyLib 2.x (via SMAPI), MonoGame XNA, SMAPI 4.0+ (`StardewModdingAPI.IMonitor`), reflection via `HarmonyLib.AccessTools`.

**Spec:** [`docs/superpowers/specs/2026-05-15-load-game-cursor-design.md`](../specs/2026-05-15-load-game-cursor-design.md) — read the **Revision — 2026-05-15 (Phase 2: Fix, target v3.7.6)** section.

**Project rules that override skill defaults:**
- **One 0.0.1 commit per change.** All changes in this plan land in a single commit at Task 6. Do NOT commit between tasks.
- **No unit tests for patches.** Verification is the on-device test in the spec, not TDD. Don't add a test project, don't modify the excluded `AndroidControllerFix.Tests/` directory.
- **`git add <specific files>` only.** Never `git add .` or `git add -A`.
- **NEVER push.** Local commit only.
- **Pre-existing uncommitted state stays uncommitted.** `AndroidConsolizer.csproj` modification is intentional (per STATUS.md) — leave it. The untracked dirs (`Logs/`, `marketing/`, `references/`, `release/`, `release-notes/`, `sync/`, `tools/`, `AndroidControllerFix.Tests/`) belong to the user's broader working tree.
- **Don't touch `Patches/TitleMenuPatches.cs`.** It's the v3.7.4 fix for the title cursor and unrelated to v3.7.6.

---

## File Structure

| Path | Action | Responsibility |
|---|---|---|
| `Patches/LoadGameMenuDiagnosticPatches.cs` | **Delete** | v3.7.5 diagnostic — no longer needed; the data it produced is captured in the spec's Phase 2 revision. |
| `Patches/LoadGameMenuPatches.cs` | **Create** | Single Harmony postfix on `LoadGameMenu.update(GameTime)` that snaps cursor to slot 0 once per menu instance. |
| `ModEntry.cs` | **Modify** (line 166) | Replace `LoadGameMenuDiagnosticPatches.Apply(...)` with `LoadGameMenuPatches.Apply(...)`. |
| `manifest.json` | **Modify** (line 4) | Bump `Version` from `3.7.5` to `3.7.6`. |

No other files touched.

---

### Task 1: Delete the v3.7.5 diagnostic patch file

**Files:**
- Delete: `Patches/LoadGameMenuDiagnosticPatches.cs`

- [ ] **Step 1: Remove the file via `git rm`**

```powershell
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"; git rm Patches/LoadGameMenuDiagnosticPatches.cs
```

Expected output: `rm 'Patches/LoadGameMenuDiagnosticPatches.cs'`. The file is removed from the working tree AND staged for deletion.

- [ ] **Step 2: Verify it's gone**

```powershell
Test-Path "Patches/LoadGameMenuDiagnosticPatches.cs"
```

Expected: `False`.

---

### Task 2: Create `Patches/LoadGameMenuPatches.cs`

**Files:**
- Create: `Patches/LoadGameMenuPatches.cs`

- [ ] **Step 1: Create the file with the full content below**

```csharp
using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// v3.7.6 fix — #35 LoadGameMenu cursor on slot 0.
    ///
    /// On Android, opening LoadGameMenu via touch leaves the cursor at the
    /// touch position (typically the Load Game button area, well below the
    /// save list). The user has to press DPad once to jog it onto slot 0,
    /// which is both an extra step and a visual surprise — slot 0 is "above"
    /// the touch position, but DPadUp on _joypadSelectedItemIndex == -1
    /// snaps to slot 0 rather than scrolling.
    ///
    /// "Fix the data" approach: when the menu opens and the async save scan
    /// completes, set _joypadSelectedItemIndex = 0 (private field, reflection)
    /// and call the game's own snapToDefaultClickableComponent(), which then
    /// reads the index, sets currentlySnappedComponent = slotButtons[0], and
    /// calls Game1.setMousePosition(slot.bounds.Center).
    ///
    /// The gate (currentlySnappedComponent == null && slotButtons.Count > 0)
    /// fires once per fresh LoadGameMenu instance — once we snap, both fields
    /// are set, so the postfix becomes a no-op for that instance. A new
    /// LoadGameMenu construction resets both back to default and the snap
    /// fires again. No static "did we snap" state needed.
    ///
    /// Spec: docs/superpowers/specs/2026-05-15-load-game-cursor-design.md
    /// (Revision — 2026-05-15 / Phase 2).
    /// </summary>
    internal static class LoadGameMenuPatches
    {
        private static IMonitor Monitor;
        private static FieldInfo _joypadSelectedItemIndexField;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                _joypadSelectedItemIndexField = AccessTools.Field(typeof(LoadGameMenu), "_joypadSelectedItemIndex");
                if (_joypadSelectedItemIndexField == null)
                {
                    Monitor.Log("[LoadGameMenu] Reflection: _joypadSelectedItemIndex not found — postfix will no-op.", LogLevel.Warn);
                }

                var update = AccessTools.Method(
                    typeof(LoadGameMenu),
                    nameof(LoadGameMenu.update),
                    new[] { typeof(GameTime) });
                if (update != null)
                {
                    harmony.Patch(
                        original: update,
                        postfix: new HarmonyMethod(typeof(LoadGameMenuPatches), nameof(Update_Postfix))
                    );
                    Monitor.Log("[LoadGameMenu] Patches applied (v3.7.6 fix).", LogLevel.Trace);
                }
                else
                {
                    Monitor.Log("[LoadGameMenu] LoadGameMenu.update not found — patch skipped.", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[LoadGameMenu] Failed to apply patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Postfix on LoadGameMenu.update — when the menu is freshly open and
        /// the async save scan has populated slotButtons, snap the cursor to
        /// slot 0 by setting _joypadSelectedItemIndex and calling vanilla
        /// snapToDefaultClickableComponent().
        /// </summary>
        private static void Update_Postfix(LoadGameMenu __instance, GameTime time)
        {
            try
            {
                if (__instance.currentlySnappedComponent != null) return;
                if (__instance.slotButtons == null || __instance.slotButtons.Count == 0) return;
                if (_joypadSelectedItemIndexField == null) return;

                _joypadSelectedItemIndexField.SetValue(__instance, 0);
                __instance.snapToDefaultClickableComponent();
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LoadGameMenu] Update_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
```

- [ ] **Step 2: Verify file created with correct namespace**

```powershell
Test-Path "Patches/LoadGameMenuPatches.cs"
```

Expected: `True`. Spot-check that line 9 is `namespace AndroidConsolizer.Patches`.

---

### Task 3: Update the `ModEntry.cs` registration

**Files:**
- Modify: `ModEntry.cs:166`

The v3.7.5 commit added `Patches.LoadGameMenuDiagnosticPatches.Apply(harmony, this.Monitor);` immediately after `Patches.TitleMenuPatches.Apply(...)`. We replace that single line with the new fix-class registration.

**Current content** (`ModEntry.cs:165-167`):

```csharp
            Patches.TitleMenuPatches.Apply(harmony, this.Monitor);
            Patches.LoadGameMenuDiagnosticPatches.Apply(harmony, this.Monitor);
            Patches.BootDiagnosticPatches.Apply(harmony, this.Monitor);
```

- [ ] **Step 1: Replace the diagnostic registration with the fix registration**

Use `Edit` with:
- `old_string = "            Patches.LoadGameMenuDiagnosticPatches.Apply(harmony, this.Monitor);"`
- `new_string = "            Patches.LoadGameMenuPatches.Apply(harmony, this.Monitor);"`

After the edit, lines 165-167 read:

```csharp
            Patches.TitleMenuPatches.Apply(harmony, this.Monitor);
            Patches.LoadGameMenuPatches.Apply(harmony, this.Monitor);
            Patches.BootDiagnosticPatches.Apply(harmony, this.Monitor);
```

The single-line `old_string` is unique in the file (only the diagnostic class registers under that name).

---

### Task 4: Bump version in `manifest.json`

**Files:**
- Modify: `manifest.json:4`

**Current content** (line 4):

```json
    "Version": "3.7.5",
```

- [ ] **Step 1: Change the version string**

Use `Edit` with `old_string = "    \"Version\": \"3.7.5\","` and `new_string = "    \"Version\": \"3.7.6\","`.

After the edit, line 4 reads:

```json
    "Version": "3.7.6",
```

---

### Task 5: Build and verify

**Files:** None modified.

- [ ] **Step 1: Run the release build**

```powershell
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"; dotnet build AndroidConsolizer.csproj -c Release
```

Expected: `Build succeeded.` with `0 Error(s)`. The `LF will be replaced by CRLF` warnings are normal. If there are compilation errors, fix them before continuing — do **not** commit a broken build. Most likely error if anything goes wrong: a stale reference to `LoadGameMenuDiagnosticPatches` somewhere unexpected; verify with `git grep` and report.

- [ ] **Step 2: Verify the new zip exists**

```powershell
Get-ChildItem "bin/Release/net6.0/AndroidConsolizer 3.7.6.zip"
```

Expected: one file listed, modified time within the last minute. If only `AndroidConsolizer 3.7.5.zip` exists, the manifest bump (Task 4) didn't happen.

- [ ] **Step 3: Confirm the v3.7.5 zip is still there (we don't delete previous artifacts)**

```powershell
Get-ChildItem "bin/Release/net6.0/AndroidConsolizer*.zip" | Select-Object Name, Length, LastWriteTime
```

Expected: at least `AndroidConsolizer 3.7.5.zip` (older) and `AndroidConsolizer 3.7.6.zip` (just built).

---

### Task 6: Commit

**Files:** Stage exactly the four files this patch touches: one delete, one create, two modifications.

- [ ] **Step 1: Stage the four changes**

The deletion was already staged by `git rm` in Task 1. Now stage the create + two modifications:

```powershell
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"; git add Patches/LoadGameMenuPatches.cs ModEntry.cs manifest.json
```

- [ ] **Step 2: Verify exactly four files are staged**

```powershell
git status --short
```

Expected output (order may vary; `D` = deleted, `A` = added, `M` = modified):

```
M  ModEntry.cs
A  Patches/LoadGameMenuPatches.cs
D  Patches/LoadGameMenuDiagnosticPatches.cs
M  manifest.json
 M AndroidConsolizer.csproj
 D chest_layout_debug.html
?? <untracked dirs>
```

The pre-existing unstaged changes (` M AndroidConsolizer.csproj`, ` D chest_layout_debug.html`, the `??` untracked dirs) MUST stay unstaged. If any of those appear in the staged section (no leading space before the letter), unstage them with `git restore --staged <path>` and re-check.

- [ ] **Step 3: Create the commit**

```powershell
git commit -m @'
v3.7.6: #35 LoadGameMenu cursor on slot 0 — snap on first update with slots loaded

Replaces the v3.7.5 diagnostic with a one-postfix fix on
StardewValley.Menus.LoadGameMenu.update(GameTime). When the menu is
freshly open (currentlySnappedComponent == null) and the async save
scan has populated slotButtons (Count > 0), set
_joypadSelectedItemIndex = 0 (private field, via reflection) and call
the game's own snapToDefaultClickableComponent(), which then reads
the index, picks slotButtons[0] as currentlySnappedComponent, and
calls Game1.setMousePosition to slot 0's bounds center.

Why a postfix on update: the menu's _initTask save scan completes
asynchronously, so we can't snap from the constructor. The gate
self-resets per fresh LoadGameMenu instance — no static "did we
snap" state needed.

What this fixes (G Cloud, v3.7.5 diagnostic finding): cursor used
to land at the touch position on the Load Game button area
(typically (973, 836)) instead of slot 0. Now lands on slot 0 the
moment the save list populates. Side benefit: the controller user's
first DPadDown advances straight to slot 1 instead of being
absorbed by the vanilla "claim slot 0" early-return at decompile
LoadGameMenu.cs:524.

What this does not touch: TitleMenuPatches.cs (v3.7.4 cursor fix,
unrelated). No GMCM toggle, no behaviour change for any other menu.
The v3.7.5 diagnostic file is deleted.

Spec: docs/superpowers/specs/2026-05-15-load-game-cursor-design.md
(Revision — 2026-05-15 / Phase 2).
'@
```

- [ ] **Step 4: Verify the commit landed**

```powershell
git log --oneline -1
```

Expected: top line is `<sha> v3.7.6: #35 LoadGameMenu cursor on slot 0 — snap on first update with slots loaded`.

```powershell
git show --stat HEAD
```

Expected file list:

```
 ModEntry.cs                                        |   2 +-
 Patches/LoadGameMenuDiagnosticPatches.cs           | 178 ----------------------
 Patches/LoadGameMenuPatches.cs                     |  <NN> ++++++
 manifest.json                                      |   2 +-
```

Four files in the commit: one deleted (-178 lines), one created, two modified by 1 line each. If any other file appears, do NOT push, and inspect with `git show HEAD`.

- [ ] **Step 5: STOP. Do not push. Do not deploy. Hand back.**

Per workspace memory `feedback_no_auto_publish.md` and `feedback_deploy_and_logs_are_mine.md`: anything beyond a local commit requires the user's explicit yes-push. Hand back at this point with: build artifact path (`bin/Release/net6.0/AndroidConsolizer 3.7.6.zip`), commit SHA, and the test sequence quoted from the spec for the user to run on the G Cloud:

> 1. Boot to title.
> 2. Touch-tap Load Game.
> 3. Wait ~2 sec for the save list to populate.
> 4. Confirm cursor is on **slot 0** (top save) without any controller input.
> 5. DPadDown once → cursor jumps to slot 1.
> 6. A → slot 1 loads.
> 7. Re-open Load Game and confirm step 4 still happens.

The Claude controller deploys via `SyncdewValley/sync.ps1 deploy` and pulls logs via `SyncdewValley/sync.ps1 logs` — the user only does the on-device test and reports back.
