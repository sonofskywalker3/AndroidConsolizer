# v3.7.16 LetterViewerMenu Fix Implementation Plan (#39)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship v3.7.16, the real fix for #39 — replace the v3.7.15 diagnostic patches in `Patches/LetterViewerMenuPatches.cs` with a single `Constructor_Postfix` on `LetterViewerMenu(string)` that runs the snap initialization block missing from that overload, mirroring `LetterViewerMenu(string, string, bool)` lines 176-185.

**Architecture:** Single Harmony postfix patch on the `LetterViewerMenu(string)` constructor. When `Game1.options.SnappyMenus` is true, call `populateClickableComponentList()`, then `snapToDefaultClickableComponent()`, then disable the nav buttons (`myID = -100`) when there's only one page. Pure "fix the data" — give the menu the same initialization its sibling overload already does, let the game's snappy nav handle everything else. The four diagnostic patches and all log/cap state from v3.7.15 are deleted.

**Tech Stack:** C# 9 (net6.0), HarmonyLib 2.x (via SMAPI), MonoGame XNA (`Microsoft.Xna.Framework.*`), SMAPI 4.0+ (`StardewModdingAPI.IMonitor`).

**Spec:** [`docs/superpowers/specs/2026-05-16-monster-eradication-cursor-design.md`](../specs/2026-05-16-monster-eradication-cursor-design.md) (Revision section dated 2026-05-17).

