# Title/Main Menu Cursor Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the cursor visible on the Load/New button when the Android title screen loads under controller control, instead of staying invisible until the player's first stick input.

**Architecture:** A single Harmony postfix on `TitleMenu.update(GameTime)`. On Android, `Game1.options.snappyMenus` is `false`, so the game never runs its own title-menu cursor snap and `mouseCursorTransparency` stays at `0` until first stick input. The postfix supplies the missing state every frame under gamepad control: it calls the game's own `snapToDefaultClickableComponent()` once (when nothing is snapped) and forces `Game1.mouseCursorTransparency = 1f`. Pure "fix the data" — no input interception, no custom cursor rendering.

**Tech Stack:** C# / .NET 6, SMAPI 4.x mod, HarmonyLib for runtime patching. Built with `dotnet build`. Verified by on-device testing (no unit-test suite — this project verifies patches by building + device test, per `.claude/CLAUDE.md`).

**Spec:** `docs/superpowers/specs/2026-05-14-title-menu-cursor-design.md`

---

## File Structure

| File | Responsibility | Change |
|------|----------------|--------|
| `Patches/TitleMenuPatches.cs` | Owns the `TitleMenu` initial-cursor-state patch. Self-contained — one `Apply` method, one postfix. | Create |
| `ModEntry.cs` | Registers all Harmony patch classes during mod startup. | Modify (one line added) |
| `manifest.json` | Mod metadata, including version. | Modify (version bump) |

The `.csproj` uses SDK-style glob includes, so the new `Patches/*.cs` file is picked up automatically — no `.csproj` edit needed.

This is one feature = one `0.0.1` patch (`v3.7.1` → `v3.7.2`). All three files are committed together in a single commit (Task 4), per the project's "one patch = one change, commit every change" rule.

All fields and methods the patch touches are **public** — no reflection needed:

| Member | Type | Declared on |
|--------|------|-------------|
| `TitleMenu.titleInPosition` | `public bool` | `TitleMenu` |
| `TitleMenu.buttonsToShow` | `public int` | `TitleMenu` |
| `TitleMenu.numberOfButtons` | `public static int` | `TitleMenu` |
| `TitleMenu.subMenu` | `public static IClickableMenu` | `TitleMenu` |
| `currentlySnappedComponent` | `public ClickableComponent` | `IClickableMenu` (inherited) |
| `snapToDefaultClickableComponent()` | `public override void` | `TitleMenu` |
| `Game1.mouseCursorTransparency` | `public static float` | `Game1` |

---

### Task 1: Create the TitleMenu patch class

**Files:**
- Create: `Patches/TitleMenuPatches.cs`

- [ ] **Step 1: Create `Patches/TitleMenuPatches.cs` with the full patch class**

Write this exact file content:

```csharp
using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Harmony patch for the title/main menu cursor initial state.
    ///
    /// On Android, Game1.options.snappyMenus is false, so TitleMenu never runs
    /// its own snapToDefaultClickableComponent() at setup — the cursor is never
    /// positioned on the Load/New button. Separately, mouseCursorTransparency
    /// stays at 0 until the player's first stick input, so even a positioned
    /// cursor is drawn at 0% opacity. Net effect: no visible cursor on the title
    /// screen until the player moves the stick.
    ///
    /// Fix: a postfix on TitleMenu.update() supplies the missing state every
    /// frame under gamepad control. It calls the game's own
    /// snapToDefaultClickableComponent() once (when nothing is snapped) to
    /// position the cursor on Load (or New for a fresh save), and forces
    /// mouseCursorTransparency = 1f so the game's own drawMouse renders it.
    /// Gated on Game1.options.gamepadControls && !Game1.lastCursorMotionWasMouse
    /// so pure-touch users keep vanilla behavior (no title-screen cursor).
    /// </summary>
    internal static class TitleMenuPatches
    {
        private static IMonitor Monitor;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                var update = AccessTools.Method(typeof(TitleMenu), nameof(TitleMenu.update), new[] { typeof(GameTime) });
                if (update != null)
                {
                    harmony.Patch(
                        original: update,
                        postfix: new HarmonyMethod(typeof(TitleMenuPatches), nameof(Update_Postfix))
                    );
                    Monitor.Log("TitleMenu patches applied.", LogLevel.Trace);
                }
                else
                {
                    Monitor.Log("TitleMenuPatches: 'update' method not found — patch skipped.", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply TitleMenu patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Postfix on TitleMenu.update — under gamepad control, once the title
        /// intro has settled and the main button row is interactive, ensure the
        /// cursor is snapped to a default button and visible. Runs every frame so
        /// that if Game1 resets mouseCursorTransparency, it is re-set the same
        /// frame; the snap itself fires only once (guarded on
        /// currentlySnappedComponent == null) so it never fights the player's
        /// own navigation.
        /// </summary>
        private static void Update_Postfix(TitleMenu __instance)
        {
            try
            {
                if (!Game1.options.gamepadControls || Game1.lastCursorMotionWasMouse)
                    return;

                // Only act once the intro animation has settled and the main
                // button row is fully shown. Sub-menus (Load Game, Co-op, About)
                // are out of scope — they manage their own navigation.
                if (!__instance.titleInPosition)
                    return;
                if (__instance.buttonsToShow < TitleMenu.numberOfButtons)
                    return;
                if (TitleMenu.subMenu != null)
                    return;

                if (__instance.currentlySnappedComponent == null)
                    __instance.snapToDefaultClickableComponent();

                Game1.mouseCursorTransparency = 1f;
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[TitleMenu] Update_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
```

