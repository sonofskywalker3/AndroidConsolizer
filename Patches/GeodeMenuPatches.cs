using System;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Console-parity geode menu input: single-press A places + cracks the
    /// selected geode. (Switch behaviour, confirmed by user testing on
    /// Switch hardware 2026-05-18.)
    ///
    /// Vanilla Android GeodeMenu:
    ///   * X = place selected geode on anvil and start crack animation
    ///     (one press, atomic — the geode IS visible on the anvil for
    ///     the ~2.7s animation).
    ///   * A = toggle a tooltip (no console analogue, useless).
    /// Observation logs from v3.7.23 confirmed: A produced zero
    /// startGeodeCrack events across 5 presses; every X press fired it.
    ///
    /// Fix: intercept Buttons.A on this menu and re-fire it as
    /// receiveGamePadButton(Buttons.X). Vanilla's own X-case then runs end
    /// to end — including the fall-through that syncs currentlySelectedItem
    /// and tooltip state. No reflection, no state machine.
    ///
    /// (Earlier two-press designs in v3.7.21 / v3.7.22 were built on the
    /// wrong premise that Switch uses two-press A. They also hit a
    /// non-managed fault inside FieldInfo.GetValue on Android Mono that
    /// escaped our try/catch and hard-crashed the app. This version uses
    /// only public API to sidestep that.)
    /// </summary>
    internal static class GeodeMenuPatches
    {
        private static IMonitor Monitor;

        // Recursion guard: when our prefix re-fires the button as X, our
        // own prefix runs again. Skip the second pass so vanilla X executes.
        [ThreadStatic]
        private static bool _inRedirect;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(ReceiveGamePadButton_Prefix))
                );
                monitor.Log("GeodeMenu A→X redirect attached.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                monitor.Log($"Failed to attach GeodeMenu patch: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Called from ModEntry.OnMenuChanged on every GeodeMenu open/close.</summary>
        public static void OnMenuChanged()
        {
            // No state to reset — the recursion guard is intra-frame only.
        }

        private static bool ReceiveGamePadButton_Prefix(GeodeMenu __instance, Buttons b)
        {
            if (_inRedirect) return true;
            if (b != Buttons.A) return true;
            if (ModEntry.Config?.EnableConsoleGeodeMenu != true) return true;

            _inRedirect = true;
            try
            {
                __instance.receiveGamePadButton(Buttons.X);
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"[GeodeMenu] A→X redirect error: {ex.Message}", LogLevel.Error); } catch { }
                return true; // fall back to vanilla A handler on error
            }
            finally
            {
                _inRedirect = false;
            }
            return false;
        }
    }
}
