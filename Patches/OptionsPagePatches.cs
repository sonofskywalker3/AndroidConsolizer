using System;
using System.Collections.Generic;
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
    /// Harmony patches for OptionsPage — adds full controller navigation to the Options tab.
    /// Manages its own focus index rather than using the game's allClickableComponents system,
    /// because OptionsPage never populates clickable components and the MobileScrollbox
    /// scissor rect doesn't manage visibility on off-screen items.
    /// </summary>
    internal static class OptionsPagePatches
    {
        private static IMonitor Monitor;

        // State
        private static int _focusedIndex = -1;
        private static List<int> _interactiveIndices;
        private static bool _dropdownOpen;
        private static object _activeDropdown; // OptionsDropDown via reflection
        private static int _dropdownOriginalSelection;
        private static bool _lastAPressed;

        // Cached reflection — OptionsPage fields
        private static FieldInfo _optionsField;
        private static FieldInfo _scrollAreaField;
        private static FieldInfo _selectedDropdownField;

        // Cached reflection — OptionsDropDown fields (Android names differ from PC DLL)
        private static FieldInfo _ddDropDownOpenField;
        private static FieldInfo _ddSelectedOptionField;
        private static FieldInfo _ddDropDownOptionsField;
        private static FieldInfo _ddWhichOptionField;
        private static FieldInfo _ddSelectedStaticField; // OptionsDropDown.selected (static)

        // Cached reflection — OptionsSlider fields
        private static FieldInfo _sliderValueField;       // or property
        private static PropertyInfo _sliderValueProp;
        private static FieldInfo _sliderMinField;
        private static FieldInfo _sliderMaxField;
        private static FieldInfo _sliderWhichOptionField;

        // Cached reflection — OptionsButton
        private static MethodInfo _buttonReleaseLeftClick;

        // Cached reflection — OptionsPlusMinus
        private static FieldInfo _pmMinusButtonField;
        private static FieldInfo _pmPlusButtonField;

        // Constant: right stick scroll threshold (matches social tab)
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
                _selectedDropdownField = AccessTools.Field(typeof(OptionsPage), "_selectedDropdown");

                if (_optionsField == null)
                    Monitor.Log("OptionsPagePatches: 'options' field not found", LogLevel.Warn);
                if (_scrollAreaField == null)
                    Monitor.Log("OptionsPagePatches: 'scrollArea' field not found", LogLevel.Warn);

                // Cache OptionsDropDown reflection
                _ddDropDownOpenField = AccessTools.Field(typeof(OptionsDropDown), "dropDownOpen");
                _ddSelectedOptionField = AccessTools.Field(typeof(OptionsDropDown), "selectedOption");
                _ddDropDownOptionsField = AccessTools.Field(typeof(OptionsDropDown), "dropDownOptions");
                _ddWhichOptionField = AccessTools.Field(typeof(OptionsElement), "whichOption");
                _ddSelectedStaticField = AccessTools.Field(typeof(OptionsDropDown), "selected");

                // Cache OptionsSlider reflection — try field first, then property
                _sliderValueProp = AccessTools.Property(typeof(OptionsSlider), "value");
                _sliderValueField = AccessTools.Field(typeof(OptionsSlider), "value")
                                 ?? AccessTools.Field(typeof(OptionsSlider), "_value");
                _sliderMinField = AccessTools.Field(typeof(OptionsSlider), "sliderMinValue");
                _sliderMaxField = AccessTools.Field(typeof(OptionsSlider), "sliderMaxValue");
                _sliderWhichOptionField = AccessTools.Field(typeof(OptionsElement), "whichOption");

                // Cache OptionsButton reflection
                _buttonReleaseLeftClick = AccessTools.Method(typeof(OptionsButton), "releaseLeftClick",
                    new[] { typeof(int), typeof(int) })
                    ?? AccessTools.Method(typeof(OptionsButton), "leftClickReleased",
                        new[] { typeof(int), typeof(int) });

                // Cache OptionsPlusMinus reflection
                _pmMinusButtonField = AccessTools.Field(typeof(OptionsPlusMinus), "minusButton");
                _pmPlusButtonField = AccessTools.Field(typeof(OptionsPlusMinus), "plusButton");

                Monitor.Log($"OptionsPagePatches reflection: ddOpen={_ddDropDownOpenField != null}, " +
                    $"sliderVal={_sliderValueProp != null || _sliderValueField != null}, " +
                    $"sliderMin={_sliderMinField != null}, sliderMax={_sliderMaxField != null}, " +
                    $"btnRelease={_buttonReleaseLeftClick != null}", LogLevel.Trace);

                // Patch receiveGamePadButton — virtual method, patch on OptionsPage even though
                // it doesn't override (Harmony patches virtual methods on derived types)
                var receiveGPB = AccessTools.Method(typeof(OptionsPage), nameof(OptionsPage.receiveGamePadButton));
                if (receiveGPB == null)
                    receiveGPB = AccessTools.Method(typeof(IClickableMenu), nameof(IClickableMenu.receiveGamePadButton));

                if (receiveGPB != null)
                {
                    harmony.Patch(
                        original: receiveGPB,
                        prefix: new HarmonyMethod(typeof(OptionsPagePatches), nameof(ReceiveGamePadButton_Prefix))
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

                // Patch draw
                var drawMethod = AccessTools.Method(typeof(OptionsPage), "draw", new[] { typeof(SpriteBatch) });
                if (drawMethod != null)
                {
                    harmony.Patch(
                        original: drawMethod,
                        postfix: new HarmonyMethod(typeof(OptionsPagePatches), nameof(Draw_Postfix))
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
            _dropdownOpen = false;
            _activeDropdown = null;
            _lastAPressed = false;
        }

        #region Reflection Helpers

        private static bool GetDropDownOpen(object dropdown)
        {
            if (_ddDropDownOpenField == null || dropdown == null) return false;
            return (bool)_ddDropDownOpenField.GetValue(dropdown);
        }

        private static void SetDropDownOpen(object dropdown, bool value)
        {
            _ddDropDownOpenField?.SetValue(dropdown, value);
        }

        private static int GetDropDownSelectedOption(object dropdown)
        {
            if (_ddSelectedOptionField == null || dropdown == null) return 0;
            return (int)_ddSelectedOptionField.GetValue(dropdown);
        }

        private static void SetDropDownSelectedOption(object dropdown, int value)
        {
            _ddSelectedOptionField?.SetValue(dropdown, value);
        }

        private static List<string> GetDropDownOptions(object dropdown)
        {
            return _ddDropDownOptionsField?.GetValue(dropdown) as List<string>;
        }

        private static int GetWhichOption(OptionsElement element)
        {
            if (_ddWhichOptionField == null) return element.whichOption;
            return (int)_ddWhichOptionField.GetValue(element);
        }

        private static void SetDropDownSelectedStatic(object dropdown)
        {
            _ddSelectedStaticField?.SetValue(null, dropdown);
        }

        private static void ClearDropDownSelectedStatic()
        {
            _ddSelectedStaticField?.SetValue(null, null);
        }

        private static int GetSliderValue(OptionsSlider slider)
        {
            if (_sliderValueProp != null) return (int)_sliderValueProp.GetValue(slider);
            if (_sliderValueField != null) return (int)_sliderValueField.GetValue(slider);
            return 0;
        }

        private static void SetSliderValue(OptionsSlider slider, int value)
        {
            if (_sliderValueProp != null) _sliderValueProp.SetValue(slider, value);
            else _sliderValueField?.SetValue(slider, value);
        }

        private static int GetSliderMin(OptionsSlider slider)
        {
            if (_sliderMinField != null) return (int)_sliderMinField.GetValue(slider);
            return 0;
        }

        private static int GetSliderMax(OptionsSlider slider)
        {
            if (_sliderMaxField != null) return (int)_sliderMaxField.GetValue(slider);
            return 100;
        }

        private static void InvokeButtonRelease(OptionsButton button, int x, int y)
        {
            _buttonReleaseLeftClick?.Invoke(button, new object[] { x, y });
        }

        #endregion

        /// <summary>Snap the mouse cursor to the focused option so the game's rendering/hit-testing works.</summary>
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
                    // Option is above visible area — scroll up
                    int newOffset = yOffset + (scissor.Y - bounds.Y) + padding;
                    newOffset = Math.Min(0, newOffset);
                    setYOffset.Invoke(scrollArea, new object[] { newOffset });
                }
                else if (bounds.Y + bounds.Height > scissor.Bottom)
                {
                    // Option is below visible area — scroll down
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
        /// Prefix on OptionsPage.receiveGamePadButton — handles D-pad navigation,
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

            // B button: if dropdown is open, cancel it. Otherwise let base handle (close menu).
            if (b == Buttons.B)
            {
                if (_dropdownOpen && _activeDropdown != null)
                {
                    SetDropDownSelectedOption(_activeDropdown, _dropdownOriginalSelection);
                    SetDropDownOpen(_activeDropdown, false);
                    ClearDropDownSelectedStatic();
                    if (_selectedDropdownField != null)
                        _selectedDropdownField.SetValue(page, null);
                    _dropdownOpen = false;
                    _activeDropdown = null;
                    Game1.playSound("bigDeSelect");
                    return false;
                }
                return true; // Let base handle B = close
            }

            // While dropdown is open, only up/down navigate within it
            if (_dropdownOpen && _activeDropdown != null)
            {
                var ddOptions = GetDropDownOptions(_activeDropdown);
                int ddSelected = GetDropDownSelectedOption(_activeDropdown);

                if (b == Buttons.DPadUp || b == Buttons.LeftThumbstickUp)
                {
                    if (ddSelected > 0)
                    {
                        SetDropDownSelectedOption(_activeDropdown, ddSelected - 1);
                        Game1.playSound("shiny4");
                    }
                    return false;
                }
                if (b == Buttons.DPadDown || b == Buttons.LeftThumbstickDown)
                {
                    if (ddOptions != null && ddSelected < ddOptions.Count - 1)
                    {
                        SetDropDownSelectedOption(_activeDropdown, ddSelected + 1);
                        Game1.playSound("shiny4");
                    }
                    return false;
                }
                if (b == Buttons.A)
                {
                    // Confirm dropdown selection
                    if (ddOptions != null && _activeDropdown is OptionsElement ddElem)
                    {
                        int sel = GetDropDownSelectedOption(_activeDropdown);
                        Game1.options.changeDropDownOption(ddElem.whichOption, ddOptions[sel]);
                    }
                    SetDropDownOpen(_activeDropdown, false);
                    ClearDropDownSelectedStatic();
                    if (_selectedDropdownField != null)
                        _selectedDropdownField.SetValue(page, null);
                    _dropdownOpen = false;
                    _activeDropdown = null;
                    Game1.playSound("bigSelect");
                    return false;
                }
                // Consume all other buttons while dropdown is open
                return false;
            }

            // D-pad Up / LeftThumbstickUp — navigate to previous interactive option
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

            // D-pad Down / LeftThumbstickDown — navigate to next interactive option
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

                if (opt is OptionsCheckbox)
                {
                    opt.receiveLeftClick(opt.bounds.Center.X, opt.bounds.Center.Y);
                    Game1.playSound("drumkit6");
                    return false;
                }

                if (opt is OptionsDropDown dropdown)
                {
                    // Open the dropdown for browsing
                    _dropdownOriginalSelection = GetDropDownSelectedOption(dropdown);
                    SetDropDownOpen(dropdown, true);
                    SetDropDownSelectedStatic(dropdown);
                    if (_selectedDropdownField != null)
                        _selectedDropdownField.SetValue(page, dropdown);
                    _dropdownOpen = true;
                    _activeDropdown = dropdown;
                    Game1.playSound("shwip");
                    return false;
                }

                if (opt is OptionsButton button)
                {
                    button.receiveLeftClick(button.bounds.Center.X, button.bounds.Center.Y);
                    InvokeButtonRelease(button, button.bounds.Center.X, button.bounds.Center.Y);
                    return false;
                }

                // OptionsPlusMinus and OptionsSlider: A does nothing, left/right adjusts
                return false;
            }

            // Left/Right — adjust value on focused option
            if (b == Buttons.DPadLeft || b == Buttons.LeftThumbstickLeft ||
                b == Buttons.DPadRight || b == Buttons.LeftThumbstickRight)
            {
                if (_focusedIndex < 0 || _focusedIndex >= options.Count)
                    return false;

                var opt = options[_focusedIndex];
                bool isRight = (b == Buttons.DPadRight || b == Buttons.LeftThumbstickRight);

                if (opt is OptionsSlider slider)
                {
                    // Directly adjust slider value via reflection
                    int step = 10;
                    int curVal = GetSliderValue(slider);
                    int minVal = GetSliderMin(slider);
                    int maxVal = GetSliderMax(slider);
                    int newVal = isRight
                        ? Math.Min(curVal + step, maxVal)
                        : Math.Max(curVal - step, minVal);
                    SetSliderValue(slider, newVal);
                    Game1.options.changeSliderOption(slider.whichOption, newVal);
                    Game1.playSound("shiny4");
                    return false;
                }

                if (opt is OptionsDropDown dropdown)
                {
                    // Cycle dropdown value directly via reflection
                    var ddOptions = GetDropDownOptions(dropdown);
                    if (ddOptions != null && ddOptions.Count > 0)
                    {
                        int sel = GetDropDownSelectedOption(dropdown);
                        if (isRight)
                        {
                            sel++;
                            if (sel >= ddOptions.Count) sel = 0;
                        }
                        else
                        {
                            sel--;
                            if (sel < 0) sel = ddOptions.Count - 1;
                        }
                        SetDropDownSelectedOption(dropdown, sel);
                        Game1.options.changeDropDownOption(dropdown.whichOption, ddOptions[sel]);
                    }
                    Game1.playSound("shiny4");
                    return false;
                }

                if (opt is OptionsPlusMinus plusMinus)
                {
                    // Simulate click on plus/minus button via reflection
                    if (_pmMinusButtonField != null && _pmPlusButtonField != null)
                    {
                        var minusRect = (Rectangle)_pmMinusButtonField.GetValue(plusMinus);
                        var plusRect = (Rectangle)_pmPlusButtonField.GetValue(plusMinus);
                        if (isRight)
                            plusMinus.receiveLeftClick(plusRect.Center.X, plusRect.Center.Y);
                        else
                            plusMinus.receiveLeftClick(minusRect.Center.X, minusRect.Center.Y);
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
                {
                    _focusedIndex = _interactiveIndices[0];
                    SnapCursorToFocused(options);
                }

                // Right stick free-scroll
                var state = GamePad.GetState(PlayerIndex.One);
                float rightY = state.ThumbSticks.Right.Y;
                if (Math.Abs(rightY) > StickScrollThreshold)
                {
                    var scrollArea = _scrollAreaField?.GetValue(__instance);
                    if (scrollArea != null)
                    {
                        var receiveScroll = AccessTools.Method(scrollArea.GetType(), "receiveScrollWheelAction", new[] { typeof(int) });
                        receiveScroll?.Invoke(scrollArea, new object[] { (int)(rightY * 120) });
                    }
                }

                _lastAPressed = state.IsButtonDown(Buttons.A);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[OptionsPage] Update_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Postfix on OptionsPage.draw — draws the finger cursor at the focused option.
        /// Runs AFTER finishScrollBoxDrawing restores the scissor rect, so we manually
        /// check if the option is in the visible scroll area.
        /// </summary>
        private static void Draw_Postfix(OptionsPage __instance, SpriteBatch b)
        {
            if (!ModEntry.Config.EnableGameMenuNavigation)
                return;
            if (!Game1.options.gamepadControls || Game1.options.hardwareCursor)
                return;

            try
            {
                var options = _optionsField?.GetValue(__instance) as List<OptionsElement>;
                if (options == null || _focusedIndex < 0 || _focusedIndex >= options.Count)
                    return;

                var opt = options[_focusedIndex];
                var scrollArea = _scrollAreaField?.GetValue(__instance);

                // Visibility check: only draw cursor if option is within the scroll viewport
                if (scrollArea != null)
                {
                    var scissorField = AccessTools.Field(scrollArea.GetType(), "scissorRectangle");
                    if (scissorField != null)
                    {
                        var scissor = (Rectangle)scissorField.GetValue(scrollArea);
                        if (opt.bounds.Y + opt.bounds.Height < scissor.Y || opt.bounds.Y > scissor.Bottom)
                            return; // Off-screen, don't draw cursor
                    }
                }

                // Draw finger cursor to the left of the option, vertically centered
                // Cursor sprite is tile 44, 16x16 at 4x scale = 64x64
                int cursorX = opt.bounds.X - 68;
                int cursorY = opt.bounds.Center.Y - 32;

                b.Draw(Game1.mouseCursors,
                    new Vector2(cursorX, cursorY),
                    Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 44, 16, 16),
                    Color.White, 0f, Vector2.Zero,
                    4f + Game1.dialogueButtonScale / 150f,
                    SpriteEffects.None, 1f);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[OptionsPage] Draw_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
