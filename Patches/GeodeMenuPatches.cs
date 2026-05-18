using System;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// PURE-OBSERVATION shim for #19 GeodeMenu visual feedback work.
    ///
    /// The behavioural two-press patch (v3.7.21 / v3.7.22) hard-crashed the
    /// app between method entry and the first interior log line in
    /// TryPlaceOnAnvil — escaping the prefix's try/catch — and the heavy
    /// per-button-press logging starved the input thread enough that the
    /// vanilla snap cursor and joystick nav stopped working on the G Cloud.
    ///
    /// This rewrite ships ZERO behavioural change. We patch the menu only to
    /// log what vanilla actually does (which gamepad buttons reach the
    /// menu, whether startGeodeCrack runs and how often), and pass every
    /// button through to vanilla unchanged. Goal: establish a clean baseline
    /// so the next iteration designs the two-press flow from observed data
    /// rather than from a PC-DLL decompile reading.
    ///
    /// All logging is button-gated (A/X/B only — nav passes through silently)
    /// and wrapped in try/catch so a logger fault can never escape into
    /// vanilla. EnableConsoleGeodeMenu still exists in ModConfig but is now
    /// inert (no behaviour to gate); the next iteration will re-introduce it
    /// once we have a non-crashing fix.
    /// </summary>
    internal static class GeodeMenuPatches
    {
        private static IMonitor Monitor;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(ReceiveGamePadButton_Prefix))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.startGeodeCrack)),
                    postfix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(StartGeodeCrack_Postfix))
                );
                Monitor.Log("GeodeMenu observation patch attached (pass-through; no behavioural change).", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to attach GeodeMenu observation patch: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Called from ModEntry.OnMenuChanged on every GeodeMenu open/close.</summary>
        public static void OnMenuChanged()
        {
            // No state to reset — observation only.
        }

        private static bool ReceiveGamePadButton_Prefix(Buttons b)
        {
            // Gate logging on A/B/X — these are the buttons we care about
            // for the two-press redesign. Nav events (D-pad / stick) pass
            // through silently to avoid starving the input thread.
            if (b == Buttons.A || b == Buttons.B || b == Buttons.X)
            {
                try
                {
                    Monitor.Log($"[GeodeMenu obs] receiveGamePadButton({b}) — vanilla handler runs.", LogLevel.Info);
                }
                catch { }
            }
            return true; // ALWAYS pass to vanilla.
        }

        private static void StartGeodeCrack_Postfix(GeodeMenu __instance)
        {
            try
            {
                Monitor.Log($"[GeodeMenu obs] startGeodeCrack fired. animTimer={__instance.geodeAnimationTimer}", LogLevel.Info);
            }
            catch { }
        }
    }
}
