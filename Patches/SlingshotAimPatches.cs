using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// #25b — Slingshot combat (console-parity aim).
    ///
    /// Phase 1 is a DIAGNOSTIC ONLY (no behaviour change): a VerboseLogging-gated, once-per-tick
    /// snapshot of the full slingshot control state while a Slingshot is the current tool. It tells
    /// us — without guessing — where the chain breaks on Android: whether movement is stopped, whether
    /// the held tool button reaches the game (post X/Y swap), whether usingSlingshot/UsingTool engage
    /// on hold, whether the engine's aim/charge ramp runs, and what the aim position resolves to.
    ///
    /// Console reference (decompile): holding the use-tool button enters Slingshot.beginUsing →
    /// usingSlingshot/UsingTool; Slingshot.tickUpdate → updateAimPos reads the LEFT thumbstick to
    /// swing the crosshair; release → onRelease → PerformFire. The Android tap-to-move teardown
    /// (Game1._mobileUpdateControlInput) and the mod's X/Y swap are the prime suspects (see the
    /// design spec 2026-06-04-slingshot-combat-25b-design.md).
    ///
    /// Android-vs-PC reflection safety: options.weaponControl and Game1.controllerSlingshotSafeTime
    /// are mobile-only members, resolved via AccessTools and read defensively.
    /// </summary>
    internal static class SlingshotAimPatches
    {
        private static IMonitor Monitor;

        /// <summary>Android-only Options.weaponControl (touch weapon-control mode). Reflected.</summary>
        private static FieldInfo _weaponControlField;

        /// <summary>Android-only Game1.controllerSlingshotSafeTime (post-fire movement lockout). Reflected.</summary>
        private static FieldInfo _safeTimeField;

        /// <summary>Once-per-tick guard for the diagnostic line.</summary>
        private static int _lastLogTick = -1;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                _weaponControlField = AccessTools.Field(typeof(Options), "weaponControl");
                _safeTimeField = AccessTools.Field(typeof(Game1), "controllerSlingshotSafeTime");
                // Phase 1: no Harmony patches — diagnostic is driven from ModEntry.OnUpdateTicked.
                Monitor.Log("Slingshot-aim (#25b) diagnostic ready.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to init slingshot-aim diagnostic: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>VerboseLogging-gated, once-per-tick slingshot control-state snapshot. Active only
        /// while a Slingshot is the current tool, so it stays quiet during normal play. Called from
        /// ModEntry.OnUpdateTicked. Never throws.</summary>
        internal static void LogAimStateIfVerbose()
        {
            try
            {
                if (!(ModEntry.Config?.VerboseLogging ?? false)) return;
                if (!Context.IsWorldReady) return;

                var p = Game1.player;
                if (p?.CurrentTool is not Slingshot slingshot) return;

                if (Game1.ticks == _lastLogTick) return;
                _lastLogTick = Game1.ticks;

                // Android-only state (reflected, defensive).
                int weaponControl = -1;
                try { if (_weaponControlField != null) weaponControl = (int)_weaponControlField.GetValue(Game1.options); } catch { }
                float safeTime = -1f;
                try { if (_safeTimeField != null) safeTime = (float)_safeTimeField.GetValue(Game1.game1); } catch { }

                // Game-facing left stick (post swap/suppression) vs the raw value cached pre-suppression.
                Vector2 gameLeft = GamePad.GetState(PlayerIndex.One).ThumbSticks.Left;

                // Slingshot-side aim/charge readouts.
                float charge = 0f;
                int backArm = 0;
                int aimX = 0, aimY = 0;
                try { charge = slingshot.GetSlingshotChargeTime(); } catch { }
                try { backArm = slingshot.GetBackArmDistance(p); } catch { }
                try { aimX = slingshot.aimPos.X; aimY = slingshot.aimPos.Y; } catch { }

                Monitor?.Log(
                    $"[Slingshot] tick={Game1.ticks} wc={weaponControl} usingSling={p.usingSlingshot} "
                    + $"usingTool={p.UsingTool} canRelease={p.canReleaseTool} canMove={p.CanMove} "
                    + $"moveDirs={p.movementDirections.Count} toolPower={p.toolPower.Value} "
                    + $"safeTime={safeTime:F2} charge={charge:F2} backArm={backArm} aim=({aimX},{aimY}) "
                    + $"btnHeld(X)={GameplayButtonPatches.GameUseToolHeld} swapXY={GameplayButtonPatches.ShouldSwapXY()} "
                    + $"Lraw=({GameplayButtonPatches.RawLeftStickX:F2},{GameplayButtonPatches.RawLeftStickY:F2}) "
                    + $"Lgame=({gameLeft.X:F2},{gameLeft.Y:F2}) ammo={slingshot.attachments[0]?.Stack ?? 0}",
                    LogLevel.Debug);
            }
            catch { /* diagnostic must never break gameplay */ }
        }
    }
}
