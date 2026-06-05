using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// v4.0 right-stick overworld cursor. Phase 0 = diagnostic: confirm the engine's
    /// native cursor path (Game1.UpdateControlInput:13303-13334 + drawMouseCursor:15618-15692)
    /// fires once AC stops zeroing the right thumbstick in the overworld.
    ///
    /// The console overworld cursor is engine-native (NOT the #18 menu drawMouse trap): when
    /// options.gamepadControls is true, the right stick moves the mouse via setMousePositionRaw
    /// and drawMouseCursor fades it after timerUntilMouseFade (4000ms). AC currently starves it
    /// by zeroing the right stick in GameplayButtonPatches. This diagnostic observes whether the
    /// engine path runs on the G Cloud once un-starved.
    /// </summary>
    internal static class RightStickCursorPatches
    {
        private static IMonitor Monitor;

        private static int _lastLoggedTick = -1;

        // timerUntilMouseFade is public static int on Android; reflect defensively (Android-vs-PC pattern).
        private static readonly FieldInfo _timerUntilMouseFade =
            AccessTools.Field(typeof(Game1), "timerUntilMouseFade");

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                // Make INTERACTION (check / open / gift / talk) follow the right-stick cursor.
                // Game1.pressActionButton's tile gate (Game1.cs:11971) uses the cursor only when
                // lastCursorMotionWasMouse is true, but the controller A-press handler sets it
                // false (Game1.cs:13449) earlier in the same tick. A prefix re-asserts it true
                // right before the gate reads it, while the cursor is active. Tool-swinging
                // (pressUseToolButton) is deliberately NOT patched — it stays on the facing tile
                // (console parity, user-confirmed on Switch 2026-06-05).
                var pressAction = AccessTools.Method(
                    typeof(Game1), nameof(Game1.pressActionButton),
                    new[] { typeof(KeyboardState), typeof(MouseState), typeof(GamePadState) });
                if (pressAction != null)
                {
                    harmony.Patch(
                        pressAction,
                        prefix: new HarmonyMethod(typeof(RightStickCursorPatches), nameof(PressActionButton_Prefix)));
                }
                else
                {
                    Monitor.Log("[RStickCursor] pressActionButton not found; interaction cursor targeting disabled.", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[RStickCursor] Apply failed: {ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>
        /// Prefix on Game1.pressActionButton: while the right-stick cursor is active, tell the game
        /// the cursor is the pointer so interaction targets the cursor tile (Game1.cs:11971) instead
        /// of the facing tile. Scoped to the cursor-visible window (timerUntilMouseFade > 0) so once
        /// the cursor auto-hides, interaction reverts to the facing tile — matching Switch.
        /// </summary>
        private static void PressActionButton_Prefix()
        {
            try
            {
                if (ModEntry.Config?.EnableRightStickCursor != true) return;
                if (Game1.activeClickableMenu != null || Game1.eventUp) return;
                if (Game1.player?.CurrentTool is StardewValley.Tools.Slingshot) return;

                int fade = 0;
                try { fade = (int)(_timerUntilMouseFade?.GetValue(null) ?? 0); } catch { return; }
                if (fade <= 0) return; // cursor faded → let interaction use the facing tile

                Game1.lastCursorMotionWasMouse = true;
            }
            catch (Exception ex)
            {
                Monitor.Log($"[RStickCursor] pressAction prefix error: {ex.Message}", LogLevel.Trace);
            }
        }

        /// <summary>
        /// Call from ModEntry.OnUpdateTicked. When the right stick moves the overworld cursor,
        /// tell the game the cursor is the active pointer by setting lastCursorMotionWasMouse=true.
        ///
        /// Why: Game1.UpdateControlInput moves the cursor from the right stick (13303-13334) but,
        /// unlike the real-mouse branch, leaves lastCursorMotionWasMouse=false. On Android the
        /// cursor-draw gate (Game1.cs:11971/12161) and Character.GetToolLocation(1213-1219) then
        /// skip the cursor and fall back to the facing tile (player.GetGrabTile) — so the cursor
        /// is invisible AND tools target the facing tile. Setting the flag true makes the engine
        /// draw the cursor (wasMouseVisibleThisFrame becomes true) and aim tools/interaction at
        /// the cursor tile. When the stick goes idle and the 4s fade zeroes mouseCursorTransparency,
        /// the gates' "transparency == 0" term reverts targeting to the facing tile on its own.
        ///
        /// Scoped: overworld only (no active menu), cursor enabled, not while a slingshot is the
        /// active tool (#25b aim = left stick). One-tick lag on first flick is imperceptible.
        /// </summary>
        public static void EnforceTick()
        {
            try
            {
                if (ModEntry.Config?.EnableRightStickCursor != true) return;
                if (Game1.activeClickableMenu != null) return;
                if (Game1.player?.CurrentTool is StardewValley.Tools.Slingshot) return;
                if (GameplayButtonPatches.RawRightStickX == 0f && GameplayButtonPatches.RawRightStickY == 0f) return;

                Game1.lastCursorMotionWasMouse = true;
            }
            catch (Exception ex)
            {
                Monitor.Log($"[RStickCursor] enforce error: {ex.Message}", LogLevel.Trace);
            }
        }

        /// <summary>
        /// Draw the overworld right-stick cursor ourselves. The Android engine's drawMouseCursor
        /// computes cursor state (transparency, wasMouseVisibleThisFrame) but renders NO sprite in
        /// the overworld — mobile strips the hardware cursor — so it stays invisible even with
        /// lastCursorMotionWasMouse=true (device-confirmed v3.9.5). Mirror the #18 museum self-draw:
        /// paint the pointer at Game1.getMouseX/Y. Gated on timerUntilMouseFade > 0, which the engine
        /// counts down from 4000 after the last stick motion — so the cursor auto-hides ~4s after you
        /// stop moving it, matching console. Call from a Display.RenderedHud handler.
        /// </summary>
        public static void DrawCursor(SpriteBatch b)
        {
            try
            {
                if (ModEntry.Config?.EnableRightStickCursor != true) return;
                if (!Context.IsWorldReady || Game1.activeClickableMenu != null || Game1.eventUp) return;
                if (Game1.player == null) return;
                if (Game1.player.CurrentTool is StardewValley.Tools.Slingshot) return;

                int fade = 0;
                try { fade = (int)(_timerUntilMouseFade?.GetValue(null) ?? 0); } catch { return; }
                if (fade <= 0) return; // engine has faded the cursor out (auto-hide)

                float alpha = Game1.mouseCursorTransparency > 0f ? Game1.mouseCursorTransparency : 1f;
                int tile = Game1.mouseCursor >= 0 ? Game1.mouseCursor : 0;

                b.Draw(
                    Game1.mouseCursors,
                    new Vector2(Game1.getMouseX(), Game1.getMouseY()),
                    Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, tile, 16, 16),
                    Color.White * alpha,
                    0f,
                    Vector2.Zero,
                    4f,
                    SpriteEffects.None,
                    1f
                );
            }
            catch (Exception ex)
            {
                Monitor.Log($"[RStickCursor] draw failed: {ex.Message}", LogLevel.Trace);
            }
        }

        /// <summary>Call from ModEntry.OnUpdateTicked. Logs engine cursor state while the right stick moves.</summary>
        public static void DiagnosticTick()
        {
            try
            {
                if (ModEntry.Config?.VerboseLogging != true) return;
                if (Game1.activeClickableMenu != null) return;
                if (Game1.player == null) return;

                float rx = GameplayButtonPatches.RawRightStickX;
                float ry = GameplayButtonPatches.RawRightStickY;
                if (rx == 0f && ry == 0f) return;

                if (Game1.ticks == _lastLoggedTick) return;
                _lastLoggedTick = Game1.ticks;

                int fade = -1;
                try { fade = (int)(_timerUntilMouseFade?.GetValue(null) ?? -1); } catch { /* ignore */ }

                Monitor.Log(
                    $"[RStickDiag] rstick=({rx:0.00},{ry:0.00}) gamepadControls={Game1.options?.gamepadControls} " +
                    $"mouseXY=({Game1.getMouseX()},{Game1.getMouseY()}) transparency={Game1.mouseCursorTransparency:0.00} " +
                    $"timerUntilMouseFade={fade} lastCursorMotionWasMouse={Game1.lastCursorMotionWasMouse} " +
                    $"cursorEnabled={ModEntry.Config?.EnableRightStickCursor}",
                    LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[RStickDiag] error: {ex.Message}", LogLevel.Trace);
            }
        }
    }
}
