# Tool Charging While Moving (#25) — Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make holding an upgraded Hoe / Watering Can while walking **charge** the area effect (like console) instead of rapid-firing single uses and locking movement.

**Architecture:** A new `Patches/ToolUsePatches.cs` enforces "one tool-use *begin* per physical hold of the use-tool button" — the first begin of a hold passes through; every later-tick re-fire is suppressed (prefixes on `Farmer.FireTool` and `Game1.pressUseToolButton` that skip the original). That removes the Android held-button auto-repeat's spurious `FireTool` (rapid uses) and spurious `pressUseToolButton` (which re-zeroes the charge), letting the engine's own charge ramp accumulate `toolPower`. The session is closed when the use-tool button is released, detected at the existing `GamePad.GetState` postfix. Staged: Stage 1 relies on the vanilla ramp; Stage 2 (only if the device test proves it necessary) actively drives the charge.

**Tech Stack:** C# / SMAPI / HarmonyLib, `dotnet build` against the PC DLL, deployed to Android (G Cloud). No unit-test harness exists for Harmony patches against the Android runtime — **verification is "build succeeds" + a device playtest** (per `.claude/CLAUDE.md`).

**Spec:** `docs/superpowers/specs/2026-06-04-tool-charging-while-moving-phase2-design.md`

**Conventions (from `.claude/CLAUDE.md`):**
- One change per commit. Bump `manifest.json` **before** building. PATCH `0.0.1` per change.
- `git add <specific files>` — never `git add .`.
- Build: `dotnet build AndroidConsolizer.csproj -c Release` → `bin/Release/net6.0/AndroidConsolizer X.X.X.zip`.
- Never push/publish without explicit user "yes."
- Deploy + log-pull are Claude's job: `cd ../SyncdewValley; pwsh -NoProfile -File sync.ps1 deploy|logs`.

**Current version:** 3.8.7.

---

## File Structure

| File | Responsibility | Tasks |
|------|----------------|-------|
| `Patches/ToolChargeDiagnosticPatches.cs` | Phase-1 throwaway diagnostic — **deleted** | 1 |
| `Patches/GameplayButtonPatches.cs` | Remove Phase-1 `Diag*` captures (T1); expose game-facing use-tool-held + fire the session-release notify (T3) | 1, 3 |
| `ModConfig.cs` | `EnableMoveWhileCharging` toggle (default on) | 2 |
| `ModEntry.cs` | Remove diag registration (T1); add GMCM entry (T2); register `ToolUsePatches` (T3) | 1, 2, 3 |
| `Patches/ToolUsePatches.cs` | **New** — hold-session state machine + `FireTool`/`pressUseToolButton` suppression (Stage 1); charge maintenance (Stage 2) | 3, 5 |

---

## Task 1: Remove the Phase-1 diagnostics

**Files:**
- Delete: `Patches/ToolChargeDiagnosticPatches.cs`
- Modify: `ModEntry.cs:212`
- Modify: `Patches/GameplayButtonPatches.cs` (remove `Diag*` field + assignments)
- Modify: `manifest.json` (version bump)

- [ ] **Step 1: Bump version to 3.8.8**

In `manifest.json`, change `"Version": "3.8.7"` to `"Version": "3.8.8"`.

- [ ] **Step 2: Delete the diagnostic patch file**

```bash
git rm "Patches/ToolChargeDiagnosticPatches.cs"
```

- [ ] **Step 3: Remove its registration in `ModEntry.cs`**

Delete this line (currently `ModEntry.cs:212`):

```csharp
            Patches.ToolChargeDiagnosticPatches.Apply(harmony, this.Monitor);
```

- [ ] **Step 4: Remove the `Diag*` field declaration in `GameplayButtonPatches.cs`**

Replace:

```csharp
        // v3.8.7 #25 diagnostic: raw (pre-swap/suppress) and final (game-facing) tool face buttons.
        internal static bool DiagRawToolX, DiagRawToolY, DiagFinalToolX, DiagFinalToolY;

```

with nothing (delete those two lines and the blank line after).

- [ ] **Step 5: Remove the raw-capture assignment in `GetState_Postfix`**

Replace:

