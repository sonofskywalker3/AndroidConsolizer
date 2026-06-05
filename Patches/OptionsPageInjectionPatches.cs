using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Injects stripped-but-functional vanilla options back into the Android OptionsPage, hides
    /// touch-only options when a controller is active, and keeps the scroll extent sized to the live
    /// options list (so late-added entries like GMCM's button are reachable). Separate from
    /// OptionsPagePatches, which owns controller navigation of the list.
    /// </summary>
    internal static class OptionsPageInjectionPatches
    {
        private static IMonitor Monitor;

        private static FieldInfo _optionsField;
        private static FieldInfo _scrollAreaField;
        // OptionsElement.ItemHeight is Android-only (absent from the PC DLL) → reflected. Virtual, so
        // GetValue dispatches to each subclass override (Checkbox 72, Slider 122, DropDown 64, etc.).
        private static PropertyInfo _itemHeightProp;

        // MobileScrollbox is Android-only — resolve its members off the runtime object, once.
        private static MethodInfo _msSetMaxYOffset;
        private static FieldInfo _msMaxYOffsetField;
        private static bool _msResolved;

        // ContentHeight constants mirrored from OptionsPage.cs (Y_SPACING=20; scrollArea build:
        // maxYOffset = ContentHeight - (height-16) + 50).
        private const int YSpacing = 20;
        private const int ScrollBottomPad = 16;
        private const int ScrollExtra = 50;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                _optionsField = AccessTools.Field(typeof(OptionsPage), "options");
                _scrollAreaField = AccessTools.Field(typeof(OptionsPage), "scrollArea");
                _itemHeightProp = AccessTools.Property(typeof(OptionsElement), "ItemHeight");

                var update = AccessTools.Method(typeof(OptionsPage), nameof(OptionsPage.update), new[] { typeof(GameTime) });
                if (update != null)
                    harmony.Patch(update, postfix: new HarmonyMethod(typeof(OptionsPageInjectionPatches), nameof(Update_Postfix)));

                Monitor.Log("OptionsPage injection patches applied.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply OptionsPage injection patches: {ex.Message}", LogLevel.Error);
            }
        }

        private static void ResolveScrollboxMembers(object scrollArea)
        {
            if (_msResolved || scrollArea == null) return;
            var t = scrollArea.GetType();
            _msSetMaxYOffset = AccessTools.Method(t, "setMaxYOffset", new[] { typeof(int) });
            _msMaxYOffsetField = AccessTools.Field(t, "maxYOffset");
            _msResolved = true;
        }

        /// <summary>Feature 4: keep the scroll extent sized to the live options list so late-added
        /// entries (GMCM's button) and our injected options are reachable. The ctor bakes maxYOffset
        /// once; entries added/removed after that leave it stale. Only re-applies when the computed
        /// value actually changes (setMaxYOffset resets havePanelScrolled, so don't call it every frame).</summary>
        private static void Update_Postfix(OptionsPage __instance)
        {
            try
            {
                var options = _optionsField?.GetValue(__instance) as List<OptionsElement>;
                if (options == null) return;

                var scrollArea = _scrollAreaField?.GetValue(__instance);
                if (scrollArea == null) return;
                ResolveScrollboxMembers(scrollArea);
                if (_msSetMaxYOffset == null || _msMaxYOffsetField == null) return;

                int contentHeight = YSpacing;
                for (int i = 0; i < options.Count; i++)
                {
                    int ih = (_itemHeightProp != null) ? (int)_itemHeightProp.GetValue(options[i]) : 64;
                    contentHeight += ih + YSpacing;
                }

                int viewportH = __instance.height - ScrollBottomPad;
                int newMax = Math.Max(0, contentHeight - viewportH + ScrollExtra);
                int applied = (newMax == 0) ? 1 : newMax; // MobileScrollbox stores 0 as 1
                int currentMax = (int)_msMaxYOffsetField.GetValue(scrollArea);
                if (applied != currentMax)
                    _msSetMaxYOffset.Invoke(scrollArea, new object[] { newMax });
            }
            catch (Exception ex)
            {
                Monitor?.Log($"OptionsPage scroll-fit error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
