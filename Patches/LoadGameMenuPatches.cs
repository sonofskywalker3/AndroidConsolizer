using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// v3.7.8 fix — #35 LoadGameMenu cursor / highlight on slot 0.
    ///
    /// Two complementary mechanisms in a single update postfix, both via
    /// reflection on the private _joypadSelectedItemIndex field:
    ///
    ///   1. One-shot on entry — when _joypadSelectedItemIndex is still -1
    ///      and the async save scan has populated slotButtons, set it to 0
    ///      so slot 0 is highlighted (drawSlotBackground colors slot Wheat
    ///      at _joypadSelectedItemIndex == i, decompile line 899-902). Self-
    ///      resetting via vanilla state — a fresh LoadGameMenu instance
    ///      starts with _joypadSelectedItemIndex = -1, so the gate naturally
    ///      closes after our snap and re-opens on the next instance. No
    ///      static "did we snap" flag needed.
    ///
    ///   2. Auto-sync every tick — if currentlySnappedComponent is a slot
    ///      button (region == 900, set in recalculateSlots line 995) and its
    ///      myID disagrees with _joypadSelectedItemIndex, write the
    ///      component's myID to _joypadSelectedItemIndex. Keeps the
    ///      highlight in step with the cursor regardless of which input
    ///      path moved it (snappy nav OR LoadGameMenu.receiveGamePadButton's
    ///      switch). Eliminates the v3.7.6 desync that occurred when the
    ///      snappy-nav path advanced currentlySnappedComponent without
    ///      _joypadSelectedItemIndex following along.
    ///
    /// What this does NOT do:
    ///   - No call to snapToDefaultClickableComponent — the v3.7.7
    ///     diagnostic confirmed it silently fails when invoked from the
    ///     update postfix (allClickableComponents not populated yet, so
    ///     getComponentWithID(0) returns null and the snap is a no-op).
    ///   - No currentlySnappedComponent manipulation — let vanilla manage
    ///     the cursor; we only sync the highlight.
    ///   - No cursor-draw patch. Touch-entry sets lastCursorMotionWasMouse
    ///     = True, which suppresses drawMouse on entry. The cursor becomes
    ///     visible on the first controller press (vanilla behaviour). If a
    ///     visible-cursor-on-entry fix is wanted later, that's a separate
    ///     LoadGameMenu.draw postfix patch (parallel to #17 v3.7.4 for
    ///     TitleMenu).
    ///
    /// Spec: docs/superpowers/specs/2026-05-15-load-game-cursor-design.md
    /// (Revision 3 — 2026-05-15 / Phase 4).
    /// </summary>
    internal static class LoadGameMenuPatches
    {
        private const int DefaultSlotIndex = 0;
        private const int SlotRegion = 900;
        private const int JoypadIndexUnclaimed = -1;

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
                    Monitor.Log("[LoadGameMenu] Patches applied (v3.7.8 fix).", LogLevel.Trace);
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

        private static void Update_Postfix(LoadGameMenu __instance, GameTime time)
        {
            try
            {
                if (_joypadSelectedItemIndexField == null) return;

                int joypadIdx = (int)_joypadSelectedItemIndexField.GetValue(__instance);

                if (joypadIdx == JoypadIndexUnclaimed
                    && __instance.slotButtons != null
                    && __instance.slotButtons.Count > 0)
                {
                    _joypadSelectedItemIndexField.SetValue(__instance, DefaultSlotIndex);
                    return;
                }

                var snapped = __instance.currentlySnappedComponent;
                if (snapped != null && snapped.region == SlotRegion && snapped.myID != joypadIdx)
                {
                    _joypadSelectedItemIndexField.SetValue(__instance, snapped.myID);
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LoadGameMenu] Update_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
