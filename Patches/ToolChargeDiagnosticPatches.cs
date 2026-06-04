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

        private static int _lastBtnLogTick = -1;

        private static void CanStrafeForToolUse_Postfix(Farmer __instance, bool __result)
        {
            try
            {
                if (__instance != Game1.player || !ShouldLog()) return;
                if (Game1.ticks == _lastBtnLogTick) return; // once per tick (canStrafe is polled many times/tick)
                _lastBtnLogTick = Game1.ticks;

                bool moving = System.Math.Abs(GameplayButtonPatches.RawLeftStickX) > 0.01f
                           || System.Math.Abs(GameplayButtonPatches.RawLeftStickY) > 0.01f
                           || (Game1.player?.movementDirections?.Count ?? 0) > 0;

                Monitor?.Log(
                    $"[ToolCharge] canStrafe={__result} {State()} "
                    + $"| rawX={GameplayButtonPatches.DiagRawToolX} rawY={GameplayButtonPatches.DiagRawToolY} "
                    + $"finalX={GameplayButtonPatches.DiagFinalToolX} finalY={GameplayButtonPatches.DiagFinalToolY} "
                    + $"moving={moving} Lstk=({GameplayButtonPatches.RawLeftStickX:F2},{GameplayButtonPatches.RawLeftStickY:F2})",
                    LogLevel.Debug);
            }
            catch { }
        }
    }
}
