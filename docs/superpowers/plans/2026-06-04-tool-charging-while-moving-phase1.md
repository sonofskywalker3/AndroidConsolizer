# Tool Charging While Moving — Phase 1 (Diagnostic) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a Verbose-Logging-gated diagnostic build (v3.8.6) that records the Android tool charge-state machine while the player holds a Hoe / Watering Can and walks, so Phase 2's fix is designed from real device data rather than a guess.

**Architecture:** One new Harmony patch class `Patches/ToolChargeDiagnosticPatches.cs` that prefixes `Game1.pressUseToolButton`, `Farmer.FireTool`, `Farmer.toolPowerIncrease`, and postfixes `Farmer.canStrafeForToolUse`. Every patch logs a single `[ToolCharge]` line per call, gated so it only fires while a Hoe/Watering Can is equipped AND `Config.VerboseLogging` is on. All targets resolved by string via `AccessTools` with null-checks (Android-vs-PC reflection safety); all bodies wrapped in try/catch so a diagnostic fault can never break tool use.

**Tech Stack:** C# / .NET 6, HarmonyLib, SMAPI 4.x (Android), `dotnet build`. No unit-test harness for game patches — verification is build success + on-device SMAPI log inspection (deploy + log-pull via `../SyncdewValley/sync.ps1`, done by Claude).

**Reference:** Design spec `docs/superpowers/specs/2026-06-04-tool-charging-while-moving-design.md`.

---

## Verification model (read first)

These are Harmony patches against the Android game runtime; there is no local test
runner that can exercise them. So each code task is verified by:
1. **Build success** — `dotnet build AndroidConsolizer.csproj -c Release` reports `0 Error(s)`.
2. **On-device log** — after deploy, the pulled SMAPI log contains the expected
   `[ToolCharge]` lines (Task 5).

Do **not** add an MSTest/xUnit project for these patches — the existing
`AndroidControllerFix.Tests/` project does not cover Harmony game patches and is not
part of this work.

---

## File structure

- **Create:** `Patches/ToolChargeDiagnosticPatches.cs` — the entire diagnostic patch
  set (Apply + 4 patch bodies + 2 gate helpers). Single responsibility: capture
  charge-state telemetry. Throwaway — removed/demoted in Phase 2.
- **Modify:** `ModEntry.cs` — one registration line in the patch-apply list.
- **Modify:** `manifest.json` — version bump `3.8.5 → 3.8.6`.

---

## Task 1: Create the diagnostic patch class

**Files:**
- Create: `Patches/ToolChargeDiagnosticPatches.cs`

- [ ] **Step 1: Create the file with the full diagnostic patch set**

Create `Patches/ToolChargeDiagnosticPatches.cs` with exactly this content:

