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
    /// - D-pad Left/Right adjusts values via the game's native receiveKeyPress (handles
    ///   sliders +/-10, dropdown cycling, and plus/minus buttons natively)
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

            // D-pad Left/Right — adjust value using the game's native receiveKeyPress.
            // OptionsSlider, OptionsDropDown, and OptionsPlusMinus all have receiveKeyPress
            // implementations that handle moveLeftButton/moveRightButton with proper logic
            // (slider +/-10, dropdown cycle, plusminus +/-1).
            if (b == Buttons.DPadLeft || b == Buttons.LeftThumbstickLeft ||
                b == Buttons.DPadRight || b == Buttons.LeftThumbstickRight)
            {
                if (_focusedIndex < 0 || _focusedIndex >= options.Count)
                    return false;

                var opt = options[_focusedIndex];
                bool isRight = (b == Buttons.DPadRight || b == Buttons.LeftThumbstickRight);

                // Delegate to the option's own gamepad key handling
                Keys key = isRight ? Keys.Right : Keys.Left;
                opt.receiveKeyPress(key);

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
                        receiveScroll?.Invoke(scrollArea, new object[] { (int)(rightY * 30) });
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
