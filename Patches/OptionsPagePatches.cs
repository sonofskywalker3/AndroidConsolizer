using System;
using System.Collections.Generic;
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
    /// Harmony patches for OptionsPage — adds full controller navigation to the Options tab.
    ///
    /// Interaction model:
    /// - D-pad Up/Down navigates between interactive options (skips labels/spacers)
    /// - D-pad Left/Right adjusts values via direct reflection (snappyMenus=false on Android
    ///   so receiveKeyPress doesn't work): sliders +/-10, dropdown cycling, plusminus +/-1
    /// - A toggles checkboxes and activates buttons; blocks touch-sim click via same-tick guard
    /// - B closes the menu (passed to base)
    /// - Right stick scrolls the options list
    /// - Left thumbstick is suppressed at GetState level (no free-roaming cursor)
    /// </summary>
    internal static class OptionsPagePatches
    {
        private static IMonitor Monitor;

        // State
        private static int _focusedIndex = -1;
        private static List<int> _interactiveIndices;
        private static int _aPressTick = -1; // Same-tick guard: blocks touch-sim receiveLeftClick

        // Cached reflection — OptionsPage fields
        private static FieldInfo _optionsField;
        private static FieldInfo _scrollAreaField;

        // Cached reflection — OptionsButton
        private static MethodInfo _buttonReleaseLeftClick;

        // Cached reflection — OptionsSlider fields
        private static PropertyInfo _sliderValueProp;
        private static FieldInfo _sliderValueField;
        private static FieldInfo _sliderMinField;
        private static FieldInfo _sliderMaxField;

        // Cached reflection — OptionsDropDown fields
        private static FieldInfo _ddSelectedOptionField;
        private static FieldInfo _ddDropDownOptionsField;

        // Cached reflection — OptionsPlusMinus fields
        private static FieldInfo _pmMinusButtonField;
        private static FieldInfo _pmPlusButtonField;

        // Constants
        private const float StickScrollThreshold = 0.2f;

        /// <summary>Whether the left thumbstick should be suppressed at GetState level.
        /// When true, GameplayButtonPatches.GetState_Postfix zeros the left stick so the
        /// cursor doesn't free-roam — D-pad handles option-to-option navigation instead.</summary>
        public static bool ShouldSuppressLeftStick()
        {
            if (!ModEntry.Config.EnableGameMenuNavigation || !Game1.options.gamepadControls)
                return false;
            if (Game1.activeClickableMenu is GameMenu gm)
            {
                var currentPage = gm.GetCurrentPage();
                return currentPage is OptionsPage;
            }
            return false;
        }

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                // Cache OptionsPage reflection fields
                _optionsField = AccessTools.Field(typeof(OptionsPage), "options");
                _scrollAreaField = AccessTools.Field(typeof(OptionsPage), "scrollArea");

                if (_optionsField == null)
                    Monitor.Log("OptionsPagePatches: 'options' field not found", LogLevel.Warn);
                if (_scrollAreaField == null)
                    Monitor.Log("OptionsPagePatches: 'scrollArea' field not found", LogLevel.Warn);

                // Cache OptionsButton reflection
                _buttonReleaseLeftClick = AccessTools.Method(typeof(OptionsButton), "releaseLeftClick",
                    new[] { typeof(int), typeof(int) })
                    ?? AccessTools.Method(typeof(OptionsButton), "leftClickReleased",
                        new[] { typeof(int), typeof(int) });

                // Cache OptionsSlider reflection
                _sliderValueProp = AccessTools.Property(typeof(OptionsSlider), "value");
                _sliderValueField = AccessTools.Field(typeof(OptionsSlider), "value")
                                 ?? AccessTools.Field(typeof(OptionsSlider), "_value");
                _sliderMinField = AccessTools.Field(typeof(OptionsSlider), "sliderMinValue");
                _sliderMaxField = AccessTools.Field(typeof(OptionsSlider), "sliderMaxValue");

                // Cache OptionsDropDown reflection
                _ddSelectedOptionField = AccessTools.Field(typeof(OptionsDropDown), "selectedOption");
                _ddDropDownOptionsField = AccessTools.Field(typeof(OptionsDropDown), "dropDownOptions");

                // Cache OptionsPlusMinus reflection
                _pmMinusButtonField = AccessTools.Field(typeof(OptionsPlusMinus), "minusButton");
                _pmPlusButtonField = AccessTools.Field(typeof(OptionsPlusMinus), "plusButton");

                // Patch receiveGamePadButton on IClickableMenu (OptionsPage doesn't override it).
                // Prefix checks __instance is OptionsPage so it only fires for the Options tab.
                var receiveGPB = AccessTools.Method(typeof(IClickableMenu), nameof(IClickableMenu.receiveGamePadButton));
                if (receiveGPB != null)
                {
                    harmony.Patch(
                        original: receiveGPB,
                        prefix: new HarmonyMethod(typeof(OptionsPagePatches), nameof(ReceiveGamePadButton_Prefix))
                    );
                }

                // Patch receiveLeftClick on OptionsPage — same-tick guard blocks touch-sim
                // clicks after our A-press handling to prevent double-firing.
                var receiveLeftClick = AccessTools.Method(typeof(OptionsPage), nameof(OptionsPage.receiveLeftClick),
                    new[] { typeof(int), typeof(int), typeof(bool) });
                if (receiveLeftClick != null)
                {
                    harmony.Patch(
                        original: receiveLeftClick,
                        prefix: new HarmonyMethod(typeof(OptionsPagePatches), nameof(ReceiveLeftClick_Prefix))
                    );
                }

                // Patch update
                var updateMethod = AccessTools.Method(typeof(OptionsPage), nameof(OptionsPage.update), new[] { typeof(GameTime) });
                if (updateMethod != null)
                {
                    harmony.Patch(
                        original: updateMethod,
                        postfix: new HarmonyMethod(typeof(OptionsPagePatches), nameof(Update_Postfix))
                    );
                }

                Monitor.Log("OptionsPage patches applied.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply OptionsPage patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Reset state when leaving the options tab or closing the GameMenu.</summary>
        public static void OnOptionsPageClosed()
        {
            _focusedIndex = -1;
            _interactiveIndices = null;
            _aPressTick = -1;
        }

        /// <summary>Snap the mouse cursor to the focused option so the game's cursor shows focus.</summary>
        private static void SnapCursorToFocused(List<OptionsElement> options)
        {
            if (_focusedIndex < 0 || _focusedIndex >= options.Count)
                return;
            var bounds = options[_focusedIndex].bounds;
            Game1.setMousePosition(bounds.Left + 48, bounds.Center.Y);
        }

        /// <summary>Build the list of interactive option indices, skipping labels and spacers.</summary>
        private static void BuildInteractiveIndices(List<OptionsElement> options)
        {
            _interactiveIndices = new List<int>();
            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                // OptionsButton has whichOption == -1 but IS interactive
                if (opt is OptionsButton)
                {
                    _interactiveIndices.Add(i);
                    continue;
                }
                // Skip bare OptionsElement labels/spacers (whichOption == -1)
                if (opt.whichOption == -1)
                    continue;
                // Everything else with a valid whichOption is interactive
                _interactiveIndices.Add(i);
            }
        }

        /// <summary>Scroll the options list to ensure the focused option is visible.</summary>
        private static void EnsureVisible(OptionsPage page, List<OptionsElement> options, object scrollArea)
        {
            if (_focusedIndex < 0 || _focusedIndex >= options.Count || scrollArea == null)
                return;

            try
            {
                var scrollType = scrollArea.GetType();
                var scissorField = AccessTools.Field(scrollType, "scissorRectangle");
                var getYOffset = AccessTools.Method(scrollType, "getYOffsetForScroll");
                var setYOffset = AccessTools.Method(scrollType, "setYOffsetForScroll", new[] { typeof(int) });
                var maxYOffsetField = AccessTools.Field(scrollType, "maxYOffset");

                if (scissorField == null || getYOffset == null || setYOffset == null || maxYOffsetField == null)
                    return;

                var scissor = (Rectangle)scissorField.GetValue(scrollArea);
                int yOffset = (int)getYOffset.Invoke(scrollArea, null);
                int maxYOffset = (int)maxYOffsetField.GetValue(scrollArea);

                var bounds = options[_focusedIndex].bounds;
                int padding = 20;

                if (bounds.Y < scissor.Y)
                {
                    int newOffset = yOffset + (scissor.Y - bounds.Y) + padding;
                    newOffset = Math.Min(0, newOffset);
                    setYOffset.Invoke(scrollArea, new object[] { newOffset });
                }
                else if (bounds.Y + bounds.Height > scissor.Bottom)
                {
                    int newOffset = yOffset - (bounds.Y + bounds.Height - scissor.Bottom) - padding;
                    newOffset = Math.Max(-maxYOffset, newOffset);
                    setYOffset.Invoke(scrollArea, new object[] { newOffset });
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[OptionsPage] EnsureVisible error: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Find the position of the current focused index in _interactiveIndices.</summary>
        private static int FindCurrentPosition()
        {
            if (_interactiveIndices == null || _interactiveIndices.Count == 0)
                return -1;
            return _interactiveIndices.IndexOf(_focusedIndex);
        }

        /// <summary>
        /// Prefix on OptionsPage.receiveLeftClick — blocks touch-sim clicks on the same tick
        /// as our A-press handling. Without this, pressing A would toggle a checkbox then the
        /// touch-sim click would toggle it back (double-fire, net no change).
        /// </summary>
        private static bool ReceiveLeftClick_Prefix(OptionsPage __instance, int x, int y)
        {
            if (!ModEntry.Config.EnableGameMenuNavigation || !Game1.options.gamepadControls)
                return true;
            if (_aPressTick == Game1.ticks)
            {
                _aPressTick = -1;
                return false; // Block touch-sim click
            }
            return true;
        }

        /// <summary>
        /// Prefix on IClickableMenu.receiveGamePadButton — handles D-pad navigation,
        /// A-button interaction, and left/right value adjustment.
        /// </summary>
        private static bool ReceiveGamePadButton_Prefix(IClickableMenu __instance, Buttons b)
        {
            if (!(__instance is OptionsPage page))
                return true;
            if (!ModEntry.Config.EnableGameMenuNavigation)
                return true;
            if (!Game1.options.gamepadControls)
                return true;

            var options = _optionsField?.GetValue(page) as List<OptionsElement>;
            if (options == null || options.Count == 0)
                return true;

            if (_interactiveIndices == null)
                BuildInteractiveIndices(options);
            if (_interactiveIndices.Count == 0)
                return true;

            // Initialize focus if needed
            if (_focusedIndex < 0)
                _focusedIndex = _interactiveIndices[0];

            var scrollArea = _scrollAreaField?.GetValue(page);

            // B button: let base handle (close menu)
            if (b == Buttons.B)
                return true;

            // D-pad Up — navigate to previous interactive option
            if (b == Buttons.DPadUp || b == Buttons.LeftThumbstickUp)
            {
                int pos = FindCurrentPosition();
                if (pos <= 0)
                    pos = _interactiveIndices.Count; // wrap
                _focusedIndex = _interactiveIndices[pos - 1];
                EnsureVisible(page, options, scrollArea);
                SnapCursorToFocused(options);
                Game1.playSound("shiny4");
                return false;
            }

            // D-pad Down — navigate to next interactive option
            if (b == Buttons.DPadDown || b == Buttons.LeftThumbstickDown)
            {
                int pos = FindCurrentPosition();
                if (pos < 0 || pos >= _interactiveIndices.Count - 1)
                    pos = -1; // wrap
                _focusedIndex = _interactiveIndices[pos + 1];
                EnsureVisible(page, options, scrollArea);
                SnapCursorToFocused(options);
                Game1.playSound("shiny4");
                return false;
            }

            // A button — interact with focused option
            if (b == Buttons.A)
            {
                if (_focusedIndex < 0 || _focusedIndex >= options.Count)
                    return false;

                var opt = options[_focusedIndex];

                // Set same-tick guard to block the touch-sim click that follows
                _aPressTick = Game1.ticks;

                if (opt is OptionsCheckbox)
                {
                    // OptionsCheckbox.receiveLeftClick has no hit-testing — any coords toggle it
                    opt.receiveLeftClick(opt.bounds.Center.X, opt.bounds.Center.Y);
                    return false;
                }

                if (opt is OptionsButton button)
                {
                    button.receiveLeftClick(button.bounds.Center.X, button.bounds.Center.Y);
                    _buttonReleaseLeftClick?.Invoke(button, new object[] { button.bounds.Center.X, button.bounds.Center.Y });
                    return false;
                }

                // Sliders, dropdowns, plus/minus: A does nothing (use Left/Right to adjust)
                return false;
            }

            // D-pad Left/Right — adjust value directly via reflection.
            // Can't use receiveKeyPress because it requires snappyMenus=true (false on Android).
            if (b == Buttons.DPadLeft || b == Buttons.LeftThumbstickLeft ||
                b == Buttons.DPadRight || b == Buttons.LeftThumbstickRight)
            {
                if (_focusedIndex < 0 || _focusedIndex >= options.Count)
                    return false;

                var opt = options[_focusedIndex];
                bool isRight = (b == Buttons.DPadRight || b == Buttons.LeftThumbstickRight);

                if (opt is OptionsSlider slider)
                {
                    int curVal = _sliderValueProp != null ? (int)_sliderValueProp.GetValue(slider)
                               : _sliderValueField != null ? (int)_sliderValueField.GetValue(slider) : 0;
                    int minVal = _sliderMinField != null ? (int)_sliderMinField.GetValue(slider) : 0;
                    int maxVal = _sliderMaxField != null ? (int)_sliderMaxField.GetValue(slider) : 100;
                    int newVal = isRight ? Math.Min(curVal + 10, maxVal) : Math.Max(curVal - 10, minVal);
                    if (_sliderValueProp != null) _sliderValueProp.SetValue(slider, newVal);
                    else _sliderValueField?.SetValue(slider, newVal);
                    Game1.options.changeSliderOption(slider.whichOption, newVal);
                    Game1.playSound("shiny4");
                    return false;
                }

                if (opt is OptionsDropDown dropdown)
                {
                    if (_ddSelectedOptionField != null && _ddDropDownOptionsField != null)
                    {
                        var ddOptions = _ddDropDownOptionsField.GetValue(dropdown) as List<string>;
                        if (ddOptions != null && ddOptions.Count > 0)
                        {
                            int sel = (int)_ddSelectedOptionField.GetValue(dropdown);
                            sel = isRight ? (sel + 1) % ddOptions.Count
                                         : (sel - 1 + ddOptions.Count) % ddOptions.Count;
                            _ddSelectedOptionField.SetValue(dropdown, sel);
                            Game1.options.changeDropDownOption(dropdown.whichOption, ddOptions[sel]);
                        }
                    }
                    Game1.playSound("shiny4");
                    return false;
                }

                if (opt is OptionsPlusMinus plusMinus)
                {
                    if (_pmMinusButtonField != null && _pmPlusButtonField != null)
                    {
                        var targetRect = isRight
                            ? (Rectangle)_pmPlusButtonField.GetValue(plusMinus)
                            : (Rectangle)_pmMinusButtonField.GetValue(plusMinus);
                        plusMinus.receiveLeftClick(targetRect.Center.X, targetRect.Center.Y);
                    }
                    return false;
                }

                // For other types, consume the input (don't let it navigate tabs)
                return false;
            }

            // All other buttons: let base handle
            return true;
        }

        /// <summary>
        /// Postfix on OptionsPage.update — handles right stick scrolling and focus initialization.
        /// </summary>
        private static void Update_Postfix(OptionsPage __instance, GameTime time)
        {
            if (!ModEntry.Config.EnableGameMenuNavigation)
                return;
            if (!Game1.options.gamepadControls)
                return;

            try
            {
                var options = _optionsField?.GetValue(__instance) as List<OptionsElement>;
                if (options == null)
                    return;

                // Initialize interactive indices if needed
                if (_interactiveIndices == null)
                    BuildInteractiveIndices(options);

                // Initialize focus on first frame
                if (_focusedIndex < 0 && _interactiveIndices != null && _interactiveIndices.Count > 0)
                    _focusedIndex = _interactiveIndices[0];

                // Re-snap cursor every frame — option bounds update via updateContentPositions()
                // which runs before this postfix, so bounds are current. This keeps the cursor
                // tracking the focused option through scrolling.
                SnapCursorToFocused(options);

                // Right stick free-scroll — directly adjust scroll offset for smooth scrolling
                // (receiveScrollWheelAction caused visual tearing)
                var state = GamePad.GetState(PlayerIndex.One);
                float rightY = state.ThumbSticks.Right.Y;
                if (Math.Abs(rightY) > StickScrollThreshold)
                {
                    var scrollArea = _scrollAreaField?.GetValue(__instance);
                    if (scrollArea != null)
                    {
                        var scrollType = scrollArea.GetType();
                        var getYOffset = AccessTools.Method(scrollType, "getYOffsetForScroll");
                        var setYOffset = AccessTools.Method(scrollType, "setYOffsetForScroll", new[] { typeof(int) });
                        var maxYOffsetField = AccessTools.Field(scrollType, "maxYOffset");
                        if (getYOffset != null && setYOffset != null && maxYOffsetField != null)
                        {
                            int curOffset = (int)getYOffset.Invoke(scrollArea, null);
                            int maxYOffset = (int)maxYOffsetField.GetValue(scrollArea);
                            int delta = (int)(rightY * 8);
                            int newOffset = Math.Max(-maxYOffset, Math.Min(0, curOffset + delta));
                            setYOffset.Invoke(scrollArea, new object[] { newOffset });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[OptionsPage] Update_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