```csharp
using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// PHASE 1 DIAGNOSTIC (#25 — tool charging while moving). Logs the Android tool
    /// charge-state machine while the player holds a Hoe / Watering Can, to confirm
    /// whether Android delivers the tool button as repeated press-edges (re-firing +
    /// re-zeroing the charge each frame) or a sustained hold. Gated behind
    /// Config.VerboseLogging AND a Hoe/WateringCan being equipped, so it never emits
    /// in a normal shipped session. THROWAWAY: removed/demoted in Phase 2.
    ///
    /// Targets resolved by string via AccessTools (Android-vs-PC reflection safety);
    /// every body wrapped so a diagnostic fault cannot break tool use.
    /// </summary>
    internal static class ToolChargeDiagnosticPatches
    {
        private static IMonitor Monitor;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                var pressUse = AccessTools.Method(typeof(Game1), "pressUseToolButton");
                if (pressUse != null)
                    harmony.Patch(pressUse,
                        prefix: new HarmonyMethod(typeof(ToolChargeDiagnosticPatches), nameof(PressUseToolButton_Prefix)));
                else
                    Monitor.Log("[ToolCharge] Game1.pressUseToolButton not found — diag not attached for it.", LogLevel.Warn);

                var fireTool = AccessTools.Method(typeof(Farmer), "FireTool");
                if (fireTool != null)
                    harmony.Patch(fireTool,
                        prefix: new HarmonyMethod(typeof(ToolChargeDiagnosticPatches), nameof(FireTool_Prefix)));
                else
                    Monitor.Log("[ToolCharge] Farmer.FireTool not found — diag not attached for it.", LogLevel.Warn);

                var powerInc = AccessTools.Method(typeof(Farmer), "toolPowerIncrease");
                if (powerInc != null)
                    harmony.Patch(powerInc,
                        prefix: new HarmonyMethod(typeof(ToolChargeDiagnosticPatches), nameof(ToolPowerIncrease_Prefix)));
                else
                    Monitor.Log("[ToolCharge] Farmer.toolPowerIncrease not found — diag not attached for it.", LogLevel.Warn);

                var canStrafe = AccessTools.Method(typeof(Farmer), "canStrafeForToolUse");
                if (canStrafe != null)
                    harmony.Patch(canStrafe,
                        postfix: new HarmonyMethod(typeof(ToolChargeDiagnosticPatches), nameof(CanStrafeForToolUse_Postfix)));
                else
                    Monitor.Log("[ToolCharge] Farmer.canStrafeForToolUse not found — diag not attached for it.", LogLevel.Warn);

                Monitor.Log("Tool charge diagnostic patches applied.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply tool charge diagnostic patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>True only when a Hoe/WateringCan is equipped and verbose logging is on.</summary>
        private static bool ShouldLog()
        {
            if (!(ModEntry.Config?.VerboseLogging ?? false)) return false;
            var tool = Game1.player?.CurrentTool;
            return tool is Hoe || tool is WateringCan;
        }

        /// <summary>Snapshot of the charge/movement fields shared by every log line.</summary>
        private static string State()
        {
            var p = Game1.player;
            if (p == null) return "player=null";
            string tool = p.CurrentTool?.Name ?? p.CurrentTool?.GetType().Name ?? "none";
            return $"tick={Game1.ticks} tool={tool} power={p.toolPower.Value} hold={p.toolHold.Value} "
                 + $"using={p.UsingTool} canMove={p.CanMove} moveDirs={p.movementDirections.Count}";
        }

        private static void PressUseToolButton_Prefix()
        {
            try
            {
                if (!ShouldLog()) return;
                Monitor?.Log($"[ToolCharge] pressUseToolButton (pre-reset) {State()}", LogLevel.Debug);
            }
            catch { /* diagnostic must never break tool use */ }
        }

        private static void FireTool_Prefix(Farmer __instance)
        {
            try
            {
                if (__instance != Game1.player || !ShouldLog()) return;
                Monitor?.Log($"[ToolCharge] FireTool {State()}", LogLevel.Debug);
            }
            catch { }
        }

        private static void ToolPowerIncrease_Prefix(Farmer __instance)
        {
            try
            {
                if (__instance != Game1.player || !ShouldLog()) return;
                Monitor?.Log($"[ToolCharge] toolPowerIncrease {State()}", LogLevel.Debug);
            }
            catch { }
        }

        private static void CanStrafeForToolUse_Postfix(Farmer __instance, bool __result)
        {
            try
            {
                if (__instance != Game1.player || !ShouldLog()) return;
                Monitor?.Log($"[ToolCharge] canStrafeForToolUse -> {__result} {State()}", LogLevel.Debug);
            }
            catch { }
        }
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"; dotnet build AndroidConsolizer.csproj -c Release`
Expected: `Build succeeded.` with `0 Error(s)`. (The new file is not yet registered, so it has no runtime effect yet — this step only proves it compiles.)

If a member doesn't resolve at compile time (e.g. `movementDirections`, `toolPower`, `UsingTool`, `CanMove` differ on the PC DLL), STOP — do not guess. Re-check the field/property name against the PC `StardewValley.dll` the project references; if it's an Android-only member, read it via `AccessTools.Field`/reflection instead of direct access, mirroring `FarmerPatches.cs`.

---

## Task 2: Register the diagnostic patch set

**Files:**
- Modify: `ModEntry.cs` (the patch-apply list, ends at the `BootDiagnosticPatches.Apply` line ~211)

- [ ] **Step 1: Add the registration line**

In `ModEntry.cs`, find:

```csharp
            Patches.BootDiagnosticPatches.Apply(harmony, this.Monitor);
```

Add immediately after it:

```csharp
            Patches.ToolChargeDiagnosticPatches.Apply(harmony, this.Monitor);
```

- [ ] **Step 2: Build**

Run: `cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"; dotnet build AndroidConsolizer.csproj -c Release`
Expected: `Build succeeded.` with `0 Error(s)`.

---

## Task 3: Version bump to 3.8.6 and build the release zip

**Files:**
- Modify: `manifest.json`

- [ ] **Step 1: Bump the version**

In `manifest.json`, change:

```json
    "Version": "3.8.5",
```

to:

```json
    "Version": "3.8.6",
```

- [ ] **Step 2: Build the release zip**

Run: `cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"; dotnet build AndroidConsolizer.csproj -c Release`
Expected: `Build succeeded.`, `0 Error(s)`, and a generated `bin/Release/net6.0/AndroidConsolizer 3.8.6.zip`.

---

## Task 4: Commit

**Files:**
- `Patches/ToolChargeDiagnosticPatches.cs`, `ModEntry.cs`, `manifest.json`

- [ ] **Step 1: Stage only the changed files and commit**

Run:

