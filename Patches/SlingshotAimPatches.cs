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

                // The fix: drive the slingshot from the physical tool button, not the tap-to-move
                // injections. Postfix the same Android mobile-input method #25 uses.
                var mobileInput = AccessTools.Method(typeof(Game1), "_mobileUpdateControlInput");
                if (mobileInput != null)
                    harmony.Patch(mobileInput,
                        postfix: new HarmonyMethod(typeof(SlingshotAimPatches), nameof(MobileUpdateControlInput_Postfix)));
                else
                    Monitor.Log("[Slingshot] Game1._mobileUpdateControlInput not found — aim fix not attached.", LogLevel.Warn);

                Monitor.Log("Slingshot-aim (#25b) patches applied.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply slingshot-aim patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>The #25b fix. Android's tap-to-move/mobile-input layer injects the slingshot's
        /// use-tool button events (Game1._mobileUpdateControlInput lines 2446-2457) from stick/tap
        /// motion via tapToMove.mobileKeyStates — so stick motion alone fires the slingshot, a held
        /// button auto-fires repeatedly (spurious mid-hold useToolButtonReleased), and movement is
        /// blocked because any stick touch enters usingSlingshot/UsingTool. While a Slingshot is the
        /// current tool, override the three use-tool refs to reflect ONLY the physical tool button
        /// (final Buttons.X, tracked in GameplayButtonPatches): held → draw/aim, release edge → fire
        /// once. This runs inside Game1.UpdateControlInput before the press block (~13884) and the
        /// release path (~13689), so the clean console path drives beginUsing / tickUpdate (left-stick
        /// aim) / onRelease. The ref params are the caller's locals, matched by name.</summary>
        private static void MobileUpdateControlInput_Postfix(
            ref bool useToolButtonPressed, ref bool useToolButtonReleased, ref bool useToolHeld)
        {
            try
            {
                var cfg = ModEntry.Config;
                if (cfg == null || !cfg.EnableSlingshotAim) return;
                if (Game1.player?.CurrentTool is not Slingshot) return;

                // Console aim direction. Android defaults Options.useLegacySlingshotFiring = TRUE
                // (Options.cs:2515, mobile default) — the old "pull-back" geometry: crosshair draws
                // OPPOSITE the stick, defaults straight down when centered, and won't fire without
                // real aim distance (a centered release does nothing). Switch runs NON-legacy direct
                // aim (crosshair points where you push; a held release fires toward facing). Flip it
                // for parity. Guarded so we only write once.
                if (Game1.options.useLegacySlingshotFiring)
                    Game1.options.useLegacySlingshotFiring = false;

                // Don't fight the engine when gameplay is suspended (mirrors the engine's flag4).
                if (Game1.eventUp || Game1.farmEvent != null) return;

                bool held = GameplayButtonPatches.GameUseToolHeld;

                // Replace the stick/tap-derived injections with the physical button only.
                useToolHeld = held;
                useToolButtonPressed = held;                                  // begin-once: gated by !UsingTool at ~13884
                useToolButtonReleased = GameplayButtonPatches.GameUseToolReleasedEdge; // fire once on physical release
            }
            catch { /* never break slingshot use */ }
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
                    + $"legacy={Game1.options.useLegacySlingshotFiring} "
                    + $"btnHeld(X)={GameplayButtonPatches.GameUseToolHeld} swapXY={GameplayButtonPatches.ShouldSwapXY()} "
                    + $"Lraw=({GameplayButtonPatches.RawLeftStickX:F2},{GameplayButtonPatches.RawLeftStickY:F2}) "
                    + $"Lgame=({gameLeft.X:F2},{gameLeft.Y:F2}) ammo={slingshot.attachments[0]?.Stack ?? 0}",
                    LogLevel.Debug);
            }
            catch { /* diagnostic must never break gameplay */ }
        }
    }
}