```csharp
                // v3.8.7 #25 diagnostic: raw tool face buttons BEFORE any swap/suppression.
                DiagRawToolX = __result.IsButtonDown(Buttons.X);
                DiagRawToolY = __result.IsButtonDown(Buttons.Y);

```

with nothing (delete those three lines and the trailing blank line).

- [ ] **Step 6: Remove the three final-capture assignment pairs**

There are three identical pairs in the three return paths. Remove all occurrences of:

```csharp
                    DiagFinalToolX = __result.IsButtonDown(Buttons.X);
                    DiagFinalToolY = __result.IsButtonDown(Buttons.Y);
```

(Use a replace-all on those two lines → empty. Each sits between `_cachedState = __result;` and `_cachedRawRightStickY = RawRightStickY;`.)

- [ ] **Step 7: Build**

Run: `dotnet build AndroidConsolizer.csproj -c Release`
Expected: `Build succeeded`, `0 Error(s)`, output `bin/Release/net6.0/AndroidConsolizer 3.8.8.zip`. (No reference to `Diag*` or `ToolChargeDiagnosticPatches` should remain — a leftover reference would fail the build with CS0103/CS0246.)

- [ ] **Step 8: Commit**

```bash
git add manifest.json ModEntry.cs Patches/GameplayButtonPatches.cs Patches/ToolChargeDiagnosticPatches.cs
git commit -m "v3.8.8: Remove Phase-1 tool-charge diagnostics (#25)

Deletes ToolChargeDiagnosticPatches.cs and its registration, and the
DiagRawToolX/Y + DiagFinalToolX/Y captures in GameplayButtonPatches.
Root cause is confirmed; the throwaway instrumentation is no longer
needed. RawLeftStickX/Y are kept (still used elsewhere)."
```

---

## Task 2: Add the `EnableMoveWhileCharging` config toggle + GMCM entry

**Files:**
- Modify: `ModConfig.cs` (new property)
- Modify: `ModEntry.cs` (GMCM `AddBoolOption`)
- Modify: `manifest.json` (version bump)

- [ ] **Step 1: Bump version to 3.8.9**

In `manifest.json`, change `"Version": "3.8.8"` to `"Version": "3.8.9"`.

- [ ] **Step 2: Add the config property**

In `ModConfig.cs`, in the `** Standalone Features` region (after the `EnableGameMenuNavigation` property, before `FreeCursorOnSettings`), add:

```csharp
        /// <summary>
        /// #25: Holding an upgraded (Copper+) Hoe or Watering Can while moving charges the
        /// area effect (console parity) instead of rapid-firing single uses and locking
        /// movement. The character slides freely during the charge; release fires the charged
        /// area. A quick tap still performs one normal single use. Basic (level-0) tools and
        /// Pickaxe/Axe are unaffected. Disable to restore vanilla Android behavior.
        /// </summary>
        public bool EnableMoveWhileCharging { get; set; } = true;
```

- [ ] **Step 3: Add the GMCM toggle**

In `ModEntry.cs`, immediately after the `EnableGameMenuNavigation` `AddBoolOption` block (the one whose `name` is `"Console Menus"`, ending at the `);` near line 1339), insert:

```csharp

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Charge Tools While Moving",
                tooltip: () => "Hold an upgraded (Copper+) Hoe or Watering Can while walking to charge its area effect (like console), instead of rapid single uses. Release fires the charged area; a quick tap is still one use. Basic tools, Pickaxe and Axe are unaffected.",
                getValue: () => Config.EnableMoveWhileCharging,
                setValue: value => Config.EnableMoveWhileCharging = value
            );
```

- [ ] **Step 4: Build**

Run: `dotnet build AndroidConsolizer.csproj -c Release`
Expected: `Build succeeded`, `0 Error(s)`, output `bin/Release/net6.0/AndroidConsolizer 3.8.9.zip`.

- [ ] **Step 5: Commit**

```bash
git add manifest.json ModConfig.cs ModEntry.cs
git commit -m "v3.8.9: Add EnableMoveWhileCharging config + GMCM toggle (#25)

New toggle (default on) that will gate the move-while-charging fix.
No behavior change yet — wiring only."
```

---

## Task 3: Stage 1 — hold-session suppression of the re-fire

