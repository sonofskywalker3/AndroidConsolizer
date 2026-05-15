using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Harmony patch for the title/main menu cursor.
    ///
    /// On Android the game's own drawMouse() produces no visible output on
    /// TitleMenu even when all input state is correct: snappyMenus=True,
    /// gamepadControls=True, mouseCursorTransparency=1f, currentlySnappedComponent
    /// already set to the Load/New button, valid mouse position. The v3.7.2
    /// attempt at supplying state from an update() postfix was a no-op — see the
    /// v3.7.3 diagnostic findings in
    /// docs/superpowers/specs/2026-05-14-title-menu-cursor-design.md
    /// (Revision — 2026-05-15).
    ///
    /// Fix: postfix on TitleMenu.draw() draws the mouse cursor sprite ourselves
    /// at (Game1.getMouseX(), Game1.getMouseY()) under controller control, after
    /// the title intro has settled, when not in a sub-menu. Same pattern as
    /// Patches/ShopMenuPatches.cs (DONE.md #40a, shop sell-tab cursor).
    ///
    /// Gate is Game1.options.gamepadControls && !Game1.lastCursorMotionWasMouse —
    /// GamePad.IsConnected is intentionally NOT used because XInput returns False
    /// for the Ayaneo's built-in controller (diagnostic confirmed).
    /// </summary>
    internal static class TitleMenuPatches
    {
        private static IMonitor Monitor;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                var draw = AccessTools.Method(typeof(TitleMenu), nameof(TitleMenu.draw), new[] { typeof(SpriteBatch) });
                if (draw != null)
                {
                    harmony.Patch(
                        original: draw,
                        postfix: new HarmonyMethod(typeof(TitleMenuPatches), nameof(Draw_Postfix))
                    );
                    Monitor.Log("TitleMenu patches applied.", LogLevel.Trace);
                }
                else
                {
                    Monitor.Log("TitleMenuPatches: 'draw' method not found — patch skipped.", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply TitleMenu patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Postfix on TitleMenu.draw — when a controller is in use and the title
        /// has settled, draw the mouse cursor sprite ourselves at the current
        /// mouse position. The game's own drawMouse() produces nothing on the
        /// Android title screen even with correct input state.
        /// </summary>
        private static void Draw_Postfix(TitleMenu __instance, SpriteBatch b)
        {
            try
            {
                if (!Game1.options.gamepadControls || Game1.lastCursorMotionWasMouse)
                    return;
                if (!__instance.titleInPosition)
                    return;
                if (TitleMenu.subMenu != null)
                    return;

                int cursorTile = Game1.options.snappyMenus ? 44 : Game1.mouseCursor;
                b.Draw(
                    Game1.mouseCursors,
                    new Vector2(Game1.getMouseX(), Game1.getMouseY()),
                    Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, cursorTile, 16, 16),
                    Color.White,
                    0f,
                    Vector2.Zero,
                    4f + Game1.dialogueButtonScale / 150f,
                    SpriteEffects.None,
                    1f
                );
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[TitleMenu] Draw_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
