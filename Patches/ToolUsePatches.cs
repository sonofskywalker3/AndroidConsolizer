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

        /// <summary>Diagnostic counter: re-fires suppressed during the current hold. Reset on release.</summary>
        private static int _suppressedThisHold;

        /// <summary>Diagnostic: last tick the charge-state line was logged (once-per-tick guard).</summary>
        private static int _lastChargeLogTick = -1;

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

                // Stage 2: counteract the tap-to-move teardown that stalls the charge on movement.
                var mobileInput = AccessTools.Method(typeof(Game1), "_mobileUpdateControlInput");
                if (mobileInput != null)
                    harmony.Patch(mobileInput,
                        postfix: new HarmonyMethod(typeof(ToolUsePatches), nameof(MobileUpdateControlInput_Postfix)));
                else
                    Monitor.Log("[MoveCharge] Game1._mobileUpdateControlInput not found — charge keep-alive not attached.", LogLevel.Warn);

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
            _suppressedThisHold++;
            return true;
        }

        /// <summary>Called from GameplayButtonPatches when the game-facing use-tool button is
        /// released, closing the hold session so the next press starts fresh.</summary>
        internal static void OnUseToolReleased()
        {
            _holdActive = false;
            _allowTick = -1;
            _suppressedThisHold = 0;
        }

        /// <summary>VerboseLogging-gated, once-per-tick charge-state snapshot. Lets us verify from
        /// the device log (engineering correctness, no playtest needed) whether the held-button
        /// re-fire is being suppressed AND whether the engine's charge ramp accumulates toolPower
        /// while moving — the data the Stage-1-vs-Stage-2 decision (#25) turns on. Logs only while
        /// a qualifying tool is equipped and a charge is in flight (button held, hold session open,
        /// or power already building). Called once per tick from ModEntry.OnUpdateTicked.</summary>
        internal static void LogChargeStateIfVerbose()
        {
            try
            {
                if (!(ModEntry.Config?.VerboseLogging ?? false)) return;
                if (!IsQualifyingTool()) return;

                var p = Game1.player;
                if (p == null) return;

                bool inFlight = _holdActive || GameplayButtonPatches.GameUseToolHeld || p.toolPower.Value > 0;
                if (!inFlight) return;

                if (Game1.ticks == _lastChargeLogTick) return;
                _lastChargeLogTick = Game1.ticks;

                Monitor?.Log(
                    $"[MoveCharge] tick={Game1.ticks} tool={p.CurrentTool?.Name} power={p.toolPower.Value} "
                    + $"hold={p.toolHold.Value} using={p.UsingTool} canRelease={p.canReleaseTool} canMove={p.CanMove} "
                    + $"moveDirs={p.movementDirections.Count} holdActive={_holdActive} "
                    + $"suppressed={_suppressedThisHold} btnHeld={GameplayButtonPatches.GameUseToolHeld} "
                    + $"Lstk=({GameplayButtonPatches.RawLeftStickX:F2},{GameplayButtonPatches.RawLeftStickY:F2})",
                    LogLevel.Debug);
            }
            catch { /* diagnostic must never break gameplay */ }
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

        /// <summary>Stage 2 — keep the charge alive while moving. The Android tap-to-move system
        /// (driven from Game1._mobileUpdateControlInput, which runs every tick BEFORE the charge
        /// ramp and press block inside Game1.UpdateControlInput) tears down an in-progress tool use
        /// the instant a movement direction is held: it drops canReleaseTool/useToolHeld so the
        /// engine's charge ramp (decompile ~13914) stalls and canStrafeForToolUse returns false,
        /// then ends the use and fires the charged area prematurely. While our hold session is open
        /// over a qualifying upgraded Hoe/Watering Can, re-assert the charge-hold state right after
        /// that teardown so the engine's OWN ramp accumulates toolPower and canStrafeForToolUse lets
        /// the player slide. On button release the session closes (GetState postfix runs earlier in
        /// the same tick), so this stops and vanilla EndUsingTool fires the charged area normally.
        /// The ref params are the caller's (UpdateControlInput) locals.</summary>
        private static void MobileUpdateControlInput_Postfix(ref bool useToolHeld, ref bool useToolButtonReleased)
        {
            try
            {
                var cfg = ModEntry.Config;
                if (cfg == null || !cfg.EnableMoveWhileCharging) return;
                if (!_holdActive || !IsQualifyingTool()) return;

                // Don't re-assert charge state when gameplay is suspended by an event/cutscene or
                // farm event. Mirrors the engine's own `flag4` gate on the charge ramp; without it,
                // a button still physically held when a cutscene starts would force UsingTool=true
                // for the whole event, fighting the game's completelyStopAnimatingOrDoingAction.
                if (Game1.eventUp || Game1.farmEvent != null) return;

                var p = Game1.player;
                if (p == null) return;

                // Undo the tap-to-move teardown so the engine's charge ramp + strafe run normally.
                useToolHeld = true;
                useToolButtonReleased = false;
                p.canReleaseTool = true;
                p.UsingTool = true;
            }
            catch { /* never break tool use */ }
        }
    }
}