**Files:**
- Create: `Patches/ToolUsePatches.cs`
- Modify: `ModEntry.cs` (register the patch set)
- Modify: `Patches/GameplayButtonPatches.cs` (expose game-facing use-tool-held; notify release)
- Modify: `manifest.json` (version bump)

**Design recap:** `FireTool` and `pressUseToolButton` are each called only from the press block (`Game1.cs:13888` / `13890`). On the **first** begin of a hold we record the tick and allow both calls; on any **later** tick while the hold is active we suppress them (skip the original). The hold is closed when the game-facing use-tool button (final `Buttons.X` after the X/Y swap) is released, detected once per tick in `GetState_Postfix`.

- [ ] **Step 1: Bump version to 3.8.10**

In `manifest.json`, change `"Version": "3.8.9"` to `"Version": "3.8.10"`.

- [ ] **Step 2: Create `Patches/ToolUsePatches.cs`**

```csharp
using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// #25 — Tool charging while moving (Stage 1).
    ///
    /// Android's held-button auto-repeat (Game1.UpdateControlInput, decompile line ~13640)
    /// forces useToolButtonPressed=true every frame a non-melee tool button is held. The
    /// press block (decompile ~13884, gated by !UsingTool) therefore re-fires the tool each
    /// time a use animation completes, and pressUseToolButton() re-zeroes toolPower/toolHold
    /// (decompile 12269-12270) on every fire — so an upgraded Hoe/Watering Can never charges
    /// while moving (it charges fine stationary, where the use settles into a static hold).
    ///
    /// Stage 1 fix: allow exactly ONE tool-use begin per physical hold of the use-tool
    /// button, and suppress every later-tick re-fire (skip Farmer.FireTool and
    /// Game1.pressUseToolButton). With the resets gone, the engine's own charge ramp
    /// (decompile ~13914) keeps its accumulated toolPower and fires the charged area on
    /// release. Scoped to upgraded (UpgradeLevel >= 1) Hoe/Watering Can and gated behind
    /// EnableMoveWhileCharging.
    ///
    /// Targets resolved via AccessTools (Android-vs-PC reflection safety); every patch body
    /// is wrapped so a fault falls through to vanilla and can never break tool use.
    /// </summary>
    internal static class ToolUsePatches
    {
        private static IMonitor Monitor;

        /// <summary>True while a use-tool-button hold session is open (between the first begin
        /// of a hold and the button's release).</summary>
        private static bool _holdActive;

        /// <summary>The tick on which this hold's single legitimate begin was allowed. Calls on
        /// this tick pass through; calls on later ticks are spurious re-fires and are suppressed.</summary>
        private static int _allowTick = -1;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                var fireTool = AccessTools.Method(typeof(Farmer), "FireTool");
                if (fireTool != null)
                    harmony.Patch(fireTool,
                        prefix: new HarmonyMethod(typeof(ToolUsePatches), nameof(FireTool_Prefix)));
                else
                    Monitor.Log("[MoveCharge] Farmer.FireTool not found — suppression not attached.", LogLevel.Warn);

                var pressUse = AccessTools.Method(typeof(Game1), "pressUseToolButton");
                if (pressUse != null)
                    harmony.Patch(pressUse,
                        prefix: new HarmonyMethod(typeof(ToolUsePatches), nameof(PressUseToolButton_Prefix)));
                else
                    Monitor.Log("[MoveCharge] Game1.pressUseToolButton not found — suppression not attached.", LogLevel.Warn);

                Monitor.Log("Tool-use (move-while-charging) patches applied.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply tool-use patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Upgraded Hoe / Watering Can — the only tools v1 charges while moving.</summary>
        private static bool IsQualifyingTool()
        {
            var tool = Game1.player?.CurrentTool;
            if (tool == null) return false;
            if (!(tool is Hoe || tool is WateringCan)) return false;
            return tool.UpgradeLevel >= 1;
        }

        /// <summary>Decide whether the current FireTool/pressUseToolButton call is a spurious
        /// held-button re-fire that should be suppressed. Opens the hold session on the first
        /// begin (allowing it through). Returns true only for later-tick re-fires.</summary>
        private static bool ShouldSuppressRefire()
        {
            var cfg = ModEntry.Config;
            if (cfg == null || !cfg.EnableMoveWhileCharging) return false;
            if (!IsQualifyingTool()) return false;

            if (!_holdActive)
            {
                // First begin of this hold — allow it and arm the session.
                _holdActive = true;
                _allowTick = Game1.ticks;
                return false;
            }

            // The paired FireTool + pressUseToolButton on the begin tick both pass.
            if (Game1.ticks == _allowTick) return false;

            // A later tick while still holding → spurious re-fire.
            return true;
        }

        /// <summary>Called from GameplayButtonPatches when the game-facing use-tool button is
        /// released, closing the hold session so the next press starts fresh.</summary>
        internal static void OnUseToolReleased()
        {
            _holdActive = false;
            _allowTick = -1;
        }

        /// <summary>Prefix on Farmer.FireTool — skip the spurious single-use swing on re-fires.</summary>
        private static bool FireTool_Prefix(Farmer __instance)
        {
            try
            {
                if (__instance != Game1.player) return true;
                return !ShouldSuppressRefire();
            }
            catch { return true; } // never break tool use
        }

        /// <summary>Prefix on Game1.pressUseToolButton — skip the spurious charge reset + re-begin
        /// on re-fires. The original returns bool; the press block only uses it to gate a no-op,
        /// so returning false is harmless.</summary>
        private static bool PressUseToolButton_Prefix(ref bool __result)
        {
            try
            {
                if (!ShouldSuppressRefire()) return true;
                __result = false;
                return false; // skip original
            }
            catch { return true; }
        }
    }
}
```

