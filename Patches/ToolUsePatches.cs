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
