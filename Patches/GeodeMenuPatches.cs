using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Console-parity geode menu input on Clint's anvil:
    ///   * single-press A places + cracks the selected geode (Switch
    ///     behaviour, confirmed against Switch hardware 2026-05-18);
    ///   * the touch-sim leftClick Android fires after every A press is
    ///     suppressed so it can't pick up the partially-consumed stack;
    ///   * the item tooltip auto-shows for whatever geode is selected,
    ///     instead of vanilla's "press A to toggle" (we redirected A);
    ///   * the snap cursor sits at the slot center instead of vanilla's
    ///     bounds-right/4 offset (which renders top-left of the slot on
    ///     G Cloud's UI scaling for unclear reasons).
    ///
    /// Vanilla Android GeodeMenu has X = place + crack (atomic, one
    /// press, geode visible on anvil for the ~2.7s animation) and A =
    /// toggle _showTooltip. Our redirect re-fires A as X via the public
    /// receiveGamePadButton, then eats the same-tick synthesised
    /// leftClick. _showTooltip is forced true on menu open so the
    /// vanilla fall-through (which runs at the bottom of every
    /// receiveGamePadButton) calls inventory.GamePadShowInfoPanel each
    /// time, keeping the tooltip in sync with the selected slot.
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

        // Reflected private members. Resolved at startup, null-checked
        // at every use so a missing field on some port silently degrades
        // rather than crashing.
        private static FieldInfo _showTooltipField;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                _showTooltipField = AccessTools.Field(typeof(GeodeMenu), "_showTooltip");
                if (_showTooltipField == null)
                    monitor.Log("[GeodeMenu] _showTooltip not found — tooltip auto-show disabled.", LogLevel.Warn);

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
                harmony.Patch(
                    original: AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.snapCursorToCurrentSnappedComponent)),
                    postfix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(SnapCursorToCurrentSnappedComponent_Postfix))
                );
                monitor.Log("GeodeMenu patches attached (A→X + touch-sim suppression + tooltip + cursor center).", LogLevel.Info);
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

        /// <summary>Called from ModEntry.OnMenuChanged when a GeodeMenu OPENS.
        /// Sets _showTooltip = true on the fresh instance so vanilla's
        /// per-press fall-through auto-fires GamePadShowInfoPanel.</summary>
        public static void OnGeodeMenuOpened(GeodeMenu menu)
        {
            if (menu == null || _showTooltipField == null) return;
            if (ModEntry.Config?.EnableConsoleGeodeMenu != true) return;
            try
            {
                _showTooltipField.SetValue(menu, true);
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"[GeodeMenu] couldn't force _showTooltip=true: {ex.Message}", LogLevel.Warn); } catch { }
            }
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

        /// <summary>
        /// Re-anchor the snap cursor at the slot center. Vanilla
        /// IClickableMenu.snapCursorToCurrentSnappedComponent positions
        /// the mouse at (bounds.Right - Width/4, bounds.Bottom - Height/4),
        /// which on G Cloud's UI scale renders the cursor sprite at the
        /// top-left of the slot rather than the expected bottom-right.
        /// Centering puts the cursor visually inside the slot regardless
        /// of how the scale path resolves.
        /// </summary>
        private static void SnapCursorToCurrentSnappedComponent_Postfix(GeodeMenu __instance)
        {
            if (ModEntry.Config?.EnableConsoleGeodeMenu != true) return;
            var snapped = __instance.currentlySnappedComponent;
            if (snapped == null) return;
            try
            {
                Game1.setMousePosition(snapped.bounds.Center, ui_scale: true);
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"[GeodeMenu] snap recenter failed: {ex.Message}", LogLevel.Warn); } catch { }
            }
        }
    }
}
