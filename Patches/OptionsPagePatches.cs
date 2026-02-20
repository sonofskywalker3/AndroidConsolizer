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
    ///   so receiveKeyPress doesn't work): sliders +/-10, plusminus +/-1
    /// - A toggles checkboxes, activates buttons, and opens/confirms dropdowns
    /// - Dropdowns: A opens the list, D-pad Up/Down selects, A confirms, B cancels
    /// - B closes the menu (or cancels an open dropdown)
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

        // Dropdown mode state — when a dropdown is open, D-pad navigates within it
        private static bool _dropdownMode = false;
        private static OptionsDropDown _activeDropdown = null;
        private static int _dropdownStartSelection = -1;

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
        private static FieldInfo _ddDropDownOpenField;
        private static FieldInfo _ddSelectedStaticField;

        // Cached reflection — OptionsPlusMinus fields
        private static FieldInfo _pmMinusButtonField;
        private static FieldInfo _pmPlusButtonField;

        // Left stick discrete navigation state
        private static int _stickNavDir = 0; // 0=none, 1=up, 2=down, 3=left, 4=right
        private static int _stickNavLastTick = -1;
        private static bool _stickNavInitial = true;
        private const float StickNavThreshold = 0.5f;
        private const int StickNavInitialDelay = 18; // ~300ms at 60fps
        private const int StickNavRepeatDelay = 9;   // ~150ms at 60fps

        // Constants
        private const float StickScrollThreshold = 0.2f;

        /// <summary>Whether the left thumbstick should be suppressed at GetState level.
        /// When true, GameplayButtonPatches.GetState_Postfix zeros the left stick so the
        /// cursor doesn't free-roam — D-pad handles option-to-option navigation instead.</summary>
        public static bool ShouldSuppressLeftStick()
        {
            if (!ModEntry.Config.EnableGameMenuNavigation || !Game1.options.gamepadControls)
                return false;
            if (ModEntry.Config.FreeCursorOnSettings)
                return false;
            if (Game1.activeClickableMenu is GameMenu gm)
            {
                var currentPage = gm.GetCurrentPage();
                if (!(currentPage is OptionsPage))
                    return false;
                // Don't suppress left stick when GMCM is open as a child menu —
                // GMCM fork handles its own controller navigation.
                var childMenu = gm.GetChildMenu();
                if (childMenu != null && IsGmcmMenu(childMenu))
                    return false;
                return true;
            }
            return false;
        }

        /// <summary>Check if the given menu is a GMCM menu type (by full type name, no reflection init needed).</summary>
        private static bool IsGmcmMenu(IClickableMenu menu)
        {
            if (menu == null) return false;
            var fullName = menu.GetType().FullName;
            return fullName == "GenericModConfigMenu.Framework.ModConfigMenu"
                || fullName == "GenericModConfigMenu.Framework.SpecificModConfigMenu";
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
                _ddDropDownOpenField = AccessTools.Field(typeof(OptionsDropDown), "dropDownOpen");
                _ddSelectedStaticField = AccessTools.Field(typeof(OptionsDropDown), "selected");

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
            CloseDropdown(false);
            _focusedIndex = -1;
            _interactiveIndices = null;
            _aPressTick = -1;
            _stickNavDir = 0;
        }

        /// <summary>Close the active dropdown, optionally applying the selection.</summary>
        private static void CloseDropdown(bool apply)
        {
            if (_activeDropdown != null)
            {
                if (apply)
                {
                    var ddOptions = _ddDropDownOptionsField?.GetValue(_activeDropdown) as List<string>;
                    int sel = _ddSelectedOptionField != null ? (int)_ddSelectedOptionField.GetValue(_activeDropdown) : 0;
                    if (ddOptions != null && sel >= 0 && sel < ddOptions.Count)
                        Game1.options.changeDropDownOption(_activeDropdown.whichOption, ddOptions[sel]);
                }
                else
                {
                    _ddSelectedOptionField?.SetValue(_activeDropdown, _dropdownStartSelection);
                }

                _ddDropDownOpenField?.SetValue(_activeDropdown, false);
                _ddSelectedStaticField?.SetValue(null, null);
            }
            _dropdownMode = false;
            _activeDropdown = null;
            _dropdownStartSelection = -1;
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

        /// <summary>Fire a synthetic navigation event from stick input (same logic as D-pad).</summary>
        private static void HandleStickNav(int dir, OptionsPage page, List<OptionsElement> options)
        {
            var scrollArea = _scrollAreaField?.GetValue(page);

            if (dir == 1 || dir == 2) // Up/Down
            {
                bool isUp = (dir == 1);
                if (_dropdownMode && _activeDropdown != null)
                {
                    var ddOptions = _ddDropDownOptionsField?.GetValue(_activeDropdown) as List<string>;
                    int count = ddOptions?.Count ?? 0;
                    if (count > 0 && _ddSelectedOptionField != null)
                    {
                        int sel = (int)_ddSelectedOptionField.GetValue(_activeDropdown);
                        sel = isUp ? (sel - 1 + count) % count : (sel + 1) % count;
                        _ddSelectedOptionField.SetValue(_activeDropdown, sel);
                        Game1.playSound("shiny4");
                    }
                    return;
                }

                if (isUp)
                {
                    int pos = FindCurrentPosition();
                    if (pos <= 0)
                        pos = _interactiveIndices.Count;
                    _focusedIndex = _interactiveIndices[pos - 1];
                }
                else
                {
                    int pos = FindCurrentPosition();
                    if (pos < 0 || pos >= _interactiveIndices.Count - 1)
                        pos = -1;
                    _focusedIndex = _interactiveIndices[pos + 1];
                }
                EnsureVisible(page, options, scrollArea);
                SnapCursorToFocused(options);
                Game1.playSound("shiny4");
            }
            else if (dir == 3 || dir == 4) // Left/Right
            {
                if (_dropdownMode)
                    return;
                if (_focusedIndex < 0 || _focusedIndex >= options.Count)
                    return;

                var opt = options[_focusedIndex];
                bool isRight = (dir == 4);

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
                }
                else if (opt is OptionsPlusMinus plusMinus)
                {
                    if (_pmMinusButtonField != null && _pmPlusButtonField != null)
                    {
                        var targetRect = isRight
                            ? (Rectangle)_pmPlusButtonField.GetValue(plusMinus)
                            : (Rectangle)_pmMinusButtonField.GetValue(plusMinus);
                        plusMinus.receiveLeftClick(targetRect.Center.X, targetRect.Center.Y);
                    }
                }
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
            if (!ModEntry.Config.EnableGameMenuNavigation || !Game1.options.gamepadControls || ModEntry.Config.FreeCursorOnSettings)
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
            if (ModEntry.Config.FreeCursorOnSettings)
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

            // B button: cancel dropdown if open, otherwise let base handle (close menu)
            if (b == Buttons.B)
            {
                if (_dropdownMode)
                {
                    CloseDropdown(false);
                    Game1.playSound("bigDeSelect");
                    return false;
                }
                return true;
            }

            // D-pad Up/Down — navigate within dropdown if open, otherwise between options
            if (b == Buttons.DPadUp || b == Buttons.LeftThumbstickUp ||
                b == Buttons.DPadDown || b == Buttons.LeftThumbstickDown)
            {
                bool isUp = (b == Buttons.DPadUp || b == Buttons.LeftThumbstickUp);

                if (_dropdownMode && _activeDropdown != null)
                {
                    var ddOptions = _ddDropDownOptionsField?.GetValue(_activeDropdown) as List<string>;
                    int count = ddOptions?.Count ?? 0;
                    if (count > 0 && _ddSelectedOptionField != null)
                    {
                        int sel = (int)_ddSelectedOptionField.GetValue(_activeDropdown);
                        sel = isUp ? (sel - 1 + count) % count : (sel + 1) % count;
                        _ddSelectedOptionField.SetValue(_activeDropdown, sel);
                        Game1.playSound("shiny4");
                    }
                    return false;
                }

                if (isUp)
                {
                    int pos = FindCurrentPosition();
                    if (pos <= 0)
                        pos = _interactiveIndices.Count; // wrap
                    _focusedIndex = _interactiveIndices[pos - 1];
                }
                else
                {
                    int pos = FindCurrentPosition();
                    if (pos < 0 || pos >= _interactiveIndices.Count - 1)
                        pos = -1; // wrap
                    _focusedIndex = _interactiveIndices[pos + 1];
                }
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

                if (opt is OptionsDropDown dropdown)
                {
                    if (_dropdownMode && _activeDropdown == dropdown)
                    {
                        // Second A press — confirm selection and close
                        CloseDropdown(true);
                        Game1.playSound("coin");
                    }
                    else
                    {
                        // First A press — open the dropdown
                        int curSel = _ddSelectedOptionField != null ? (int)_ddSelectedOptionField.GetValue(dropdown) : 0;
                        _dropdownStartSelection = curSel;
                        _ddDropDownOpenField?.SetValue(dropdown, true);
                        _ddSelectedStaticField?.SetValue(null, dropdown);
                        _dropdownMode = true;
                        _activeDropdown = dropdown;
                        Game1.playSound("shwip");

                        // Auto-scroll so the full expanded dropdown fits on screen.
                        // Dropdown height = bounds.Height * optionCount.
                        var ddOptions = _ddDropDownOptionsField?.GetValue(dropdown) as List<string>;
                        int ddCount = ddOptions?.Count ?? 1;
                        int expandedBottom = dropdown.bounds.Y + dropdown.bounds.Height * ddCount;
                        if (scrollArea != null)
                        {
                            try
                            {
                                var scrollType = scrollArea.GetType();
                                var scissorField = AccessTools.Field(scrollType, "scissorRectangle");
                                var getYOffset = AccessTools.Method(scrollType, "getYOffsetForScroll");
                                var setYOffset = AccessTools.Method(scrollType, "setYOffsetForScroll", new[] { typeof(int) });
                                var maxYOffsetField = AccessTools.Field(scrollType, "maxYOffset");
                                if (scissorField != null && getYOffset != null && setYOffset != null && maxYOffsetField != null)
                                {
                                    var scissor = (Rectangle)scissorField.GetValue(scrollArea);
                                    if (expandedBottom > scissor.Bottom)
                                    {
                                        int yOffset = (int)getYOffset.Invoke(scrollArea, null);
                                        int maxYOffset = (int)maxYOffsetField.GetValue(scrollArea);
                                        int scrollNeeded = expandedBottom - scissor.Bottom + 20;
                                        int newOffset = Math.Max(-maxYOffset, yOffset - scrollNeeded);
                                        setYOffset.Invoke(scrollArea, new object[] { newOffset });
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    return false;
                }

                // Sliders, plus/minus: A does nothing (use Left/Right to adjust)
                return false;
            }

            // D-pad Left/Right — adjust value directly via reflection.
            // Can't use receiveKeyPress because it requires snappyMenus=true (false on Android).
            if (b == Buttons.DPadLeft || b == Buttons.LeftThumbstickLeft ||
                b == Buttons.DPadRight || b == Buttons.LeftThumbstickRight)
            {
                // While dropdown is open, consume Left/Right (no action — use Up/Down to navigate)
                if (_dropdownMode)
                    return false;

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

                if (opt is OptionsDropDown)
                {
                    // Dropdowns use A to open/close — Left/Right does nothing when closed
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
            if (ModEntry.Config.FreeCursorOnSettings)
                return;
            if (!Game1.options.gamepadControls)
                return;
            // Don't run our nav when GMCM is open as child menu
            if (Game1.activeClickableMenu is GameMenu gm && gm.GetChildMenu() is IClickableMenu child && IsGmcmMenu(child))
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

                // Left stick → discrete navigation (raw values cached before GetState suppression)
                float lsX = GameplayButtonPatches.RawLeftStickX;
                float lsY = GameplayButtonPatches.RawLeftStickY;
                int newDir = 0;
                if (Math.Abs(lsY) >= Math.Abs(lsX))
                {
                    if (lsY > StickNavThreshold) newDir = 1;       // Up (Y is positive up)
                    else if (lsY < -StickNavThreshold) newDir = 2; // Down
                }
                else
                {
                    if (lsX < -StickNavThreshold) newDir = 3;      // Left
                    else if (lsX > StickNavThreshold) newDir = 4;  // Right
                }

                if (newDir == 0)
                {
                    _stickNavDir = 0;
                    _stickNavInitial = true;
                }
                else if (newDir != _stickNavDir)
                {
                    // Direction changed — fire immediately
                    _stickNavDir = newDir;
                    _stickNavLastTick = Game1.ticks;
                    _stickNavInitial = true;
                    HandleStickNav(newDir, __instance, options);
                }
                else
                {
                    // Same direction held — check repeat timing
                    int elapsed = Game1.ticks - _stickNavLastTick;
                    int delay = _stickNavInitial ? StickNavInitialDelay : StickNavRepeatDelay;
                    if (elapsed >= delay)
                    {
                        _stickNavLastTick = Game1.ticks;
                        _stickNavInitial = false;
                        HandleStickNav(newDir, __instance, options);
                    }
                }

                // Right stick free-scroll — directly adjust scroll offset for smooth scrolling
                // (receiveScrollWheelAction caused visual tearing)
                // Blocked while dropdown is open (scrolling would move the dropdown, disorienting)
                var state = GamePad.GetState(PlayerIndex.One);
                float rightY = state.ThumbSticks.Right.Y;
                if (!_dropdownMode && Math.Abs(rightY) > StickScrollThreshold)
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

                            // After scrolling, update focus if current option scrolled off-screen.
                            // Without this, D-pad would snap back to the old off-screen position.
                            var scissorField = AccessTools.Field(scrollType, "scissorRectangle");
                            if (scissorField != null && _focusedIndex >= 0 && _focusedIndex < options.Count
                                && _interactiveIndices != null)
                            {
                                var scissor = (Rectangle)scissorField.GetValue(scrollArea);
                                var fb = options[_focusedIndex].bounds;
                                if (fb.Bottom < scissor.Y || fb.Y > scissor.Bottom)
                                {
                                    // Off-screen — pick nearest visible interactive option
                                    int bestIdx = -1;
                                    int bestDist = int.MaxValue;
                                    int viewCenter = scissor.Y + scissor.Height / 2;
                                    foreach (int idx in _interactiveIndices)
                                    {
                                        var b = options[idx].bounds;
                                        if (b.Bottom >= scissor.Y && b.Y <= scissor.Bottom)
                                        {
                                            int dist = Math.Abs(b.Center.Y - viewCenter);
                                            if (dist < bestDist)
                                            {
                                                bestDist = dist;
                                                bestIdx = idx;
                                            }
                                        }
                                    }
                                    if (bestIdx >= 0)
                                        _focusedIndex = bestIdx;
                                }
                            }
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