```powershell
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"
git add Patches/ToolChargeDiagnosticPatches.cs ModEntry.cs manifest.json
git commit -m @'
v3.8.6: Tool charge diagnostic (#25 Phase 1)

Adds Patches/ToolChargeDiagnosticPatches.cs instrumenting Game1.pressUseToolButton,
Farmer.FireTool, Farmer.toolPowerIncrease, and Farmer.canStrafeForToolUse. Logs one
[ToolCharge] line per call while a Hoe/Watering Can is equipped, gated behind
Config.VerboseLogging. Captures whether Android sends repeated tool-button press-edges
vs a sustained hold (and the canStrafeForToolUse movement-gate decisions) so the
Phase-2 fix is designed from device data.

Targets resolved via AccessTools with null-checks; all bodies wrapped in try/catch.
Registered in ModEntry. Throwaway — removed/demoted in Phase 2.

Files: Patches/ToolChargeDiagnosticPatches.cs (new), ModEntry.cs (registration),
manifest.json (3.8.5 -> 3.8.6).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

Expected: commit succeeds, 3 files changed.

---

## Task 5: Deploy + device capture (Claude-driven; user does the in-game test)

**Files:** none (device operation)

- [ ] **Step 1: Ensure Verbose Logging is enabled on the device**

The diagnostic is gated behind `Config.VerboseLogging`. Before deploying, confirm
the device config has it on. Inspect the synced config
(`../SyncdewValley/sync/configs/...` or pull configs) and ensure
`"VerboseLogging": true`. If it's false, enable it (edit the config and push, or
toggle it via GMCM on-device before the test). Do not skip — with it off, the
diagnostic emits nothing.

- [ ] **Step 2: Deploy v3.8.6 to the G Cloud**

Run: `cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\SyncdewValley"; pwsh -NoProfile -File sync.ps1 deploy`
Expected: DLL + manifest pushed, game relaunched on the G Cloud.

- [ ] **Step 2.5: Confirm the deployed version loaded**

After the game boots, the SMAPI log should show Android Consolizer **3.8.6** loaded
and a `Tool charge diagnostic patches applied.` trace. (You'll see this when you pull
the log in Step 4.)

- [ ] **Step 3: User performs the hold-and-walk test**

Ask the user to, on the G Cloud, in-game:
1. Equip the **Hoe**, hold the tool button, and walk in a few directions for ~5 seconds.
2. Equip the **Watering Can**, hold the tool button, and walk for ~5 seconds.
3. Also do one quick **tap** of each (no hold) for a single-use baseline.
Then tell Claude they're done. (This is the only step the user performs.)

- [ ] **Step 4: Pull and archive the log**

Run: `cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\SyncdewValley"; pwsh -NoProfile -File sync.ps1 logs`
Expected: `AndroidConsolizer/test-output/SMAPI-latest.txt` updated; previous log auto-archived.

---

## Task 6: Analyze the capture → Phase 2 checkpoint

**Files:** none (analysis; feeds the Phase 2 spec)

- [ ] **Step 1: Extract the `[ToolCharge]` lines**

Read `AndroidConsolizer/test-output/SMAPI-latest.txt` and isolate the `[ToolCharge]`
lines for the hold-and-walk window. Use Grep on the log file for `\[ToolCharge\]`.

- [ ] **Step 2: Answer the diagnostic questions**

From the captured lines, determine and write down:
1. **Press cadence** — how many ticks between consecutive `pressUseToolButton` lines
   during a single sustained hold? (Many closely-spaced calls ⇒ repeated press-edges,
   confirming the rapid-fire/charge-reset hypothesis.)
2. **Did `toolPowerIncrease` ever fire** during a hold? (If never ⇒ charge ramp never
   ran.)
3. **`FireTool` cadence** — how often the tool actually fired during the hold.
4. **`canStrafeForToolUse` returns** — what it returned during the hold, and the
   `power`/`hold`/`moveDirs` snapshot when movement was blocked.

- [ ] **Step 3: Hand off to Phase 2**

Summarize findings to the user and write the Phase 2 spec
(`docs/superpowers/specs/2026-06-04-tool-charging-while-moving-phase2-design.md`) using
the confirmed mechanism, then plan Phase 2. **Do not start Phase 2 fix code before this
checkpoint** — the spec mandates the data-driven design.

---

## Self-review notes

- **Spec coverage:** Phase 1 fully covered (the four instrumentation points, the
  Hoe/WateringCan + VerboseLogging gate, AccessTools+null-check resolution, try/catch
  wrapping, registration in ModEntry, v3.8.6, the meaningful playtest, and the
  data-driven Phase-2 checkpoint). Phase 2 is intentionally deferred to its own
  spec/plan after the device capture, per the design.
- **Placeholder scan:** none — full file content and exact commands are inline.
- **Type consistency:** method/helper names (`ShouldLog`, `State`, the four
  `*_Prefix`/`*_Postfix`) and patch targets are consistent between Apply and the
  bodies; field accesses (`toolPower.Value`, `toolHold.Value`, `UsingTool`, `CanMove`,
  `movementDirections.Count`, `CurrentTool`) match the decompiled Farmer surface and
  the existing `FarmerPatches.cs` usage. Task 1 Step 2 flags the fallback if any member
  differs on the PC DLL.
