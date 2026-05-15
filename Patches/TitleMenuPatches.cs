using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// DIAGNOSTIC BUILD (v3.7.3) for #17 — title-menu cursor invisible until stick input.
    ///
    /// The v3.7.2 fix (postfix gated on gamepadControls && !lastCursorMotionWasMouse)
    /// did not work. This build instruments TitleMenu.update() to log the full gate
    /// state whenever it changes, and unconditionally forces mouseCursorTransparency
    /// = 1f (NO gates, and NO mouse-position move so lastCursorMotionWasMouse stays
    /// uncontaminated) to test whether forcing transparency alone makes the cursor
    /// appear at title load. To be reverted/replaced by the real fix once the log
    /// identifies which gate was blocking.
    /// </summary>
    internal static class TitleMenuPatches
    {
        private static IMonitor Monitor;
        private static string _lastSnapshot;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                var update = AccessTools.Method(typeof(TitleMenu), nameof(TitleMenu.update), new[] { typeof(GameTime) });
                if (update != null)
                {
                    harmony.Patch(
                        original: update,
                        postfix: new HarmonyMethod(typeof(TitleMenuPatches), nameof(Update_Postfix))
                    );
                    Monitor.Log("TitleMenu patches applied (DIAGNOSTIC v3.7.3).", LogLevel.Trace);
                }
                else
                {
                    Monitor.Log("TitleMenuPatches: 'update' method not found — patch skipped.", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply TitleMenu patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// DIAGNOSTIC postfix on TitleMenu.update — logs the full gate-state snapshot
        /// whenever it changes, then unconditionally forces mouseCursorTransparency = 1f
        /// (no gates, no mouse move). Reveals (a) which gate condition blocked the
        /// v3.7.2 fix and (b) whether forcing transparency alone makes the cursor visible.
        /// </summary>
        private static void Update_Postfix(TitleMenu __instance)
        {
            try
            {
                bool connected = GamePad.GetState(PlayerIndex.One).IsConnected;
                string snapped = __instance.currentlySnappedComponent != null
                    ? __instance.currentlySnappedComponent.myID.ToString()
                    : "null";
                string sub = TitleMenu.subMenu != null ? TitleMenu.subMenu.GetType().Name : "null";

                string snapshot =
                    $"gamepadControls={Game1.options.gamepadControls} " +
                    $"lastCursorMotionWasMouse={Game1.lastCursorMotionWasMouse} " +
                    $"gamepadMode={Game1.options.gamepadMode} " +
                    $"snappyMenus={Game1.options.snappyMenus} " +
                    $"gamepadConnected={connected} " +
                    $"titleInPosition={__instance.titleInPosition} " +
                    $"buttonsToShow={__instance.buttonsToShow}/{TitleMenu.numberOfButtons} " +
                    $"subMenu={sub} " +
                    $"snappedComponent={snapped} " +
                    $"mouseCursorTransparency={Game1.mouseCursorTransparency} " +
                    $"mousePos=({Game1.getMouseX()},{Game1.getMouseY()})";

                if (snapshot != _lastSnapshot)
                {
                    _lastSnapshot = snapshot;
                    Monitor?.Log($"[TitleDiag] {snapshot}", LogLevel.Info);
                }

                // PROBE: unconditionally force transparency (no gates, no mouse move).
                // Tests whether forcing transparency alone makes the cursor visible.
                Game1.mouseCursorTransparency = 1f;
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[TitleMenu] Update_Postfix DIAGNOSTIC error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
