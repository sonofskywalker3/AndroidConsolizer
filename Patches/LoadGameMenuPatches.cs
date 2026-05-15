using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// v3.7.9 fix — #35 LoadGameMenu cursor + highlight on slot 0.
    ///
    /// Two complementary mechanisms in a single update postfix:
    ///
    ///   1. Entry one-shot — when _joypadSelectedItemIndex is still -1
    ///      and the async save scan has populated slotButtons:
    ///        - Set _joypadSelectedItemIndex = 0 (slot 0 highlighted).
    ///        - Assign currentlySnappedComponent = slotButtons[0] directly,
    ///          bypassing snapToDefaultClickableComponent → getComponentWithID
    ///          which returns null when called from update postfix
    ///          (allClickableComponents is populated lazily on first nav
    ///          input, not at menu construction — confirmed by v3.7.7
    ///          diagnostic snapshot 2 which showed snapped=null after
    ///          weSnapped=True).
    ///        - Call snapCursorToCurrentSnappedComponent() to move the
    ///          mouse to slot 0's bounds.Center. Now non-null → actually
    ///          moves.
    ///      Self-resetting via _joypadSelectedItemIndex == -1: a fresh
    ///      LoadGameMenu instance starts with -1, our snap sets it to 0,
    ///      gate closes for that instance, re-opens on the next.
    ///
    ///   2. Auto-sync every tick — if currentlySnappedComponent is a slot
    ///      button (region == 900, set in recalculateSlots line 995) and
    ///      its myID disagrees with _joypadSelectedItemIndex, write the
    ///      component's myID to _joypadSelectedItemIndex. Keeps the
    ///      highlight in step with the cursor regardless of which input
    ///      path moved it (snappy nav OR LoadGameMenu.receiveGamePadButton's
    ///      switch).
    ///
    /// Why moving the cursor on entry matters: Android's touch-sim layer
    /// routes A presses through the cursor coordinates, not through
    /// _joypadSelectedItemIndex. v3.7.8 left the cursor at the touch-tap
    /// position from the title screen, which on G Cloud overlapped a
    /// slot's right-side delete button — A presses fired receiveLeftClick
    /// at the cursor coords and triggered the delete confirmation, not
    /// the load. Cursor *position* on entry is load-bearing.
    ///
    /// Not delivered: visible cursor on entry. Touch-tap entry sets
    /// lastCursorMotionWasMouse = True which suppresses drawMouse;
    /// cursor becomes visible on the first controller press (vanilla
    /// behaviour). If wanted later, that's a separate LoadGameMenu.draw
    /// postfix parallel to #17 v3.7.4 for TitleMenu.
    ///
    /// v3.7.10 adds a third mechanism: scroll-offset clamp. Vanilla
    /// LoadGameMenu.receiveGamePadButton has an asymmetric scroll bug —
    /// DPadDown only scrolls when the index is in the middle of the list
    /// (decompile line 543), but DPadUp unconditionally scrolls (line 533),
    /// missing the matching boundary check. With N <= itemsPerPage, DPadUp
    /// from any index >= 1 scrolls the list off the top with the cursor
    /// stranded at absolute pixel coords. The clamp resets any out-of-range
    /// scroll offset and re-snaps the cursor to its slot bounds.
    ///
    /// Spec: docs/superpowers/specs/2026-05-15-load-game-cursor-design.md
    /// (Revision 5 — 2026-05-15 / Phase 6).
    /// </summary>
    internal static class LoadGameMenuPatches
    {
        private const int DefaultSlotIndex = 0;
        private const int SlotRegion = 900;
        private const int JoypadIndexUnclaimed = -1;
        private const int ItemHeight = 200;

        private static IMonitor Monitor;
        private static FieldInfo _joypadSelectedItemIndexField;
        private static FieldInfo _scrollAreaField;
        private static FieldInfo _itemsPerPageField;
        private static MethodInfo _getYOffsetForScrollMethod;
        private static MethodInfo _setYOffsetForScrollMethod;

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

                // scrollArea, itemsPerPage, and the scroll-offset accessors only exist on the
                // Android LoadGameMenu (decompile lines 238, 264); the PC DLL we compile
                // against doesn't have them. Reflect.
                _scrollAreaField = AccessTools.Field(typeof(LoadGameMenu), "scrollArea");
                _itemsPerPageField = AccessTools.Field(typeof(LoadGameMenu), "itemsPerPage");
                if (_scrollAreaField == null || _itemsPerPageField == null)
                {
                    Monitor.Log("[LoadGameMenu] Reflection: scrollArea or itemsPerPage not found — scroll clamp disabled.", LogLevel.Warn);
                }
                else
                {
                    var scrollAreaType = _scrollAreaField.FieldType;
                    _getYOffsetForScrollMethod = AccessTools.Method(scrollAreaType, "getYOffsetForScroll");
                    _setYOffsetForScrollMethod = AccessTools.Method(scrollAreaType, "setYOffsetForScroll", new[] { typeof(int) });
                    if (_getYOffsetForScrollMethod == null || _setYOffsetForScrollMethod == null)
                    {
                        Monitor.Log($"[LoadGameMenu] Reflection: scrollArea methods not found on {scrollAreaType.FullName} — scroll clamp disabled.", LogLevel.Warn);
                    }
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
                    Monitor.Log("[LoadGameMenu] Patches applied (v3.7.10 fix).", LogLevel.Trace);
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
                    var slot0 = __instance.slotButtons[0];
                    if (slot0 == null) return;
                    _joypadSelectedItemIndexField.SetValue(__instance, DefaultSlotIndex);
                    __instance.currentlySnappedComponent = slot0;
                    __instance.snapCursorToCurrentSnappedComponent();
                    return;
                }

                var snapped = __instance.currentlySnappedComponent;
                if (snapped != null && snapped.region == SlotRegion && snapped.myID != joypadIdx)
                {
                    _joypadSelectedItemIndexField.SetValue(__instance, snapped.myID);
                }

                ClampScrollOffset(__instance);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LoadGameMenu] Update_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Clamps scrollArea's Y offset to the valid range and re-snaps the
        /// cursor if a clamp was needed. Fixes the vanilla DPadUp scroll
        /// overshoot — see class doc for details.
        /// </summary>
        private static void ClampScrollOffset(LoadGameMenu __instance)
        {
            if (__instance.MenuSlots == null) return;
            if (_scrollAreaField == null
                || _itemsPerPageField == null
                || _getYOffsetForScrollMethod == null
                || _setYOffsetForScrollMethod == null) return;

            object scrollArea = _scrollAreaField.GetValue(__instance);
            if (scrollArea == null) return;

            int slotCount = __instance.MenuSlots.Count;
            int itemsPerPage = (int)_itemsPerPageField.GetValue(__instance);
            int maxScrollMagnitude = Math.Max(0, (slotCount - itemsPerPage) * ItemHeight);

            int currentOffset = (int)_getYOffsetForScrollMethod.Invoke(scrollArea, null);
            int clampedOffset = Math.Max(-maxScrollMagnitude, Math.Min(0, currentOffset));

            if (clampedOffset == currentOffset) return;

            _setYOffsetForScrollMethod.Invoke(scrollArea, new object[] { clampedOffset });
            if (__instance.currentlySnappedComponent != null)
            {
                __instance.snapCursorToCurrentSnappedComponent();
            }
        }
    }
}