Notes for the engineer:
- `TitleMenu.update` is an override of `IClickableMenu.update(GameTime)`. `AccessTools.Method` with the explicit `new[] { typeof(GameTime) }` parameter array resolves the right overload.
- The postfix only declares `TitleMenu __instance` — Harmony lets a postfix omit parameters it doesn't use, so the `GameTime` argument is not declared.
- `TitleMenu.numberOfButtons` and `TitleMenu.subMenu` are **static** members — access them as `TitleMenu.numberOfButtons` / `TitleMenu.subMenu`, not via `__instance`.
- All members accessed are public (see the table in the File Structure section) — confirmed against the decompiled Android source at `C:\Users\Jeff\Documents\Projects\decompiler\stardew-valley-android\decompiled\StardewValley\StardewValley.Menus\TitleMenu.cs`. Direct access is correct; do not convert to reflection.
- The `Apply(Harmony, IMonitor)` signature, the `try/catch` around patch application, the `AccessTools.Method` + null-check + `Monitor.Log` trace line, and the `[TitleMenu] ...` error-log prefix in the postfix all match the existing pattern in `Patches/OptionsPagePatches.cs` and `Patches/DialogueBoxPatches.cs`.

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
- Modify: `ModEntry.cs` (the Harmony patch registration block, around lines 148–166)

- [ ] **Step 1: Add the registration line**

