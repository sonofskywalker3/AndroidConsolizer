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
        private static FieldInfo _adjustControlsField;
        // OptionsElement.ItemHeight is Android-only (absent from the PC DLL) → reflected. Virtual, so
        // GetValue dispatches to each subclass override (Checkbox 72, Slider 122, DropDown 64, etc.).
        private static PropertyInfo _itemHeightProp;

        // Touch-only options to hide when a controller is active (decided with user): 139 Controls
        // dropdown, 140 show-on-screen-controls toggle, 146 invisible-button width, 147 pinch-zoom.
        // The Adjust-joypad-controls OptionsButton is matched by reference (optionsButtonAdjustControls).
        private static readonly int[] TouchOptionIds = { 139, 140, 146, 147 };

        // MobileScrollbox is Android-only — resolve its members off the runtime object, once.
        private static MethodInfo _msSetMaxYOffset;
        private static FieldInfo _msMaxYOffsetField;
        private static bool _msResolved;

        // ContentHeight constants mirrored from OptionsPage.cs (Y_SPACING=20; scrollArea build:
        // maxYOffset = ContentHeight - (height-16) + 50).
        private const int YSpacing = 20;
        private const int ScrollBottomPad = 16;
        private const int ScrollExtra = 50;

        // Hardcoded labels — the PC string keys (Options_AlwaysShowToolHitLocation etc.) are absent
        // from the Android content, so LoadString won't resolve them.
        private const string LabelAlwaysShowToolHit = "Always show tool hit location";
        private const string LabelHideToolHitMoving = "Hide tool hit location while moving";

        private const int OptAlwaysShowToolHit = 11;
        private const int OptHideToolHitMoving = 12;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                _optionsField = AccessTools.Field(typeof(OptionsPage), "options");
                _scrollAreaField = AccessTools.Field(typeof(OptionsPage), "scrollArea");
                _adjustControlsField = AccessTools.Field(typeof(OptionsPage), "optionsButtonAdjustControls");
                _itemHeightProp = AccessTools.Property(typeof(OptionsElement), "ItemHeight");

                var update = AccessTools.Method(typeof(OptionsPage), nameof(OptionsPage.update), new[] { typeof(GameTime) });
                if (update != null)
                    harmony.Patch(update, postfix: new HarmonyMethod(typeof(OptionsPageInjectionPatches), nameof(Update_Postfix)));

                var ctor = AccessTools.Constructor(typeof(OptionsPage),
                    new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(float), typeof(float) });
                if (ctor != null)
                    harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(OptionsPageInjectionPatches), nameof(Ctor_Postfix)));
                else
                    Monitor.Log("OptionsPageInjectionPatches: OptionsPage ctor not found — injection disabled", LogLevel.Warn);

                Monitor.Log("OptionsPage injection patches applied.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply OptionsPage injection patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Append injected options to the OptionsPage list. They self-sync their displayed
        /// value because OptionsCheckbox/OptionsSlider ctors call set*ToProperValue, and Options handles
        /// 11/12/18 there. The ctor's updateContentPositions already ran, so positions are set on the
        /// next update tick (before first draw); the scroll-fit postfix resizes the scroll to include
        /// these. Appended at the end (an "extra options" block).</summary>
        private static void Ctor_Postfix(OptionsPage __instance)
        {
            try
            {
                var options = _optionsField?.GetValue(__instance) as List<OptionsElement>;
                if (options == null) return;

                // Hide touch-only options when a controller is active. gamepadControls (not
                // GamePad.IsConnected) matches the rest of OptionsPagePatches and sidesteps the Ayaneo
                // IsConnected=false quirk. Removing entries relies on the scroll-fit postfix to re-size
                // the list — already in place.
                if ((ModEntry.Config?.HideTouchOptionsWithController ?? false) && Game1.options.gamepadControls)
                {
                    var adjustBtn = _adjustControlsField?.GetValue(__instance) as OptionsElement;
                    options.RemoveAll(o =>
                        Array.IndexOf(TouchOptionIds, o.whichOption) >= 0
                        || (adjustBtn != null && ReferenceEquals(o, adjustBtn)));
                }

                options.Add(new OptionsCheckbox(LabelAlwaysShowToolHit, OptAlwaysShowToolHit));
                options.Add(new OptionsCheckbox(LabelHideToolHitMoving, OptHideToolHitMoving));
            }
            catch (Exception ex)
            {
                Monitor?.Log($"OptionsPage Ctor_Postfix error: {ex.Message}", LogLevel.Error);
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
