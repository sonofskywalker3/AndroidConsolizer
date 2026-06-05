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
            // No Harmony patch needed for the diagnostic — it polls from OnUpdateTicked.
            // (Registration kept symmetric with the other patch classes.)
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
