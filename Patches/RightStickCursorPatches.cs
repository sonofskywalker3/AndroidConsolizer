using System;
using System.Reflection;
using HarmonyLib;
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
            // No Harmony patch needed — the cursor is engine-native; we only nudge one flag
            // per tick (EnforceTick) and log a diagnostic. Both run from OnUpdateTicked.
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