- [ ] **Step 3: Register the patch set in `ModEntry.cs`**

In `ModEntry.cs`, in the patch-apply block (where the other `Patches.*.Apply(harmony, this.Monitor);` lines are, around line 195-213), add after the `GameplayButtonPatches.Apply` line:

```csharp
            Patches.ToolUsePatches.Apply(harmony, this.Monitor);
```

- [ ] **Step 4: Expose the game-facing use-tool-held + release notify in `GameplayButtonPatches.cs`**

Add these fields next to the other raw-state fields (just below `internal static float RawLeftStickX; internal static float RawLeftStickY;`, around line 26):

```csharp

        /// <summary>True this tick when the GAME-facing use-tool button (final Buttons.X after
        /// the X/Y swap) is held. Read by ToolUsePatches' release detection (#25).</summary>
        internal static bool GameUseToolHeld;
        private static bool _prevGameUseToolHeld;
```

Add this helper method to the class (e.g. just above `GetState_OneParam_Prefix`):

```csharp
        /// <summary>Track the game-facing use-tool button (final Buttons.X) once per tick and,
        /// on its falling edge, close the move-while-charging hold session. Called from each
        /// GetState_Postfix exit after swaps/suppression are applied (#25).</summary>
        private static void FinalizeToolHeldTracking(GamePadState finalState)
        {
            GameUseToolHeld = finalState.IsButtonDown(Buttons.X);
            if (_prevGameUseToolHeld && !GameUseToolHeld)
                ToolUsePatches.OnUseToolReleased();
            _prevGameUseToolHeld = GameUseToolHeld;
        }
```

Then call it in all three `GetState_Postfix` return paths. Replace-all this anchor:

```csharp
                    _cachedState = __result;
```

with:

```csharp
                    _cachedState = __result;
                    FinalizeToolHeldTracking(__result);
```

(There are exactly three occurrences, one per return path; all at the same indentation.)

- [ ] **Step 5: Build**

Run: `dotnet build AndroidConsolizer.csproj -c Release`
Expected: `Build succeeded`, `0 Error(s)`, output `bin/Release/net6.0/AndroidConsolizer 3.8.10.zip`.

- [ ] **Step 6: Commit**

```bash
git add manifest.json Patches/ToolUsePatches.cs ModEntry.cs Patches/GameplayButtonPatches.cs
git commit -m "v3.8.10: Move-while-charging Stage 1 — suppress held-button re-fire (#25)

New ToolUsePatches enforces one tool-use begin per hold of the use-tool
button and suppresses later-tick re-fires (skip Farmer.FireTool and
Game1.pressUseToolButton), scoped to upgraded Hoe/Watering Can and gated
behind EnableMoveWhileCharging. Removing the spurious pressUseToolButton
resets lets the engine's own charge ramp accumulate. Session closes on the
game-facing use-tool button release, detected in GameplayButtonPatches'
GetState postfix. Stage 1 relies on the vanilla ramp; Stage 2 (active
charge drive) follows only if the device test shows the charge still
won't build while moving."
```

