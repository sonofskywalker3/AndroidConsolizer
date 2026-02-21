using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Patches to swap X and Y buttons during gameplay (not menus) based on layout/style.
    ///
    /// This works by patching GamePad.GetState() to return swapped X/Y button values
    /// BEFORE the game reads them, so all game code sees the swapped buttons.
    /// </summary>
    internal static class GameplayButtonPatches
    {
        private static IMonitor Monitor;

        /// <summary>Raw right stick Y cached from GetState before suppression, for ShopMenuPatches navigation.</summary>
        internal static float RawRightStickY;

        /// <summary>Raw left stick cached from GetState before suppression, for OptionsPagePatches navigation.</summary>
        internal static float RawLeftStickX;
        internal static float RawLeftStickY;

        /// <summary>Raw trigger values cached from GetState before suppression, for HandleTriggersDirectly.</summary>
        internal static float RawLeftTrigger;
        internal static float RawRightTrigger;

        /// <summary>Cached swapped GamePadState to ensure all GetState() calls within the same tick return identical results.</summary>
        private static GamePadState? _cachedState;
        private static float _cachedRawRightStickY;
        private static float _cachedRawLeftStickX;
        private static float _cachedRawLeftStickY;
        private static float _cachedRawLeftTrigger;
        private static float _cachedRawRightTrigger;
        private static int _cachedTick = -1;

        /// <summary>Suppress logical A in GetState output until the physical button is released.
        /// Set by ItemGrabMenuPatches when A on Close X closes a chest.</summary>
        internal static bool SuppressAUntilRelease;

        /// <summary>True for one tick when Start is newly pressed during a skippable event.
        /// Consumed by ModEntry.OnUpdateTicked for cutscene skip handling.</summary>
        internal static bool StartPressedThisTick;

        /// <summary>Previous tick's raw Start state for edge detection.</summary>
        private static bool _prevStartPressed;

        /// <summary>When set to a tick number, GetMouseState postfix will report LeftButton
        /// as Pressed for that tick, simulating a screen touch. Used for cutscene skip.</summary>
        internal static int SimulateTouchClickOnTick = -1;

        /// <summary>Guard to prevent 2-param GetState postfix from running when called
        /// from within the 1-param overload (which delegates to 2-param internally).</summary>
        private static bool _inOneParamGetState;

        /// <summary>Invalidate the cached GetState so the next call recomputes.
        /// Must be called after setting Suppress*UntilRelease flags mid-tick,
        /// since the cache may already have the unsuppressed state.</summary>
        internal static void InvalidateCache() { _cachedTick = -1; }

        /// <summary>Whether triggers should be zeroed in GetState to prevent the game's
        /// native toolbar navigation from firing (our code reads RawLeftTrigger/RawRightTrigger instead).
        /// Does NOT check Context.IsPlayerFree — that can flicker during tool animations,
        /// causing the first GetState call of a tick to leak real trigger values to the game.</summary>
        internal static bool ShouldSuppressTriggers()
        {
            return ModEntry.Config != null
                && ModEntry.Config.EnableConsoleToolbar
                && !ModEntry.Config.UseBumpersInsteadOfTriggers
                && Game1.activeClickableMenu == null;
        }

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                // Patch GamePad.GetState to swap X/Y buttons at the source
                // This affects ALL code that reads gamepad state, not just specific methods
                var getStateMethod = typeof(GamePad).GetMethod(
                    nameof(GamePad.GetState),
                    new Type[] { typeof(PlayerIndex) }
                );

                if (getStateMethod != null)
                {
                    harmony.Patch(
                        original: getStateMethod,
                        prefix: new HarmonyMethod(typeof(GameplayButtonPatches), nameof(GetState_OneParam_Prefix)),
                        postfix: new HarmonyMethod(typeof(GameplayButtonPatches), nameof(GetState_Postfix))
                    );
                    Monitor.Log("Gameplay button patches applied successfully.", LogLevel.Trace);
                }
                else
                {
                    Monitor.Log("Could not find GamePad.GetState method!", LogLevel.Error);
                }

                // Also patch the 2-param overload: GetState(PlayerIndex, GamePadDeadZone)
                // The game's toolbar code may call this directly, bypassing our 1-param postfix.
                var getStateDeadZone = typeof(GamePad).GetMethod(
                    nameof(GamePad.GetState),
                    new Type[] { typeof(PlayerIndex), typeof(GamePadDeadZone) }
                );
                if (getStateDeadZone != null)
                {
                    harmony.Patch(
                        original: getStateDeadZone,
                        postfix: new HarmonyMethod(typeof(GameplayButtonPatches), nameof(GetState_DeadZone_Postfix))
                    );
                    Monitor.Log("GetState(PlayerIndex, GamePadDeadZone) patch applied.", LogLevel.Trace);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply Gameplay button patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Determines if X and Y should be swapped based on LAYOUT only.
        /// X/Y functions are positional: Left=Tool, Top=Craft on all consoles.
        /// Switch layout needs swap because Android maps buttons by position differently.
        /// </summary>
        public static bool ShouldSwapXY()
        {
            if (ModEntry.Config?.EnableButtonRemapping == false)
                return false;

            var layout = ModEntry.Config?.ControllerLayout ?? ControllerLayout.Switch;

            // Swap X/Y only for Switch layout
            // Switch: physical X is top, physical Y is left - need to swap to match game expectations
            // Xbox/PS: physical X is left, physical Y is top - matches game expectations, no swap
            return layout == ControllerLayout.Switch;
        }

        /// <summary>
        /// Determines if A and B should be swapped based on layout vs style mismatch.
        /// </summary>
        public static bool ShouldSwapAB()
        {
            if (ModEntry.Config?.EnableButtonRemapping == false)
                return false;

            var layout = ModEntry.Config?.ControllerLayout ?? ControllerLayout.Switch;
            var style = ModEntry.Config?.ControlStyle ?? ControlStyle.Switch;

            bool isXboxLayout = layout == ControllerLayout.Xbox || layout == ControllerLayout.PlayStation;
            bool isXboxStyle = style == ControlStyle.Xbox;

            // Swap when layout and style don't match
            return isXboxLayout != isXboxStyle;
        }

        /// <summary>Apply suppress-until-release logic to the final gamepad state.
        /// For each active suppression flag: if the button is still pressed, zeros it out
        /// in the returned state; if released, clears the flag. This runs on the post-swap
        /// state so it suppresses the LOGICAL button the game sees.</summary>
        private static GamePadState ApplyButtonSuppression(GamePadState state)
        {
            bool suppressA = SuppressAUntilRelease;
            bool suppressStart = ModEntry.IsCurrentEventSkippable() && state.Buttons.Start == ButtonState.Pressed;

            if (!suppressA && !suppressStart)
                return state;

            // Clear A suppression when button is released
            if (suppressA && state.Buttons.A != ButtonState.Pressed)
            {
                SuppressAUntilRelease = false;
                suppressA = false;
            }

            if (!suppressA && !suppressStart)
                return state;

            var newButtons = new GamePadButtons(
                ((!suppressA && state.Buttons.A == ButtonState.Pressed) ? Buttons.A : 0) |
                ((state.Buttons.B == ButtonState.Pressed) ? Buttons.B : 0) |
                ((state.Buttons.X == ButtonState.Pressed) ? Buttons.X : 0) |
                ((state.Buttons.Y == ButtonState.Pressed) ? Buttons.Y : 0) |
                ((!suppressStart && state.Buttons.Start == ButtonState.Pressed) ? Buttons.Start : 0) |
                ((state.Buttons.Back == ButtonState.Pressed) ? Buttons.Back : 0) |
                ((state.Buttons.LeftStick == ButtonState.Pressed) ? Buttons.LeftStick : 0) |
                ((state.Buttons.RightStick == ButtonState.Pressed) ? Buttons.RightStick : 0) |
                ((state.Buttons.LeftShoulder == ButtonState.Pressed) ? Buttons.LeftShoulder : 0) |
                ((state.Buttons.RightShoulder == ButtonState.Pressed) ? Buttons.RightShoulder : 0) |
                ((state.Buttons.BigButton == ButtonState.Pressed) ? Buttons.BigButton : 0)
            );

            return new GamePadState(
                state.ThumbSticks,
                state.Triggers,
                newButtons,
                state.DPad
            );
        }

        /// <summary>Prefix for 1-param GetState — sets nesting guard so the 2-param postfix
        /// (called internally by the 1-param overload) knows to skip.</summary>
        private static void GetState_OneParam_Prefix()
        {
            _inOneParamGetState = true;
        }

        /// <summary>
        /// Postfix for GamePad.GetState - modifies the returned state to swap buttons as needed.
        /// A/B swap applies EVERYWHERE (main menu, game menus, gameplay).
        /// X/Y swap applies only during GAMEPLAY (menus use ButtonRemapper for X/Y).
        /// </summary>
        private static void GetState_Postfix(PlayerIndex playerIndex, ref GamePadState __result)
        {
            _inOneParamGetState = false; // Clear nesting guard

            try
            {
                // Only modify for player one
                if (playerIndex != PlayerIndex.One)
                    return;

                // Return cached state if we've already computed it this tick.
                // This prevents intra-frame inconsistency when GetState() is called
                // multiple times per frame, which caused tools to fire once before charging.
                int currentTick = Game1.ticks;
                if (_cachedState.HasValue && _cachedTick == currentTick)
                {
                    __result = _cachedState.Value;
                    RawRightStickY = _cachedRawRightStickY;
                    RawLeftStickX = _cachedRawLeftStickX;
                    RawLeftStickY = _cachedRawLeftStickY;
                    RawLeftTrigger = _cachedRawLeftTrigger;
                    RawRightTrigger = _cachedRawRightTrigger;
                    return;
                }

                // Edge detection for Start during skippable events (before any swaps/suppression)
                StartPressedThisTick = false;
                bool rawStartPressed = __result.Buttons.Start == ButtonState.Pressed;
                if (rawStartPressed && !_prevStartPressed && ModEntry.IsCurrentEventSkippable())
                    StartPressedThisTick = true;
                _prevStartPressed = rawStartPressed;

                // Cache raw right stick Y before any suppression, so ShopMenuPatches can use it
                RawRightStickY = __result.ThumbSticks.Right.Y;

                // Cache raw left stick before suppression, so OptionsPagePatches can use it
                RawLeftStickX = __result.ThumbSticks.Left.X;
                RawLeftStickY = __result.ThumbSticks.Left.Y;

                // Cache raw trigger values before suppression, so HandleTriggersDirectly can use them
                RawLeftTrigger = __result.Triggers.Left;
                RawRightTrigger = __result.Triggers.Right;

                // Zero out right thumbstick when ShopMenu is on buy tab.
                // This prevents vanilla (and Game1's scroll-wheel conversion) from scrolling
                // currentItemIndex via right stick; our own navigation reads RawRightStickY.
                if (ShopMenuPatches.ShouldSuppressRightStick() && __result.ThumbSticks.Right != Vector2.Zero)
                {
                    __result = new GamePadState(
                        new GamePadThumbSticks(__result.ThumbSticks.Left, Vector2.Zero),
                        __result.Triggers,
                        __result.Buttons,
                        __result.DPad
                    );
                }

                // Zero out right thumbstick Y on social tab so Game1.updateActiveMenu()
                // doesn't fire receiveScrollWheelAction (smooth scroll). Our SocialUpdate_Prefix
                // polls RawRightStickY directly for 3-slot discrete navigation.
                if (GameMenuPatches.ShouldSuppressRightStickForSocial() && __result.ThumbSticks.Right.Y != 0f)
                {
                    __result = new GamePadState(
                        new GamePadThumbSticks(__result.ThumbSticks.Left,
                            new Vector2(__result.ThumbSticks.Right.X, 0f)),
                        __result.Triggers,
                        __result.Buttons,
                        __result.DPad
                    );
                }

                // Zero out left thumbstick on Options tab so the cursor doesn't free-roam.
                // D-pad handles option-to-option navigation; left stick would just move
                // the cursor off the options list and out of bounds.
                if (OptionsPagePatches.ShouldSuppressLeftStick()
                    && __result.ThumbSticks.Left != Vector2.Zero)
                {
                    __result = new GamePadState(
                        new GamePadThumbSticks(Vector2.Zero, __result.ThumbSticks.Right),
                        __result.Triggers,
                        __result.Buttons,
                        __result.DPad
                    );
                }

                // Zero out triggers during gameplay when our toolbar handles slot navigation.
                // This prevents the game's own trigger-based toolbar code from also moving slots.
                // Must zero BOTH analog values AND digital trigger buttons (Buttons.LeftTrigger/
                // RightTrigger) since the GamePadState constructor preserves buttons as-is.
                // Our HandleTriggersDirectly() reads RawLeftTrigger/RawRightTrigger instead.
                if (ShouldSuppressTriggers() && (__result.Triggers.Left > 0f || __result.Triggers.Right > 0f))
                {
                    var btns = __result.Buttons;
                    var cleanButtons = new GamePadButtons(
                        ((btns.A == ButtonState.Pressed) ? Buttons.A : 0) |
                        ((btns.B == ButtonState.Pressed) ? Buttons.B : 0) |
                        ((btns.X == ButtonState.Pressed) ? Buttons.X : 0) |
                        ((btns.Y == ButtonState.Pressed) ? Buttons.Y : 0) |
                        ((btns.Start == ButtonState.Pressed) ? Buttons.Start : 0) |
                        ((btns.Back == ButtonState.Pressed) ? Buttons.Back : 0) |
                        ((btns.LeftStick == ButtonState.Pressed) ? Buttons.LeftStick : 0) |
                        ((btns.RightStick == ButtonState.Pressed) ? Buttons.RightStick : 0) |
                        ((btns.LeftShoulder == ButtonState.Pressed) ? Buttons.LeftShoulder : 0) |
                        ((btns.RightShoulder == ButtonState.Pressed) ? Buttons.RightShoulder : 0) |
                        ((btns.BigButton == ButtonState.Pressed) ? Buttons.BigButton : 0)
                        // Intentionally omitting Buttons.LeftTrigger and Buttons.RightTrigger
                    );
                    __result = new GamePadState(
                        __result.ThumbSticks,
                        new GamePadTriggers(0f, 0f),
                        cleanButtons,
                        __result.DPad
                    );
                }

                // A/B swap applies everywhere (main menu, game menus, gameplay)
                bool swapAB = ShouldSwapAB();

                // X/Y swap during gameplay and BobberBar (fishing mini-game uses gameplay buttons)
                bool swapXY = ShouldSwapXY() && (Game1.activeClickableMenu == null || Game1.activeClickableMenu is BobberBar);

                // Nothing to do if no swapping needed
                if (!swapXY && !swapAB)
                {
                    __result = ApplyButtonSuppression(__result);
                    _cachedState = __result;
                    _cachedRawRightStickY = RawRightStickY;
                    _cachedRawLeftStickX = RawLeftStickX;
                    _cachedRawLeftStickY = RawLeftStickY;
                    _cachedRawLeftTrigger = RawLeftTrigger;
                    _cachedRawRightTrigger = RawRightTrigger;
                    _cachedTick = currentTick;
                    return;
                }

                // Get the original button states
                bool originalA = __result.Buttons.A == ButtonState.Pressed;
                bool originalB = __result.Buttons.B == ButtonState.Pressed;
                bool originalX = __result.Buttons.X == ButtonState.Pressed;
                bool originalY = __result.Buttons.Y == ButtonState.Pressed;

                // Determine final button states after swapping
                bool finalA = swapAB ? originalB : originalA;
                bool finalB = swapAB ? originalA : originalB;
                bool finalX = swapXY ? originalY : originalX;
                bool finalY = swapXY ? originalX : originalY;

                // Only reconstruct if something actually changed
                if (finalA == originalA && finalB == originalB && finalX == originalX && finalY == originalY)
                {
                    __result = ApplyButtonSuppression(__result);
                    _cachedState = __result;
                    _cachedRawRightStickY = RawRightStickY;
                    _cachedRawLeftStickX = RawLeftStickX;
                    _cachedRawLeftStickY = RawLeftStickY;
                    _cachedRawLeftTrigger = RawLeftTrigger;
                    _cachedRawRightTrigger = RawRightTrigger;
                    _cachedTick = currentTick;
                    return;
                }

                // Create new GamePadState with swapped buttons
                var newButtons = new GamePadButtons(
                    (finalA ? Buttons.A : 0) |
                    (finalB ? Buttons.B : 0) |
                    (finalX ? Buttons.X : 0) |
                    (finalY ? Buttons.Y : 0) |
                    ((__result.Buttons.Start == ButtonState.Pressed) ? Buttons.Start : 0) |
                    ((__result.Buttons.Back == ButtonState.Pressed) ? Buttons.Back : 0) |
                    ((__result.Buttons.LeftStick == ButtonState.Pressed) ? Buttons.LeftStick : 0) |
                    ((__result.Buttons.RightStick == ButtonState.Pressed) ? Buttons.RightStick : 0) |
                    ((__result.Buttons.LeftShoulder == ButtonState.Pressed) ? Buttons.LeftShoulder : 0) |
                    ((__result.Buttons.RightShoulder == ButtonState.Pressed) ? Buttons.RightShoulder : 0) |
                    ((__result.Buttons.BigButton == ButtonState.Pressed) ? Buttons.BigButton : 0)
                );

                __result = new GamePadState(
                    __result.ThumbSticks,
                    __result.Triggers,
                    newButtons,
                    __result.DPad
                );

                __result = ApplyButtonSuppression(__result);
                _cachedState = __result;
                _cachedRawRightStickY = RawRightStickY;
                _cachedRawLeftStickX = RawLeftStickX;
                _cachedRawLeftStickY = RawLeftStickY;
                _cachedRawLeftTrigger = RawLeftTrigger;
                _cachedRawRightTrigger = RawRightTrigger;
                _cachedTick = currentTick;
            }
            catch
            {
                // Silently ignore errors to not spam logs every frame
            }
        }

        /// <summary>Postfix for GetState(PlayerIndex, GamePadDeadZone) — the 2-param overload.
        /// Only runs when called DIRECTLY (not nested from the 1-param overload).
        /// Zeroes triggers and applies button swaps/suppression so the game's native toolbar
        /// code can't move slots through this alternate code path.</summary>
        private static void GetState_DeadZone_Postfix(PlayerIndex playerIndex, ref GamePadState __result)
        {
            // Skip if nested inside 1-param GetState (which handles everything already)
            if (_inOneParamGetState)
                return;

            // Delegate to the same processing as the 1-param postfix
            GetState_Postfix(playerIndex, ref __result);
        }
    }
}
