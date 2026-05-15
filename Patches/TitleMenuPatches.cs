using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Harmony patch for the title/main menu cursor initial state.
    ///
    /// On Android, Game1.options.snappyMenus is false, so TitleMenu never runs
    /// its own snapToDefaultClickableComponent() at setup — the cursor is never
    /// positioned on the Load/New button. Separately, mouseCursorTransparency
    /// stays at 0 until the player's first stick input, so even a positioned
    /// cursor is drawn at 0% opacity. Net effect: no visible cursor on the title
    /// screen until the player moves the stick.
    ///
    /// Fix: a postfix on TitleMenu.update() supplies the missing state every
    /// frame under gamepad control. It calls the game's own
    /// snapToDefaultClickableComponent() once (when nothing is snapped) to
    /// position the cursor on Load (or New for a fresh save), and forces
    /// mouseCursorTransparency = 1f so the game's own drawMouse renders it.
    /// Gated on Game1.options.gamepadControls && !Game1.lastCursorMotionWasMouse
    /// so pure-touch users keep vanilla behavior (no title-screen cursor).
    /// </summary>
    internal static class TitleMenuPatches
    {
        private static IMonitor Monitor;

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
                    Monitor.Log("TitleMenu patches applied.", LogLevel.Trace);
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
        /// Postfix on TitleMenu.update — under gamepad control, once the title
        /// intro has settled and the main button row is interactive, ensure the
        /// cursor is snapped to a default button and visible. Runs every frame so
        /// that if Game1 resets mouseCursorTransparency, it is re-set the same
        /// frame; the snap itself fires only once (guarded on
        /// currentlySnappedComponent == null) so it never fights the player's
        /// own navigation.
        /// </summary>
        private static void Update_Postfix(TitleMenu __instance)
        {
            try
            {
                if (!Game1.options.gamepadControls || Game1.lastCursorMotionWasMouse)
                    return;

                // Only act once the intro animation has settled and the main
                // button row is fully shown. Sub-menus (Load Game, Co-op, About)
                // are out of scope — they manage their own navigation.
                if (!__instance.titleInPosition)
                    return;
                if (__instance.buttonsToShow < TitleMenu.numberOfButtons)
                    return;
                if (TitleMenu.subMenu != null)
                    return;

                if (__instance.currentlySnappedComponent == null)
                    __instance.snapToDefaultClickableComponent();

                Game1.mouseCursorTransparency = 1f;
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[TitleMenu] Update_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