In `ModEntry.cs`, find this block (the `DialogueBoxPatches` line was added by the preceding #22b patch):

```csharp
            Patches.OptionsPagePatches.Apply(harmony, this.Monitor);
            Patches.DialogueBoxPatches.Apply(harmony, this.Monitor);
            Patches.BootDiagnosticPatches.Apply(harmony, this.Monitor);
```

Insert the `TitleMenuPatches` line immediately after the `DialogueBoxPatches` line:

```csharp
            Patches.OptionsPagePatches.Apply(harmony, this.Monitor);
            Patches.DialogueBoxPatches.Apply(harmony, this.Monitor);
            Patches.TitleMenuPatches.Apply(harmony, this.Monitor);
            Patches.BootDiagnosticPatches.Apply(harmony, this.Monitor);
```

Change nothing else in `ModEntry.cs`.

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
    "Version": "3.7.1",
```
to:
```json
    "Version": "3.7.2",
```
Change nothing else in `manifest.json`.

- [ ] **Step 2: Build the release artifact**

Run:
```bash
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"
dotnet build AndroidConsolizer.csproj -c Release
```
Expected: `Build succeeded.` with 0 errors, and the output ZIP `bin/Release/net6.0/AndroidConsolizer 3.7.2.zip` is produced (the new `3.7.2` filename confirms the version bump took effect). Verify with `ls "bin/Release/net6.0/"`.

---

### Task 4: Commit

**Files:**
- Commit: `Patches/TitleMenuPatches.cs`, `ModEntry.cs`, `manifest.json`

- [ ] **Step 1: Stage the three changed files by name**

Run:
```bash
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"
git add Patches/TitleMenuPatches.cs ModEntry.cs manifest.json
```

Do **not** use `git add .` or `git add -A` — the working tree has unrelated untracked files and a pre-existing `.csproj` edit that must stay out of this commit.

- [ ] **Step 2: Create the commit**

Run:
```bash
git commit -m "$(cat <<'EOF'
v3.7.2: Show title-menu cursor on Load button under controller (#17)

On Android, Game1.options.snappyMenus is false, so TitleMenu never runs
its own snapToDefaultClickableComponent() at setup — the cursor is never
positioned on the Load/New button. Separately, mouseCursorTransparency
stays at 0 until the player's first stick input, so even a positioned
cursor draws at 0% opacity. Net effect: no visible cursor on the title
screen until the player moves the stick.

Fix: new Patches/TitleMenuPatches.cs adds a Harmony postfix on
TitleMenu.update() that, under gamepad control and once the title intro
has settled, calls the game's own snapToDefaultClickableComponent() (when
nothing is snapped yet) and forces Game1.mouseCursorTransparency = 1f.
Runs every frame so a Game1 transparency reset is corrected the same
frame; the snap fires only once so it never fights player navigation.
Gated on Game1.options.gamepadControls && !Game1.lastCursorMotionWasMouse
so pure-touch users keep vanilla behavior. Sub-menus out of scope. No
GMCM toggle.

Files: Patches/TitleMenuPatches.cs (new), ModEntry.cs (register patch),
manifest.json (3.7.1 -> 3.7.2).

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
Expected: the latest commit is `v3.7.2: Show title-menu cursor on Load button under controller (#17)`, and `git status` still shows the unrelated untracked files / pre-existing `.csproj` modification (those are expected and must remain — do NOT commit them).

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

- [ ] **Step 2: Test the existing-save case**

Cold-launch the game to the title screen with a controller connected and an existing save present. Once the title intro animation settles, confirm the cursor is **visible on the Load button** with no stick input.

- [ ] **Step 3: Test navigation is unchanged**

From the title screen, navigate the main button row (New / Load / Co-op / Exit) and the corner About / Language buttons with the stick, and press A to select. Confirm navigation and selection still work exactly as before.

- [ ] **Step 4: Test touch is unaffected**

Tap the touchscreen on the title screen and confirm the cursor hides (vanilla touch behavior), and touch selection of the title buttons still works.

- [ ] **Step 5: Test the intro-animation timing**

Cold-launch again and watch the title intro: confirm the cursor does **not** appear during the logo swipe / viewport rise — only after the title settles and the buttons are fully shown.

- [ ] **Step 6: Pull logs and check for errors**

Run:
```bash
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\SyncdewValley"
.\sync.ps1 logs
```
Then read `AndroidConsolizer/test-output/SMAPI-latest.txt`. Expected: `TitleMenu patches applied.` appears at trace level during startup, and there are **no** `[TitleMenu] Update_Postfix error` lines and no Harmony patch-application errors.

- [ ] **Step 7: Mark the item done**

Once the user confirms all device tests pass:
- Move #17 from `TODO.md` (v3.8.0 section) to `DONE.md` with implementation notes (root cause: `snappyMenus`-gated snap + `mouseCursorTransparency` trap; the `update` postfix fix; the gamepad gate).
- Update `STATUS.md` (#17 complete, current local version v3.7.2).
- Commit the doc update as a separate `docs:` commit (documentation update, not a code change — does not get its own version bump).

---

## Self-Review

**1. Spec coverage:**
- "Postfix on `TitleMenu.update(GameTime)`" → Task 1. ✅
- "Gate: `Game1.options.gamepadControls && !Game1.lastCursorMotionWasMouse`" → Task 1, postfix step 1. ✅
- "Gate: `titleInPosition`, `buttonsToShow >= numberOfButtons`, `subMenu == null`" → Task 1, postfix. ✅
- "If `currentlySnappedComponent == null`, call `snapToDefaultClickableComponent()`" → Task 1, postfix. ✅
- "Set `Game1.mouseCursorTransparency = 1f`" → Task 1, postfix. ✅
- "No reflection — all members public" → Task 1 uses direct access; File Structure table documents it. ✅
- "New patch file `Patches/TitleMenuPatches.cs`" → Task 1. ✅
- "Register the patch in `ModEntry.cs`" → Task 2. ✅
- "Version bump to 3.7.2" → Task 3. ✅
- "No GMCM toggle" → no `ModConfig.cs` change anywhere in the plan. ✅
- "Testing" section (existing-save cursor on Load, navigation unchanged, touch unaffected, intro timing) → Task 5, steps 2–5. ✅
- Spec testing step 2 also mentions a fresh-save (New button) check "if testable" — covered implicitly by Task 5 step 2's reliance on the game's own `snapToDefaultClickableComponent()` choosing Load vs New; not given its own step because a fresh-save state is not reliably reproducible on the test device. No gap.

**2. Placeholder scan:** No "TBD"/"TODO"/"implement later" in any step. Every code step shows complete code. Every command step shows the exact command and expected output. ✅

**3. Type consistency:** `TitleMenuPatches`, `Apply`, `Update_Postfix`, `__instance.titleInPosition`, `__instance.buttonsToShow`, `__instance.currentlySnappedComponent`, `TitleMenu.numberOfButtons`, `TitleMenu.subMenu`, `Game1.mouseCursorTransparency`, `snapToDefaultClickableComponent()` are used consistently across Task 1 (definition), Task 2 (registration call `Patches.TitleMenuPatches.Apply`), and Task 4 (commit file list). The game method name `update` matches the decompiled source. ✅