---

## Task 4: Device playtest checkpoint (Stage 1 verification)

**No code.** This is the meaningful playtest the spec calls for. Claude deploys and pulls; the user does the in-game test.

- [ ] **Step 1: Deploy v3.8.10 to the G Cloud**

```bash
cd ../SyncdewValley
pwsh -NoProfile -File sync.ps1 deploy
```

- [ ] **Step 2: Confirm `EnableMoveWhileCharging` and `VerboseLogging` are on, then ask the user to test**

Ask the user to, with an **upgraded** Watering Can (with water) and then an **upgraded** Hoe:
1. Stand still, hold the use-tool button briefly — confirm it still charges (no regression).
2. Hold the use-tool button **and walk** several tiles, then release.
3. Do a quick **tap** while walking — confirm exactly one tile is used.

- [ ] **Step 3: Pull and analyze the log**

```bash
cd ../SyncdewValley
pwsh -NoProfile -File sync.ps1 logs
```

Read `AndroidConsolizer/test-output/SMAPI-latest.txt`. Confirm against the user's report:
- **No** rapid single uses while holding + moving (the ~43-tick `FireTool` cadence is gone).
- On release, the charged area fires.
- Tap = one use; stationary charge unchanged.

- [ ] **Step 4: Decide**

- **If charging works while moving** → Stage 1 is sufficient. Move item #25 from `TODO.md` to `DONE.md`, record the result, and **stop** (skip Task 5).
- **If holding + moving now does nothing useful** (no rapid-fire, but the charge doesn't build / the use self-terminates and leaves the player idle) → proceed to **Task 5 (Stage 2)**.

---

## Task 5: Stage 2 — active charge maintenance (CONDITIONAL — only if Task 4 fails)

> **Do not start this task unless Task 4, Step 4 selected "proceed to Stage 2."** If Stage 1 worked, this task is skipped entirely.

**Why it may be needed:** suppressing the re-fire stops the churn, but if movement still drives the use to self-terminate (`UsingTool → false`, `canReleaseTool → false`), the engine's ramp condition (`useToolHeld && canReleaseTool`, decompile ~13914) goes false and no charge accumulates. Stage 2 keeps the charge alive ourselves while the hold is open and the tool is qualifying.

**Files:**
- Modify: `Patches/ToolUsePatches.cs` (add a per-tick charge driver)
- Modify: `ModEntry.cs` (call the driver from `OnUpdateTicked`)
- Modify: `manifest.json` (version bump to 3.8.11)

- [ ] **Step 1: Bump version to 3.8.11**

In `manifest.json`, change `"Version": "3.8.10"` to `"Version": "3.8.11"`.

- [ ] **Step 2: Add the charge driver to `ToolUsePatches.cs`**

Add these reflection handles and method to `ToolUsePatches` (the `toolPowerIncrease` method and the `toolPower`/`toolHold` net fields are reached via AccessTools for Android-vs-PC safety):

```csharp
        // Stage 2 — active charge maintenance. Resolved lazily on first use.
        private static System.Reflection.MethodInfo _toolPowerIncrease;
        private static bool _stage2Resolved;

        private static void ResolveStage2()
        {
            if (_stage2Resolved) return;
            _stage2Resolved = true;
            _toolPowerIncrease = AccessTools.Method(typeof(Farmer), "toolPowerIncrease");
            if (_toolPowerIncrease == null)
                Monitor?.Log("[MoveCharge] Stage 2: Farmer.toolPowerIncrease not found — charge drive disabled.", LogLevel.Warn);
        }

        // Drive cadence: advance the charge roughly every CHARGE_INTERVAL_TICKS while held,
        // capped at the tool's upgrade level (matches the engine's 0..upgradeLevel range).
        private const int CHARGE_INTERVAL_TICKS = 39; // ~650ms at 60fps, close to vanilla's hold schedule
        private static int _lastChargeTick = -1;

        /// <summary>Called once per tick from ModEntry.OnUpdateTicked. While a hold session is
        /// open over an upgraded Hoe/Watering Can and the engine has stopped charging on its
        /// own, keep the tool in a usable charge state and advance toolPower ourselves so the
        /// charged area grows. Release (vanilla EndUsingTool) still fires the charged area.</summary>
        internal static void MaintainChargeTick()
        {
            try
            {
                var cfg = ModEntry.Config;
                if (cfg == null || !cfg.EnableMoveWhileCharging) return;
                if (!_holdActive || !IsQualifyingTool()) return;

                var p = Game1.player;
                if (p == null) return;

                ResolveStage2();
                if (_toolPowerIncrease == null) return;

                // Only drive once the engine's own ramp is NOT already charging this tool
                // (avoid double-charging when Stage 1 alone is enough on a given tile).
                if (p.UsingTool && p.canReleaseTool) return;

                int upgrade = p.CurrentTool?.UpgradeLevel ?? 0;
                if (p.toolPower.Value >= upgrade) return; // already fully charged

                // Keep the tool in the charge state so EndUsingTool fires the area on release.
                p.UsingTool = true;
                p.canReleaseTool = true;

                if (Game1.ticks - _lastChargeTick >= CHARGE_INTERVAL_TICKS || _lastChargeTick < 0)
                {
                    _lastChargeTick = Game1.ticks;
                    _toolPowerIncrease.Invoke(p, null);
                }
            }
            catch { /* never break tool use */ }
        }
```

Also reset the driver clock when a hold closes — update `OnUseToolReleased`:

```csharp
        internal static void OnUseToolReleased()
        {
            _holdActive = false;
            _allowTick = -1;
            _lastChargeTick = -1;
        }
```

- [ ] **Step 3: Call the driver once per tick from `ModEntry.OnUpdateTicked`**

In `ModEntry.cs`, inside the `OnUpdateTicked` handler, add near the top (after any early `Context.IsWorldReady` / player-free guard already present in that method):

```csharp
            Patches.ToolUsePatches.MaintainChargeTick();
```

- [ ] **Step 4: Build**

Run: `dotnet build AndroidConsolizer.csproj -c Release`
Expected: `Build succeeded`, `0 Error(s)`, output `bin/Release/net6.0/AndroidConsolizer 3.8.11.zip`.

- [ ] **Step 5: Commit**

```bash
git add manifest.json Patches/ToolUsePatches.cs ModEntry.cs
git commit -m "v3.8.11: Move-while-charging Stage 2 — drive the charge while moving (#25)

When suppression alone leaves the use self-terminating mid-walk, keep the
upgraded Hoe/Watering Can in the charge state and advance toolPower on a
vanilla-like cadence while the hold is open. Release still fires the
charged area via vanilla EndUsingTool. Reflection (AccessTools) for
toolPowerIncrease + toolPower; gated behind EnableMoveWhileCharging and
the qualifying-tool check. Driven once per tick from OnUpdateTicked."
```

- [ ] **Step 6: Re-test on device**

Repeat Task 4 (deploy → user tests hold+walk for both tools → pull + analyze log). Tune `CHARGE_INTERVAL_TICKS` if the charge feels too fast/slow versus stationary. When confirmed, move #25 from `TODO.md` to `DONE.md`.

---

## Notes for the implementer

- **Tap-vs-hold is preserved structurally:** a tap is one begin (allowed) followed by a release that closes the session, so the next tap opens a fresh session. Suppression only ever fires on a *later tick* of a still-open hold.
- **Basic tools / Pickaxe / Axe are never touched:** `IsQualifyingTool()` returns false for them, so `ShouldSuppressRefire()` returns false and the original runs unmodified.
- **`FireTool` and `pressUseToolButton` are each called only from the press block** (verified in the decompile), so the prefixes can't be triggered by an unrelated code path.
- **Do not** remove `RawLeftStickX`/`RawLeftStickY` from `GameplayButtonPatches` — they're used by other patches.
- If a build error mentions `toolPower`/`toolHold` being a `NetInt` rather than `int`, use `.Value` (already used above as `p.toolPower.Value`).
