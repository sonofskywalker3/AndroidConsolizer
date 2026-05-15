using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// v3.7.5 diagnostic — #35 LoadGameMenu cursor / navigation.
    ///
    /// Two Harmony patches, log only, no behaviour change:
    ///   1. Postfix on LoadGameMenu.draw — snapshot gate state on change
    ///      (capped at MaxStateSnapshots).
    ///   2. Prefix on LoadGameMenu.receiveGamePadButton — log every press
    ///      (capped at MaxButtonLogs).
    ///
    /// Removed in the v3.7.6 fix commit. See
    /// docs/superpowers/specs/2026-05-15-load-game-cursor-design.md.
    /// </summary>
    internal static class LoadGameMenuDiagnosticPatches
    {
        private const int MaxStateSnapshots = 30;
        private const int MaxButtonLogs = 50;

        // -999 is the sentinel returned by ReadIntField when reflection failed
        // or the field can't be read. Chosen so a missing field is visually
        // distinct from a legitimate -1 ("no selection").
        private const int FieldUnavailable = -999;

        private static IMonitor Monitor;
        private static FieldInfo _joypadSelectedItemIndexField;
        private static FieldInfo _currentItemIndexField;

        private static int _stateSnapshotCount;
        private static int _buttonLogCount;
        private static string _lastStateHash;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                _joypadSelectedItemIndexField = AccessTools.Field(typeof(LoadGameMenu), "_joypadSelectedItemIndex");
                if (_joypadSelectedItemIndexField == null)
                {
                    Monitor.Log("[LoadGameDiag] Reflection: _joypadSelectedItemIndex not found.", LogLevel.Warn);
                }

                _currentItemIndexField = AccessTools.Field(typeof(LoadGameMenu), "currentItemIndex");
                if (_currentItemIndexField == null)
                {
                    Monitor.Log("[LoadGameDiag] Reflection: currentItemIndex not found.", LogLevel.Warn);
                }

                var draw = AccessTools.Method(
                    typeof(LoadGameMenu),
                    nameof(LoadGameMenu.draw),
                    new[] { typeof(SpriteBatch) });
                if (draw != null)
                {
                    harmony.Patch(
                        original: draw,
                        postfix: new HarmonyMethod(typeof(LoadGameMenuDiagnosticPatches), nameof(Draw_Postfix))
                    );
                }
                else
                {
                    Monitor.Log("[LoadGameDiag] LoadGameMenu.draw not found — Draw_Postfix patch skipped.", LogLevel.Warn);
                }

                var receiveGamePad = AccessTools.Method(
                    typeof(LoadGameMenu),
                    nameof(LoadGameMenu.receiveGamePadButton),
                    new[] { typeof(Buttons) });
                if (receiveGamePad != null)
                {
                    harmony.Patch(
                        original: receiveGamePad,
                        prefix: new HarmonyMethod(typeof(LoadGameMenuDiagnosticPatches), nameof(ReceiveGamePadButton_Prefix))
                    );
                }
                else
                {
                    Monitor.Log("[LoadGameDiag] LoadGameMenu.receiveGamePadButton not found — prefix patch skipped.", LogLevel.Warn);
                }

                Monitor.Log("[LoadGameDiag] Patches applied (v3.7.5 diagnostic).", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[LoadGameDiag] Failed to apply patches: {ex.Message}", LogLevel.Error);
            }
        }

        private static void Draw_Postfix(LoadGameMenu __instance, SpriteBatch b)
        {
            try
            {
                if (_stateSnapshotCount >= MaxStateSnapshots) return;

                int joypadIdx = ReadIntField(_joypadSelectedItemIndexField, __instance);
                int currentIdx = ReadIntField(_currentItemIndexField, __instance);

                var snapped = __instance.currentlySnappedComponent;
                string snappedDesc = snapped != null
                    ? $"id={snapped.myID},region={snapped.region},bounds=({snapped.bounds.X},{snapped.bounds.Y},{snapped.bounds.Width},{snapped.bounds.Height})"
                    : "null";

                int slotCount = __instance.MenuSlots?.Count ?? -1;
                int mouseX = Game1.getMouseX();
                int mouseY = Game1.getMouseY();

                string hash =
                    $"{Game1.options.snappyMenus}|{Game1.options.gamepadControls}|{Game1.lastCursorMotionWasMouse}"
                    + $"|{Game1.mouseCursorTransparency}|{mouseX}|{mouseY}|{snappedDesc}"
                    + $"|{joypadIdx}|{currentIdx}|{slotCount}";

                if (hash == _lastStateHash) return;
                _lastStateHash = hash;

                Monitor.Log(
                    $"[LoadGameDiag] snapshot={_stateSnapshotCount} "
                    + $"snappy={Game1.options.snappyMenus} gamepad={Game1.options.gamepadControls} "
                    + $"lastMotionMouse={Game1.lastCursorMotionWasMouse} cursorAlpha={Game1.mouseCursorTransparency} "
                    + $"mouse=({mouseX},{mouseY}) snapped={snappedDesc} "
                    + $"_joypadSelectedItemIndex={joypadIdx} currentItemIndex={currentIdx} slotCount={slotCount}",
                    LogLevel.Info);

                _stateSnapshotCount++;
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LoadGameDiag] Draw_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }

        private static void ReceiveGamePadButton_Prefix(LoadGameMenu __instance, Buttons b)
        {
            try
            {
                if (_buttonLogCount >= MaxButtonLogs) return;

                int joypadIdx = ReadIntField(_joypadSelectedItemIndexField, __instance);
                int slotCount = __instance.MenuSlots?.Count ?? -1;

                Monitor.Log(
                    $"[LoadGameDiag] receiveGamePadButton b={b} "
                    + $"_joypadSelectedItemIndex={joypadIdx} slotCount={slotCount}",
                    LogLevel.Info);

                _buttonLogCount++;
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LoadGameDiag] ReceiveGamePadButton_Prefix error: {ex.Message}", LogLevel.Error);
            }
        }

        private static int ReadIntField(FieldInfo field, object instance)
        {
            if (field == null) return FieldUnavailable;
            try
            {
                return (int)field.GetValue(instance);
            }
            catch
            {
                return FieldUnavailable;
            }
        }
    }
}
