using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// v3.7.6 fix — #35 LoadGameMenu cursor on slot 0.
    ///
    /// On Android, opening LoadGameMenu via touch leaves the cursor at the
    /// touch position (typically the Load Game button area, well below the
    /// save list). The user has to press DPad once to jog it onto slot 0,
    /// which is both an extra step and a visual surprise — slot 0 is "above"
    /// the touch position, but DPadUp on _joypadSelectedItemIndex == -1
    /// snaps to slot 0 rather than scrolling.
    ///
    /// "Fix the data" approach: when the menu opens and the async save scan
    /// completes, set _joypadSelectedItemIndex = 0 (private field, reflection)
    /// and call the game's own snapToDefaultClickableComponent(), which then
    /// reads the index, sets currentlySnappedComponent = slotButtons[0], and
    /// calls Game1.setMousePosition(slot.bounds.Center).
    ///
    /// One-shot per fresh LoadGameMenu instance is enforced solely by
    /// `currentlySnappedComponent == null` — once we snap, that field is
    /// non-null and the postfix returns early on every subsequent tick. A
    /// new LoadGameMenu construction resets it back to null and the snap
    /// fires again, so no static "did we snap" state is needed.
    ///
    /// The `slotButtons.Count > 0` check is NOT what enforces one-shot
    /// behaviour — `recalculateSlots()` rebuilds slotButtons every tick
    /// (decompile LoadGameMenu.cs:814) so it's true on every frame after
    /// the saves load. It's a crash guard for the brief pre-load window
    /// when slotButtons is still empty: snapToDefaultClickableComponent()
    /// → getComponentWithID(0) would return null and the snap would silently
    /// no-op, but it's cheaper and clearer to skip the call entirely.
    ///
    /// Spec: docs/superpowers/specs/2026-05-15-load-game-cursor-design.md
    /// (Revision — 2026-05-15 / Phase 2).
    /// </summary>
    internal static class LoadGameMenuPatches
    {
        private static IMonitor Monitor;
        private static FieldInfo _joypadSelectedItemIndexField;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                _joypadSelectedItemIndexField = AccessTools.Field(typeof(LoadGameMenu), "_joypadSelectedItemIndex");
                if (_joypadSelectedItemIndexField == null)
                {
                    Monitor.Log("[LoadGameMenu] Reflection: _joypadSelectedItemIndex not found — postfix will no-op.", LogLevel.Warn);
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
                    Monitor.Log("[LoadGameMenu] Patches applied (v3.7.6 fix).", LogLevel.Trace);
                }
                else
                {
                    Monitor.Log("[LoadGameMenu] LoadGameMenu.update not found — patch skipped.", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[LoadGameMenu] Failed to apply patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Postfix on LoadGameMenu.update — when the menu is freshly open and
        /// the async save scan has populated slotButtons, snap the cursor to
        /// slot 0 by setting _joypadSelectedItemIndex and calling vanilla
        /// snapToDefaultClickableComponent().
        /// </summary>
        private static void Update_Postfix(LoadGameMenu __instance, GameTime time)
        {
            try
            {
                if (__instance.currentlySnappedComponent != null) return;
                if (__instance.slotButtons == null || __instance.slotButtons.Count == 0) return;
                if (_joypadSelectedItemIndexField == null) return;

                _joypadSelectedItemIndexField.SetValue(__instance, 0);
                __instance.snapToDefaultClickableComponent();
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LoadGameMenu] Update_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
