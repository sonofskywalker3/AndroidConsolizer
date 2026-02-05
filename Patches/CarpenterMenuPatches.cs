using System;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Patches for CarpenterMenu (Robin's building menu) to prevent instant close on Android.
    ///
    /// Problem: Pressing A to select "Construct Farm Buildings" in dialogue also fires input
    /// into the newly opened CarpenterMenu, which instantly closes it. The menu's
    /// snapToDefaultClickableComponent() snaps to the cancel button (ID 107), making any
    /// stray input hit the close button.
    ///
    /// Fix: Block ALL input methods during a grace period after the menu opens.
    /// Previous attempt only blocked receiveLeftClick and didn't work — the close likely
    /// comes through receiveKeyPress or receiveGamePadButton instead.
    /// </summary>
    internal static class CarpenterMenuPatches
    {
        private static IMonitor Monitor;

        /// <summary>Tick when the CarpenterMenu was opened. -1 means not tracking.</summary>
        private static int MenuOpenTick = -1;

        /// <summary>Number of ticks to block all input after menu opens.</summary>
        private const int GracePeriodTicks = 20;

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            if (!ModEntry.Config.EnableCarpenterMenuFix)
            {
                Monitor.Log("Carpenter menu fix is disabled in config.", LogLevel.Trace);
                return;
            }

            try
            {
                // Block receiveLeftClick — Android A-button-as-click may fire here
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.receiveLeftClick)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(ReceiveLeftClick_Prefix))
                );

                // Block leftClickHeld — Android may fire hold events from the A press
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.leftClickHeld)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(LeftClickHeld_Prefix))
                );

                // Block receiveKeyPress — escape/menu key might be simulated from controller
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.receiveKeyPress)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(ReceiveKeyPress_Prefix))
                );

                // Block receiveGamePadButton — base class handles B to exit
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(ReceiveGamePadButton_Prefix))
                );

                Monitor.Log("CarpenterMenu patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply CarpenterMenu patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Called from ModEntry.OnMenuChanged when a CarpenterMenu opens.</summary>
        public static void OnMenuOpened()
        {
            MenuOpenTick = Game1.ticks;
            Monitor.Log($"CarpenterMenu opened at tick {MenuOpenTick}. Grace period: {GracePeriodTicks} ticks.", LogLevel.Debug);
        }

        /// <summary>Called from ModEntry.OnMenuChanged when the CarpenterMenu closes.</summary>
        public static void OnMenuClosed()
        {
            if (MenuOpenTick >= 0)
            {
                int duration = Game1.ticks - MenuOpenTick;
                Monitor.Log($"CarpenterMenu closed after {duration} ticks (grace was {GracePeriodTicks}).", LogLevel.Debug);
            }
            MenuOpenTick = -1;
        }

        /// <summary>Check if we're within the grace period after menu open.</summary>
        private static bool IsInGracePeriod()
        {
            return MenuOpenTick >= 0 && (Game1.ticks - MenuOpenTick) < GracePeriodTicks;
        }

        /// <summary>Check if the A button is currently physically pressed.</summary>
        private static bool IsAButtonPhysicallyPressed()
        {
            GamePadState gpState = GamePad.GetState(Microsoft.Xna.Framework.PlayerIndex.One);
            return gpState.Buttons.A == ButtonState.Pressed;
        }

        /// <summary>Prefix for CarpenterMenu.receiveLeftClick.</summary>
        private static bool ReceiveLeftClick_Prefix(CarpenterMenu __instance, int x, int y, bool playSound)
        {
            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return true;

            if (IsInGracePeriod())
            {
                int elapsed = Game1.ticks - MenuOpenTick;
                Monitor.Log($"[CarpenterMenu] BLOCKED receiveLeftClick at ({x},{y}) — grace period ({elapsed}/{GracePeriodTicks} ticks)", LogLevel.Debug);
                return false;
            }

            if (IsAButtonPhysicallyPressed())
            {
                Monitor.Log($"[CarpenterMenu] BLOCKED receiveLeftClick at ({x},{y}) — A button physically pressed", LogLevel.Debug);
                return false;
            }

            Monitor.Log($"[CarpenterMenu] ALLOWED receiveLeftClick at ({x},{y})", LogLevel.Trace);
            return true;
        }

        /// <summary>Prefix for CarpenterMenu.leftClickHeld.</summary>
        private static bool LeftClickHeld_Prefix(CarpenterMenu __instance, int x, int y)
        {
            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return true;

            if (IsInGracePeriod())
            {
                Monitor.Log($"[CarpenterMenu] BLOCKED leftClickHeld — grace period", LogLevel.Trace);
                return false;
            }

            if (IsAButtonPhysicallyPressed())
            {
                Monitor.Log($"[CarpenterMenu] BLOCKED leftClickHeld — A button physically pressed", LogLevel.Trace);
                return false;
            }

            return true;
        }

        /// <summary>Prefix for CarpenterMenu.receiveKeyPress.</summary>
        private static bool ReceiveKeyPress_Prefix(CarpenterMenu __instance, Microsoft.Xna.Framework.Input.Keys key)
        {
            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return true;

            if (IsInGracePeriod())
            {
                Monitor.Log($"[CarpenterMenu] BLOCKED receiveKeyPress key={key} — grace period ({Game1.ticks - MenuOpenTick}/{GracePeriodTicks} ticks)", LogLevel.Debug);
                return false;
            }

            Monitor.Log($"[CarpenterMenu] ALLOWED receiveKeyPress key={key}", LogLevel.Trace);
            return true;
        }

        /// <summary>Prefix for CarpenterMenu.receiveGamePadButton.</summary>
        private static bool ReceiveGamePadButton_Prefix(CarpenterMenu __instance, Buttons b)
        {
            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return true;

            if (IsInGracePeriod())
            {
                Monitor.Log($"[CarpenterMenu] BLOCKED receiveGamePadButton button={b} — grace period ({Game1.ticks - MenuOpenTick}/{GracePeriodTicks} ticks)", LogLevel.Debug);
                return false;
            }

            Monitor.Log($"[CarpenterMenu] ALLOWED receiveGamePadButton button={b}", LogLevel.Trace);
            return true;
        }
    }
}
