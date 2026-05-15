using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// v3.7.6 fix + v3.7.7 diagnostic — #35 LoadGameMenu cursor on slot 0.
    ///
    /// Fix (v3.7.6, retained): On a fresh LoadGameMenu instance once the
    /// async save scan completes, set _joypadSelectedItemIndex = 0 and call
    /// snapToDefaultClickableComponent() so slot 0 is highlighted and the
    /// cursor sits on it.
    ///
    /// Diagnostic (v3.7.7, to be removed in v3.7.8): The v3.7.6 fix was
    /// only partially correct on G Cloud — slot 0 highlights on entry but
    /// (a) the cursor isn't visible and (b) DPadDown moves the cursor to
    /// slot 1 yet leaves slot 0 highlighted (the highlight and cursor
    /// state systems desync). Hypothesis: with currentlySnappedComponent
    /// pre-set, snappy nav consumes DPadDown and LoadGameMenu's
    /// receiveGamePadButton (which is what advances _joypadSelectedItemIndex)
    /// is never called. Two log patches confirm or refute this:
    ///
    ///   1. Update_Postfix logs gate state on change (snappy, gamepad,
    ///      lastMotionMouse, cursorAlpha, mouse XY, snapped, _joypadIdx,
    ///      currentItemIndex, slotCount, weSnapped) — capped at 30 unique
    ///      snapshots.
    ///   2. ReceiveGamePadButton_Prefix logs every button press + state —
    ///      capped at 50. Its presence/absence for DPadDown answers the
    ///      dispatch question.
    ///
    /// Spec: docs/superpowers/specs/2026-05-15-load-game-cursor-design.md
    /// (Revision 2 — 2026-05-15 / Phase 3).
    /// </summary>
    internal static class LoadGameMenuPatches
    {
        private const int MaxStateSnapshots = 30;
        private const int MaxButtonLogs = 50;
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
                    Monitor.Log("[LoadGameMenu] Reflection: _joypadSelectedItemIndex not found — fix postfix will no-op, diagnostic will report -999.", LogLevel.Warn);
                }

                _currentItemIndexField = AccessTools.Field(typeof(LoadGameMenu), "currentItemIndex");
                if (_currentItemIndexField == null)
                {
                    Monitor.Log("[LoadGameMenu] Reflection: currentItemIndex not found — diagnostic will report -999.", LogLevel.Warn);
                }

                var update = AccessTools.Method(
                    typeof(LoadGameMenu),
                    nameof(LoadGameMenu.update),
                    new[] { typeof(GameTime) });
                if (update != null)
                {
                    harmony.Patch(
                        original: update,
                        postfix: new HarmonyMethod(typeof(LoadGameMenuPatches), nameof(Update_Postfix))
                    );
                }
                else
                {
                    Monitor.Log("[LoadGameMenu] LoadGameMenu.update not found — Update_Postfix skipped.", LogLevel.Warn);
                }

                var receiveGamePad = AccessTools.Method(
                    typeof(LoadGameMenu),
                    nameof(LoadGameMenu.receiveGamePadButton),
                    new[] { typeof(Buttons) });
                if (receiveGamePad != null)
                {
                    harmony.Patch(
                        original: receiveGamePad,
                        prefix: new HarmonyMethod(typeof(LoadGameMenuPatches), nameof(ReceiveGamePadButton_Prefix))
                    );
                }
                else
                {
                    Monitor.Log("[LoadGameMenu] LoadGameMenu.receiveGamePadButton not found — diagnostic prefix skipped.", LogLevel.Warn);
                }

                Monitor.Log("[LoadGameMenu] Patches applied (v3.7.7 fix + diagnostic).", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[LoadGameMenu] Failed to apply patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Fix path: snap _joypadSelectedItemIndex = 0 + currentlySnappedComponent
        /// = slot 0 on first update with slots loaded.
        /// Diagnostic path: log gate state on change.
        /// </summary>
        private static void Update_Postfix(LoadGameMenu __instance, GameTime time)
        {
            bool weSnapped = false;
            try
            {
                bool canSnap =
                    __instance.currentlySnappedComponent == null
                    && __instance.slotButtons != null
                    && __instance.slotButtons.Count > 0
                    && _joypadSelectedItemIndexField != null;

                if (canSnap)
                {
                    _joypadSelectedItemIndexField.SetValue(__instance, 0);
                    __instance.snapToDefaultClickableComponent();
                    weSnapped = true;
                }

                LogStateOnChange(__instance, weSnapped);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LoadGameMenu] Update_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Diagnostic prefix: logs every gamepad button reaching LoadGameMenu.
        /// Absence of an entry for a press means the input was consumed
        /// upstream (likely by IClickableMenu snappy nav).
        /// </summary>
        private static void ReceiveGamePadButton_Prefix(LoadGameMenu __instance, Buttons b)
        {
            try
            {
                if (_buttonLogCount >= MaxButtonLogs) return;

                int joypadIdx = ReadIntField(_joypadSelectedItemIndexField, __instance);
                int slotCount = __instance.MenuSlots?.Count ?? -1;
                var snapped = __instance.currentlySnappedComponent;
                string snappedDesc = snapped != null
                    ? $"id={snapped.myID},region={snapped.region}"
                    : "null";

                Monitor.Log(
                    $"[LoadGameDiag] receiveGamePadButton b={b} "
                    + $"_joypadSelectedItemIndex={joypadIdx} snapped={snappedDesc} slotCount={slotCount}",
                    LogLevel.Info);

                _buttonLogCount++;
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LoadGameMenu] ReceiveGamePadButton_Prefix error: {ex.Message}", LogLevel.Error);
            }
        }

        private static void LogStateOnChange(LoadGameMenu __instance, bool weSnapped)
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
                + $"|{joypadIdx}|{currentIdx}|{slotCount}|{weSnapped}";

            if (hash == _lastStateHash) return;
            _lastStateHash = hash;

            Monitor.Log(
                $"[LoadGameDiag] snapshot={_stateSnapshotCount} "
                + $"snappy={Game1.options.snappyMenus} gamepad={Game1.options.gamepadControls} "
                + $"lastMotionMouse={Game1.lastCursorMotionWasMouse} cursorAlpha={Game1.mouseCursorTransparency} "
                + $"mouse=({mouseX},{mouseY}) snapped={snappedDesc} "
                + $"_joypadSelectedItemIndex={joypadIdx} currentItemIndex={currentIdx} slotCount={slotCount} "
                + $"weSnapped={weSnapped}",
                LogLevel.Info);

            _stateSnapshotCount++;
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
