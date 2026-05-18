using System;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
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
    /// Observation logs (v3.7.23) confirmed: A produced zero
    /// startGeodeCrack events across 5 presses; every X press fired it.
    ///
    /// Fix: intercept Buttons.A on this menu and re-fire as
    /// receiveGamePadButton(Buttons.X). Vanilla's own X-case then runs end
    /// to end. No reflection, no state machine.
    ///
    /// Same-tick touch-sim suppression (v3.7.25): Android's
    /// Game1.updateActiveMenu fires a synthesised receiveLeftClick after
    /// every A press (memory: "Android touch simulation" from
    /// CarpenterMenu). It does NOT fire for physical X presses, which is
    /// why vanilla X works cleanly and a naïve A→X redirect breaks. The
    /// touch-sim leftClick lands at the snap cursor — which the player
    /// has just moved onto an inventory slot — and calls
    /// inventory.leftClick, picking up the (possibly partially-consumed)
    /// geode stack into heldItem. Result: every A press both cracks the
    /// geode AND grabs whatever is left in the slot, plus disturbs snap
    /// state, producing the inconsistent "moves geode / moves cursor /
    /// disappears" behaviour the user reported on G Cloud.
    /// We block that one leftClick with a tick stamp.
    /// </summary>
    internal static class GeodeMenuPatches
    {
        private static IMonitor Monitor;

        // Recursion guard: when our prefix re-fires the button as X, our
        // own prefix runs again. Skip the second pass so vanilla X executes.
        [ThreadStatic]
        private static bool _inRedirect;

        // Game1.ticks value on which we last redirected A→X. The
        // touch-sim leftClick from the same A press fires later in the
        // same tick; we suppress one leftClick whose tick matches.
        private static int _redirectTick = -1;

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
                    original: AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.receiveLeftClick)),
                    prefix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(ReceiveLeftClick_Prefix))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.startGeodeCrack)),
                    postfix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(StartGeodeCrack_Postfix))
                );
                monitor.Log("GeodeMenu A→X redirect + touch-sim suppression attached.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                monitor.Log($"Failed to attach GeodeMenu patch: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Called from ModEntry.OnMenuChanged on every GeodeMenu open/close.</summary>
        public static void OnMenuChanged()
        {
            _redirectTick = -1;
        }

        private static bool ReceiveGamePadButton_Prefix(GeodeMenu __instance, Buttons b)
        {
            if (_inRedirect) return true;
            if (b != Buttons.A) return true;
            if (ModEntry.Config?.EnableConsoleGeodeMenu != true) return true;

            _inRedirect = true;
            try
            {
                _redirectTick = Game1.ticks;
                try { Monitor.Log($"[GeodeMenu] A redirect → X at tick {_redirectTick}", LogLevel.Info); } catch { }
                __instance.receiveGamePadButton(Buttons.X);
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"[GeodeMenu] A→X redirect error: {ex.Message}", LogLevel.Error); } catch { }
                return true;
            }
            finally
            {
                _inRedirect = false;
            }
            return false;
        }

        private static bool ReceiveLeftClick_Prefix()
        {
            if (ModEntry.Config?.EnableConsoleGeodeMenu != true) return true;
            if (_redirectTick != Game1.ticks) return true;

            // Same-tick touch-sim leftClick from the A press we just
            // redirected. Eat it so it can't pick up the partially-
            // consumed geode stack or disturb snap state.
            try { Monitor.Log($"[GeodeMenu] suppressing touch-sim leftClick at tick {Game1.ticks}", LogLevel.Info); } catch { }
            _redirectTick = -1;
            return false;
        }

        private static void StartGeodeCrack_Postfix(GeodeMenu __instance)
        {
            try { Monitor.Log($"[GeodeMenu] startGeodeCrack fired. animTimer={__instance.geodeAnimationTimer}", LogLevel.Info); } catch { }
        }
    }
}