**Project rules that override the skill defaults:**
- **One 0.0.1 commit per change.** All changes in this plan land in a *single* commit at Task 4. Do NOT commit between tasks. (Per [`.claude/CLAUDE.md`](../../../.claude/CLAUDE.md) → "MANDATORY: One Change Per Version" + "MANDATORY: No Bundling Changes". Deleting the now-obsolete diagnostic + adding the fix it informed counts as ONE coherent change, matching the v3.7.3→v3.7.4 (#17) and v3.7.5→v3.7.6 (#35) pattern.)
- **No unit tests for patches.** The project's MSTest scaffold is excluded from the build. Verification is via the on-device test in the spec.
- **Never push.** Local commit only.
- **No `/sdcard/` paths anywhere.**

---

## File Structure

| Path | Action | Responsibility |
|---|---|---|
| `Patches/LetterViewerMenuPatches.cs` | **Rewrite** | Replace 246-line diagnostic with ~80-line fix. Single Harmony postfix on `LetterViewerMenu(string)` ctor that runs the missing snap block. |
| `ModEntry.cs` | **No change** | Existing `Patches.LetterViewerMenuPatches.Apply(harmony, this.Monitor);` line stays as-is. |
| `manifest.json` | **Modify** (line 4) | Bump `Version` from `3.7.15` to `3.7.16`. |

---

### Task 1: Rewrite `Patches/LetterViewerMenuPatches.cs`

**Files:**
- Modify (full rewrite): `Patches/LetterViewerMenuPatches.cs`

- [ ] **Step 1: Replace the entire file with the content below**

Use the `Write` tool (not `Edit`) — the existing file is being fully replaced, not incrementally modified.

```csharp
using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// v3.7.16 fix — #39 Adventure Guild Monster Eradication tracking page
    /// (and any other Game1.drawLetterMessage caller).
    ///
    /// Root cause: LetterViewerMenu has three constructor overloads. The
    /// (string, string, bool) overload used by mailbox letters runs a snap
    /// initialization block at lines 176-185 of the decompile:
    ///
    ///     if (Game1.options.SnappyMenus)
    ///     {
    ///         populateClickableComponentList();
    ///         snapToDefaultClickableComponent();
    ///         if (mailMessage != null and mailMessage.Count &lt;= 1)
    ///         {
    ///             backButton.myID = -100;
    ///             forwardButton.myID = -100;
    ///         }
    ///     }
    ///
    /// The (string) overload used by Game1.drawLetterMessage (and therefore
    /// AdventureGuild.showMonsterKillList) is missing this block entirely.
    /// Result: currentlySnappedComponent stays null on entry, the cursor
    /// isn't moved to the forward arrow, the vanilla "breathing" pulse
    /// animation runs, and A does nothing until the first joystick input
    /// lazily triggers IClickableMenu's snappy-nav path.
    ///
    /// Fix: postfix the (string) ctor with the same snap block. Pure data
    /// fix - once the menu state matches what the mailbox overload produces,
    /// all downstream behaviour (cursor, A-to-turn-page, B-to-close) works
    /// via existing vanilla code paths.
    ///
    /// Confirmed by v3.7.15 diagnostic on GR0006 (test-output/SMAPI-latest.txt
    /// lines 1823-1880). See docs/superpowers/specs/2026-05-16-monster-eradication-cursor-design.md.
    /// </summary>
    internal static class LetterViewerMenuPatches
    {
        private static IMonitor Monitor;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                var ctorString = AccessTools.Constructor(
                    typeof(LetterViewerMenu),
                    new[] { typeof(string) });
                if (ctorString != null)
                {
                    harmony.Patch(
                        original: ctorString,
                        postfix: new HarmonyMethod(typeof(LetterViewerMenuPatches), nameof(CtorString_Postfix))
                    );
                    Monitor.Log("[LetterViewerMenu] Patches applied (v3.7.16 fix).", LogLevel.Trace);
                }
                else
                {
                    Monitor.Log("[LetterViewerMenu] LetterViewerMenu(string) ctor not found - fix skipped.", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[LetterViewerMenu] Failed to apply patches: {ex.Message}", LogLevel.Error);
            }
        }

        private static void CtorString_Postfix(LetterViewerMenu __instance)
        {
            try
            {
                if (!Game1.options.SnappyMenus) return;

                __instance.populateClickableComponentList();
                __instance.snapToDefaultClickableComponent();

                if (__instance.mailMessage != null && __instance.mailMessage.Count <= 1)
                {
                    if (__instance.backButton != null) __instance.backButton.myID = -100;
                    if (__instance.forwardButton != null) __instance.forwardButton.myID = -100;
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LetterViewerMenu] CtorString_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
```

- [ ] **Step 2: Confirm the rewrite removed the diagnostic patches**

After writing, the file should be approximately 80 lines, with:
- One `internal static class LetterViewerMenuPatches` containing only `Apply` and `CtorString_Postfix`.
- No `MaxButtonLogs`, `MaxUpdateSnapshots`, `FirstPagePrefixLen` constants.
- No `_buttonLogCount`, `_updateSnapshotCount`, `_lastUpdateHash` state.
- No `CtorMail_Postfix`, `LogOpen`, `ReceiveGamePadButton_Prefix`, `Update_Postfix` methods.
- No `[LetterDiag]` log lines.

If any of those leftovers remain, the rewrite is incomplete.

---

### Task 2: Bump version in `manifest.json`

**Files:**
- Modify: `manifest.json:4`

**Current content** (line 4):

```json
    "Version": "3.7.15",
```

- [ ] **Step 1: Change the version string**

Use `Edit` with:
- `old_string = "    \"Version\": \"3.7.15\","`
- `new_string = "    \"Version\": \"3.7.16\","`

After the edit, line 4 reads:

```json
    "Version": "3.7.16",
```

---

### Task 3: Build and verify

**Files:** None modified.

- [ ] **Step 1: Run the release build**

```powershell
dotnet build "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer\AndroidConsolizer.csproj" -c Release
```

Expected: build completes with `Build succeeded.` and `0 Error(s)`. Warnings about `LF will be replaced by CRLF` are normal and ignorable. If the build reports compilation errors, fix them before continuing — do **not** commit a broken build.

Things to watch for specifically:
- `populateClickableComponentList()` is an inherited public method on `IClickableMenu` — should resolve without issue.
- `snapToDefaultClickableComponent()` is overridden public on `LetterViewerMenu` — should resolve.
- `backButton` and `forwardButton` are public fields, `myID` is a public field on `ClickableComponent`.

If any of those calls fail with "method not found" on the Android port, that's a real problem — STOP and report it as a blocker rather than working around it.

- [ ] **Step 2: Verify the output zip exists with the correct version**

```powershell
Get-ChildItem "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer\bin\Release\net6.0\AndroidConsolizer 3.7.16.zip"
```

Expected: one file listed, modified time within the last minute. If only `AndroidConsolizer 3.7.15.zip` is present, the manifest bump (Task 2) didn't happen — go fix it.

- [ ] **Step 3: Sanity-check the DLL was emitted**

```powershell
Test-Path "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer\bin\Release\net6.0\AndroidConsolizer.dll"
```

Expected: `True`.

---

### Task 4: Commit

**Files:** Stage exactly the two files this patch touches. Do NOT use `git add .` or `git add -A`.

- [ ] **Step 1: Stage the two changed files**

```powershell
git -C "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer" add Patches/LetterViewerMenuPatches.cs manifest.json
```

Note: `ModEntry.cs` is NOT staged this round — the registration line is unchanged from v3.7.15.

- [ ] **Step 2: Confirm only those two files are staged**

```powershell
git -C "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer" status --short
```

Expected output (the leading `M` is "modified" — `Patches/LetterViewerMenuPatches.cs` should show `M` not `A`, since the file already exists in git):

```
M  Patches/LetterViewerMenuPatches.cs
M  manifest.json
 M AndroidConsolizer.csproj
 D chest_layout_debug.html
?? <other untracked dirs>
```

The pre-existing unstaged changes MUST stay unstaged. If any of those appear under the staged section (no leading space), unstage them with `git restore --staged <path>` and re-check.

- [ ] **Step 3: Create the commit**

```powershell
git -C "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer" commit -m @'
v3.7.16: #39 LetterViewerMenu fix — snap on (string) ctor to match mailbox overload

Root cause confirmed by v3.7.15 diagnostic on GR0006 (test-output/SMAPI-latest.txt
lines 1823-1880): LetterViewerMenu(string) - used by Game1.drawLetterMessage
and therefore by AdventureGuild.showMonsterKillList - is missing the snap
initialization block that LetterViewerMenu(string, string, bool) runs at
decompile lines 176-185. Without it, currentlySnappedComponent stays null on
entry, the cursor isn't moved to the forward arrow, the vanilla "breathing"
pulse animation runs, and A does nothing until the first joystick input
lazily triggers IClickableMenu's snappy-nav path.

Fix: rewrite Patches/LetterViewerMenuPatches.cs - delete the four diagnostic
patches (CtorString/CtorMail/ReceiveGamePadButton/Update postfixes plus LogOpen
helper and log caps), replace with a single Constructor_Postfix on the (string)
overload that runs the mirrored snap block when Game1.options.SnappyMenus is
true. Pure "fix the data" - once menu state matches the mailbox overload's
output, all downstream behaviour (cursor on forward arrow, A turns the page,
breathing stops, snap to back arrow on last page) works via existing vanilla
code paths.

Scope: applies to all Game1.drawLetterMessage callers, not just the kill list.
Mailbox letters (string, string, bool overload) unaffected. Single-page letters
get backButton.myID = forwardButton.myID = -100 per the mirrored block.

Spec: docs/superpowers/specs/2026-05-16-monster-eradication-cursor-design.md
'@
```

- [ ] **Step 4: Verify the commit landed**

```powershell
git -C "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer" log --oneline -1
```

Expected: top line is `<sha> v3.7.16: #39 LetterViewerMenu fix — snap on (string) ctor to match mailbox overload`.

```powershell
git -C "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer" show --stat HEAD
```

Expected file list:

```
 Patches/LetterViewerMenuPatches.cs       | <NN> +++++++-------
 manifest.json                            | 2 +-
```

`<NN>` will show many additions AND many deletions, since this is a rewrite (newly: ~80 lines, was: 246 lines).

If `ModEntry.cs` appears in this list, the commit is wrong — that file shouldn't change this round.

- [ ] **Step 5: STOP. Do not push. Do not deploy.**

Hand back to the controller with:
- Commit SHA
- Build artifact: `bin/Release/net6.0/AndroidConsolizer 3.7.16.zip`
- Test sequence quoted for convenience:

> **Test 1 — Adventure Guild kill list (GR0006):**
> 1. Load save → travel to Adventurer's Guild
> 2. Interact with kill list board
> 3. **Cursor should be on the forward arrow on entry**, NOT breathing
> 4. **A immediately turns the page** (no joystick wiggle needed)
> 5. Page 2: cursor on back arrow, A returns to page 1
> 6. B closes
>
> **Test 2 — Mailbox letter (sanity, only if a letter is pending):**
> 7. Open mailbox letter
> 8. **Behaviour unchanged** from before — cursor on forward arrow, A turns page

The controller (not the implementer) handles the deploy + log pull.
