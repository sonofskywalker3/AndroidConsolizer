using System;
using System.Collections;
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
    /// Patches for GameMenu tab switching.
    /// Fixes controller navigation on tabs where it's broken (Animals, Social, etc.)
    /// by fixing component bounds so the game's own snap navigation works.
    /// </summary>
    internal static class GameMenuPatches
    {
        private static IMonitor Monitor;

        // Track last fixed tab to avoid redundant fixes
        private static int _lastFixedTab = -1;

        // Cached reflection fields for AnimalPage
        private static FieldInfo _characterSlotsField;
        private static FieldInfo _slotHeightField;
        private static FieldInfo _mainBoxField;
        private static FieldInfo _slotPositionField;
        private static FieldInfo _slotsYStartField;

        // Cached reflection fields for SocialPage (same field names, different type)
        private static FieldInfo _socialCharacterSlotsField;
        private static FieldInfo _socialSlotHeightField;
        private static FieldInfo _socialMainBoxField;
        private static FieldInfo _socialSlotPositionField;
        private static FieldInfo _socialSlotsYStartField;
        private static FieldInfo _socialClickedEntryField;
        private static FieldInfo _socialScrollAreaField;
        private static MethodInfo _socialSelectSlotMethod;
        private static FieldInfo _socialSpritesField;

        // Track slotPosition to detect right-stick scroll changes
        private static int _lastSocialSlotPosition = -1;

        // Save selected slot index when gift log opens, restore on return (-1 = no saved position)
        private static int _savedSocialReturnIndex = -1;

        // Held-scroll acceleration: track direction, step size, and timing
        private static int _heldScrollDirection = 0; // -1=up, 0=none, 1=down
        private static int _heldScrollStep = 1; // 1=left stick/dpad, 3=right stick
        private static int _heldScrollStartTick = 0;
        private static int _lastAutoScrollTick = 0;
        private const int HeldScrollInitialDelay = 24; // ~400ms before acceleration starts
        private const int HeldScrollRepeatInterval = 8; // ~133ms between repeats (~2x manual speed)

        // Scrollbox tap simulation: two-frame tap gesture
        // Phase 0 = idle, 1 = receiveLeftClick pending, 2 = releaseLeftClick pending
        private static int _scrollboxTapPhase = 0;
        private static int _scrollboxTapX, _scrollboxTapY;

        // Right stick initial-press detection for social tab
        private static bool _prevRightStickEngaged = false;

        // Diagnostic: track child menu on SocialPage (gift log)
        private static bool _dumpedChildMenu = false;

        // Diagnostic: cached scrollbox yOffset field for logging
        private static FieldInfo _scrollboxYOffsetField;

        /// <summary>Check if right stick Y should be suppressed for social tab navigation.
        /// Called from GameplayButtonPatches.GetState_Postfix.</summary>
        internal static bool ShouldSuppressRightStickForSocial()
        {
            if (!ModEntry.Config.EnableGameMenuNavigation)
                return false;
            if (Game1.activeClickableMenu is GameMenu gameMenu)
                return gameMenu.currentTab == GameMenu.socialTab;
            return false;
        }

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(GameMenu), nameof(GameMenu.changeTab)),
                    postfix: new HarmonyMethod(typeof(GameMenuPatches), nameof(ChangeTab_Postfix))
                );

                harmony.Patch(
                    original: AccessTools.Method(typeof(GameMenu), "draw", new[] { typeof(SpriteBatch) }),
                    postfix: new HarmonyMethod(typeof(GameMenuPatches), nameof(Draw_Postfix))
                );

                harmony.Patch(
                    original: AccessTools.Method(typeof(GameMenu), nameof(GameMenu.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(GameMenuPatches), nameof(ReceiveGamePadButton_Prefix))
                );

                // Patch SocialPage.update() to drive scrollbox tap gesture
                var socialUpdateMethod = AccessTools.Method(typeof(SocialPage), "update", new[] { typeof(GameTime) });
                if (socialUpdateMethod != null)
                {
                    harmony.Patch(
                        original: socialUpdateMethod,
                        prefix: new HarmonyMethod(typeof(GameMenuPatches), nameof(SocialUpdate_Prefix))
                    );
                }

                Monitor.Log("GameMenu patches applied.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply GameMenu patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Called from ModEntry.OnMenuChanged when GameMenu opens.</summary>
        public static void OnGameMenuOpened(GameMenu menu)
        {
            _lastFixedTab = -1;
            try
            {
                if (ModEntry.Config.VerboseLogging)
                {
                    Monitor?.Log($"[GameMenuDiag] GameMenu opened, currentTab={menu.currentTab}", LogLevel.Info);
                    DumpTabState(menu, menu.currentTab);
                }

                FixCurrentTab(menu, menu.currentTab);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GameMenu] Error in OnGameMenuOpened: {ex.Message}", LogLevel.Error);
            }
        }

        private static void ChangeTab_Postfix(GameMenu __instance, int whichTab)
        {
            try
            {
                if (ModEntry.Config.VerboseLogging)
                    DumpTabState(__instance, whichTab);

                FixCurrentTab(__instance, whichTab);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GameMenu] Error in ChangeTab_Postfix: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Dispatch to per-tab fix methods based on the page type.</summary>
        private static void FixCurrentTab(GameMenu menu, int tabIndex)
        {
            if (!ModEntry.Config.EnableGameMenuNavigation)
                return;

            if (menu.pages == null || tabIndex < 0 || tabIndex >= menu.pages.Count)
                return;

            var page = menu.pages[tabIndex];
            if (page == null)
                return;

            // Use type name check (reflection-safe for Android vs PC differences)
            string typeName = page.GetType().Name;

            if (typeName == "AnimalPage")
            {
                FixAnimalPage(page);
                _lastFixedTab = tabIndex;
            }
            else if (typeName == "SocialPage")
            {
                FixSocialPage(page);
                _lastFixedTab = tabIndex;
            }
            // Future tabs: CollectionsPage, CraftingPage, OptionsPage
        }

        /// <summary>
        /// Fix the Animals tab. characterSlots have valid IDs and U/D neighbors
        /// but bounds.Y is stuck at 0. Fix bounds to match visual positions so
        /// the game's own snap navigation works and the finger cursor appears.
        /// </summary>
        private static void FixAnimalPage(IClickableMenu page)
        {
            try
            {
                // Cache reflection fields on first use
                if (_characterSlotsField == null)
                {
                    var pageType = page.GetType();
                    _characterSlotsField = AccessTools.Field(pageType, "characterSlots");
                    _slotHeightField = AccessTools.Field(pageType, "slotHeight");
                    _mainBoxField = AccessTools.Field(pageType, "mainBox");
                    _slotPositionField = AccessTools.Field(pageType, "slotPosition");
                    _slotsYStartField = AccessTools.Field(pageType, "slotsYStart");
                }

                if (_characterSlotsField == null || _slotHeightField == null || _mainBoxField == null)
                {
                    Monitor?.Log("[GameMenu] AnimalPage: missing reflection fields, cannot fix", LogLevel.Warn);
                    return;
                }

                var charSlots = _characterSlotsField.GetValue(page) as IList;
                if (charSlots == null || charSlots.Count == 0)
                {
                    Monitor?.Log("[GameMenu] AnimalPage: no characterSlots", LogLevel.Trace);
                    return;
                }

                int slotHeight = (int)_slotHeightField.GetValue(page);
                var mainBox = (Rectangle)_mainBoxField.GetValue(page);

                int slotPosition = 0;
                if (_slotPositionField != null)
                    slotPosition = (int)_slotPositionField.GetValue(page);

                // Use the actual slotsYStart field — it's relative to mainBox.Y.
                // Diagnostic data: slotsYStart=49, mainBox.Y=72 → absolute Y=121
                int slotsYOffset = 0;
                if (_slotsYStartField != null)
                    slotsYOffset = (int)_slotsYStartField.GetValue(page);
                int slotsYStart = mainBox.Y + slotsYOffset;

                bool verbose = ModEntry.Config.VerboseLogging;
                if (verbose)
                    Monitor?.Log($"[GameMenu] AnimalPage: {charSlots.Count} slots, slotHeight={slotHeight}, mainBox=({mainBox.X},{mainBox.Y},{mainBox.Width},{mainBox.Height}), slotsYStart={slotsYStart} (offset={slotsYOffset}), slotPosition={slotPosition}", LogLevel.Info);

                ClickableComponent firstSlot = null;

                for (int i = 0; i < charSlots.Count; i++)
                {
                    var slot = charSlots[i] as ClickableComponent;
                    if (slot == null) continue;

                    // Compute visual Y position relative to scroll position
                    int visualIndex = i - slotPosition;
                    int newY = slotsYStart + (visualIndex * slotHeight);

                    // Fix bounds Y — keep X, Width, Height from original
                    var b = slot.bounds;
                    slot.bounds = new Rectangle(b.X, newY, b.Width, slotHeight);

                    if (i == 0)
                        firstSlot = slot;

                    if (verbose)
                        Monitor?.Log($"[GameMenu]   slot[{i}] ID={slot.myID} bounds=({slot.bounds.X},{slot.bounds.Y},{slot.bounds.Width},{slot.bounds.Height}) U={slot.upNeighborID} D={slot.downNeighborID}", LogLevel.Info);
                }

                // Set currentlySnappedComponent and snap cursor to it
                if (firstSlot != null)
                {
                    page.currentlySnappedComponent = firstSlot;
                    page.snapCursorToCurrentSnappedComponent();

                    if (verbose)
                        Monitor?.Log($"[GameMenu] AnimalPage: snapped to slot[0] ID={firstSlot.myID} at ({firstSlot.bounds.X},{firstSlot.bounds.Y})", LogLevel.Info);
                }

                Monitor?.Log($"[GameMenu] AnimalPage: fixed {charSlots.Count} slot bounds", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GameMenu] Error fixing AnimalPage: {ex.Message}\n{ex.StackTrace}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Fix the Social tab. Same structure as AnimalPage — characterSlots with
        /// bounds.Y stuck at 0 — but has 37 slots with scrolling.
        /// </summary>
        private static void FixSocialPage(IClickableMenu page)
        {
            try
            {
                // Cache reflection fields on first use (separate from AnimalPage — different type)
                if (_socialCharacterSlotsField == null)
                {
                    var pageType = page.GetType();
                    _socialCharacterSlotsField = AccessTools.Field(pageType, "characterSlots");
                    _socialSlotHeightField = AccessTools.Field(pageType, "slotHeight");
                    _socialMainBoxField = AccessTools.Field(pageType, "mainBox");
                    _socialSlotPositionField = AccessTools.Field(pageType, "slotPosition");
                    _socialSlotsYStartField = AccessTools.Field(pageType, "slotsYStart");
                    _socialClickedEntryField = AccessTools.Field(pageType, "clickedEntry");
                    _socialScrollAreaField = AccessTools.Field(pageType, "scrollArea");
                    _socialSpritesField = AccessTools.Field(pageType, "sprites");
                    _socialSelectSlotMethod = pageType.GetMethod("_SelectSlot", BindingFlags.Instance | BindingFlags.NonPublic);

                    // Cache yOffsetForScroll field from MobileScrollbox (for scroll sync)
                    if (_socialScrollAreaField != null)
                    {
                        var scrollArea = _socialScrollAreaField.GetValue(page);
                        if (scrollArea != null)
                        {
                            _scrollboxYOffsetField = AccessTools.Field(scrollArea.GetType(), "yOffsetForScroll");
                            if (_scrollboxYOffsetField != null)
                                Monitor?.Log($"[SocialDiag] Found scrollbox yOffset field: {_scrollboxYOffsetField.Name} (type={_scrollboxYOffsetField.FieldType.Name})", LogLevel.Trace);
                            else
                                Monitor?.Log($"[SocialDiag] WARNING: yOffsetForScroll field not found on MobileScrollbox", LogLevel.Warn);
                        }
                    }
                }

                if (_socialCharacterSlotsField == null || _socialSlotHeightField == null || _socialMainBoxField == null)
                {
                    Monitor?.Log("[GameMenu] SocialPage: missing reflection fields, cannot fix", LogLevel.Warn);
                    return;
                }

                var charSlots = _socialCharacterSlotsField.GetValue(page) as IList;
                if (charSlots == null || charSlots.Count == 0)
                {
                    Monitor?.Log("[GameMenu] SocialPage: no characterSlots", LogLevel.Trace);
                    return;
                }

                int slotHeight = (int)_socialSlotHeightField.GetValue(page);
                var mainBox = (Rectangle)_socialMainBoxField.GetValue(page);

                int slotPosition = 0;
                if (_socialSlotPositionField != null)
                    slotPosition = (int)_socialSlotPositionField.GetValue(page);

                int slotsYOffset = 0;
                if (_socialSlotsYStartField != null)
                    slotsYOffset = (int)_socialSlotsYStartField.GetValue(page);
                int slotsYStart = mainBox.Y + slotsYOffset;
                int visibleSlots = mainBox.Height / slotHeight;

                bool verbose = ModEntry.Config.VerboseLogging;

                // Restore saved position when returning from gift log.
                // Don't clear _savedSocialReturnIndex here — FixSocialPage may be called twice
                // (once in ChangeTab_Postfix during constructor, once in OnGameMenuOpened).
                // Cleared on next user input in HandleSocialInput instead.
                int snapTargetIndex = slotPosition;
                if (_savedSocialReturnIndex >= 0 && _savedSocialReturnIndex < charSlots.Count)
                {
                    snapTargetIndex = _savedSocialReturnIndex;

                    // Scroll so the saved slot is visible
                    if (snapTargetIndex < slotPosition || snapTargetIndex >= slotPosition + visibleSlots)
                    {
                        slotPosition = Math.Max(0, Math.Min(snapTargetIndex, charSlots.Count - visibleSlots));
                        _socialSlotPositionField?.SetValue(page, slotPosition);

                        // Sync MobileScrollbox yOffset
                        if (_socialScrollAreaField != null && _scrollboxYOffsetField != null)
                        {
                            var scrollArea = _socialScrollAreaField.GetValue(page);
                            if (scrollArea != null)
                                _scrollboxYOffsetField.SetValue(scrollArea, -(slotPosition * slotHeight));
                        }
                    }

                    Monitor?.Log($"[GameMenu] SocialPage: restoring position to slot[{snapTargetIndex}], slotPosition={slotPosition}", LogLevel.Trace);
                }

                if (verbose)
                    Monitor?.Log($"[GameMenu] SocialPage: {charSlots.Count} slots, slotHeight={slotHeight}, mainBox=({mainBox.X},{mainBox.Y},{mainBox.Width},{mainBox.Height}), slotsYStart={slotsYStart} (offset={slotsYOffset}), slotPosition={slotPosition}", LogLevel.Info);

                ClickableComponent snapTarget = null;

                for (int i = 0; i < charSlots.Count; i++)
                {
                    var slot = charSlots[i] as ClickableComponent;
                    if (slot == null) continue;

                    int visualIndex = i - slotPosition;
                    int newY = slotsYStart + (visualIndex * slotHeight);

                    var b = slot.bounds;
                    slot.bounds = new Rectangle(b.X, newY, b.Width, slotHeight);

                    if (i == snapTargetIndex)
                        snapTarget = slot;

                    if (verbose)
                        Monitor?.Log($"[GameMenu]   slot[{i}] ID={slot.myID} bounds=({slot.bounds.X},{slot.bounds.Y},{slot.bounds.Width},{slot.bounds.Height}) U={slot.upNeighborID} D={slot.downNeighborID}", LogLevel.Info);
                }

                // Snap to target slot (saved position or first visible)
                if (snapTarget != null)
                {
                    page.currentlySnappedComponent = snapTarget;
                    page.snapCursorToCurrentSnappedComponent();

                    if (verbose)
                        Monitor?.Log($"[GameMenu] SocialPage: snapped to slot[{snapTargetIndex}] ID={snapTarget.myID} at ({snapTarget.bounds.X},{snapTarget.bounds.Y})", LogLevel.Info);
                }

                _lastSocialSlotPosition = slotPosition;

                // Also fix sprites bounds — receiveLeftClick hit-tests against sprites, not characterSlots
                // Sprites are separate objects with different (smaller) bounds; sync them to match charSlots
                SyncSpritesBounds(page, charSlots);

                Monitor?.Log($"[GameMenu] SocialPage: fixed {charSlots.Count} slot bounds + sprites", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GameMenu] Error fixing SocialPage: {ex.Message}\n{ex.StackTrace}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Intercept D-pad and A button on tabs we manage, to prevent the
        /// MobileScrollbox from eating D-pad input and to handle A-press clicks.
        /// Returns false to skip original when we handle the input.
        /// </summary>
        private static bool ReceiveGamePadButton_Prefix(GameMenu __instance, Buttons b)
        {
            try
            {
                if (!ModEntry.Config.EnableGameMenuNavigation)
                    return true;

                if (__instance.pages == null || __instance.currentTab < 0 || __instance.currentTab >= __instance.pages.Count)
                    return true;

                // Trigger-based tab switching (console parity — LT/RT switch tabs)
                if (b == Buttons.LeftTrigger)
                {
                    int prevTab = __instance.currentTab - 1;
                    if (prevTab >= 0)
                    {
                        Game1.playSound("smallSelect");
                        __instance.changeTab(prevTab);
                    }
                    return false;
                }
                if (b == Buttons.RightTrigger)
                {
                    int nextTab = __instance.currentTab + 1;
                    if (nextTab < __instance.pages.Count)
                    {
                        Game1.playSound("smallSelect");
                        __instance.changeTab(nextTab);
                    }
                    return false;
                }

                var page = __instance.pages[__instance.currentTab];
                string typeName = page.GetType().Name;

                if (typeName == "AnimalPage")
                    return HandleAnimalInput(page, b);
                if (typeName == "SocialPage")
                {
                    Monitor?.Log($"[SocialDiag] Prefix dispatching to HandleSocialInput, button={b}", LogLevel.Trace);
                    return HandleSocialInput(page, b);
                }

                return true; // pass through for unhandled tabs
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GameMenu] Error in ReceiveGamePadButton_Prefix: {ex.Message}", LogLevel.Error);
                return true;
            }
        }

        // ===== SocialPage.update() prefix — held-scroll acceleration + scrollbox tap gesture =====

        private static void SocialUpdate_Prefix(SocialPage __instance)
        {
            // Right stick initial-press detection: poll RawRightStickY (pre-suppression)
            // to trigger 3-slot navigation on neutral→engaged transition.
            // Right stick Y is suppressed at GetState level to block vanilla smooth scroll.
            float rawRightY = GameplayButtonPatches.RawRightStickY;
            bool rightStickEngaged = Math.Abs(rawRightY) > 0.5f;

            if (rightStickEngaged && !_prevRightStickEngaged && _heldScrollDirection == 0)
            {
                // Newly engaged — fire 3-slot jump
                int dir = rawRightY < -0.5f ? 1 : -1; // Y < 0 = stick pushed down
                NavigateSocialSlot(__instance, dir, 3);
                _heldScrollDirection = dir;
                _heldScrollStep = 3;
                _heldScrollStartTick = Game1.ticks;
                _lastAutoScrollTick = Game1.ticks;
            }
            _prevRightStickEngaged = rightStickEngaged;

            // Held-scroll acceleration: detect which direction is currently held,
            // handle direction changes gracefully (reset timing on reversal).
            if (_heldScrollDirection != 0)
            {
                var padState = GamePad.GetState(PlayerIndex.One);
                float rawRStickY = GameplayButtonPatches.RawRightStickY;

                // Detect current held direction from all input sources
                bool downHeld = padState.DPad.Down == ButtonState.Pressed
                             || padState.ThumbSticks.Left.Y < -0.5f
                             || rawRStickY < -0.5f;
                bool upHeld = padState.DPad.Up == ButtonState.Pressed
                           || padState.ThumbSticks.Left.Y > 0.5f
                           || rawRStickY > 0.5f;

                int currentDir = downHeld ? 1 : upHeld ? -1 : 0;

                if (currentDir == 0)
                {
                    // Nothing held — stop
                    _heldScrollDirection = 0;
                }
                else if (currentDir != _heldScrollDirection)
                {
                    // Direction reversed — reset timing for the new direction
                    _heldScrollDirection = currentDir;
                    _heldScrollStartTick = Game1.ticks;
                    _lastAutoScrollTick = Game1.ticks;
                }
                else
                {
                    // Same direction still held — auto-repeat
                    int elapsed = Game1.ticks - _heldScrollStartTick;
                    if (elapsed >= HeldScrollInitialDelay)
                    {
                        int sinceLast = Game1.ticks - _lastAutoScrollTick;
                        if (sinceLast >= HeldScrollRepeatInterval)
                        {
                            _lastAutoScrollTick = Game1.ticks;
                            NavigateSocialSlot(__instance, _heldScrollDirection, _heldScrollStep);
                        }
                    }
                }
            }

            // Scrollbox tap gesture (legacy, kept for compatibility)
            if (_scrollboxTapPhase == 0) return;

            var scrollArea = _socialScrollAreaField?.GetValue(__instance);
            if (scrollArea == null) { _scrollboxTapPhase = 0; return; }

            var scrollType = scrollArea.GetType();

            if (_scrollboxTapPhase == 1)
            {
                var clickMethod = scrollType.GetMethod("receiveLeftClick", new[] { typeof(int), typeof(int) });
                clickMethod?.Invoke(scrollArea, new object[] { _scrollboxTapX, _scrollboxTapY });
                _scrollboxTapPhase = 2;
            }
            else if (_scrollboxTapPhase == 2)
            {
                var releaseMethod = scrollType.GetMethod("releaseLeftClick", new[] { typeof(int), typeof(int) });
                releaseMethod?.Invoke(scrollArea, new object[] { _scrollboxTapX, _scrollboxTapY });
                _scrollboxTapPhase = 0;
            }
        }

        /// <summary>
        /// Handle input on the Animals tab. Returns false to skip original method.
        /// </summary>
        private static bool HandleAnimalInput(IClickableMenu page, Buttons b)
        {
            switch (b)
            {
                case Buttons.A:
                {
                    // Click at the currently snapped slot's center position
                    var snapped = page.currentlySnappedComponent;
                    if (snapped != null && snapped.bounds.Y > 0)
                    {
                        int cx = snapped.bounds.Center.X;
                        int cy = snapped.bounds.Center.Y;
                        page.receiveLeftClick(cx, cy, true);

                        if (ModEntry.Config.VerboseLogging)
                            Monitor?.Log($"[GameMenu] AnimalPage: A-press → receiveLeftClick({cx},{cy})", LogLevel.Info);
                    }
                    return false; // block original (prevents scrollbox interference)
                }

                case Buttons.DPadDown:
                case Buttons.LeftThumbstickDown:
                    NavigateAnimalSlot(page, 1);
                    return false;

                case Buttons.DPadUp:
                case Buttons.LeftThumbstickUp:
                    NavigateAnimalSlot(page, -1);
                    return false;

                default:
                    return true; // pass through LB/RB for tab switching, B for close, etc.
            }
        }

        /// <summary>
        /// Navigate to the next (+1) or previous (-1) animal slot.
        /// </summary>
        private static void NavigateAnimalSlot(IClickableMenu page, int direction)
        {
            try
            {
                if (_characterSlotsField == null) return;

                var charSlots = _characterSlotsField.GetValue(page) as IList;
                if (charSlots == null || charSlots.Count == 0) return;

                // Find current slot index
                var current = page.currentlySnappedComponent;
                int currentIndex = -1;
                for (int i = 0; i < charSlots.Count; i++)
                {
                    if (charSlots[i] == current)
                    {
                        currentIndex = i;
                        break;
                    }
                }

                if (currentIndex < 0) currentIndex = 0;

                int newIndex = currentIndex + direction;

                // Clamp to valid range — don't wrap
                if (newIndex < 0 || newIndex >= charSlots.Count)
                    return; // at boundary, do nothing

                var newSlot = charSlots[newIndex] as ClickableComponent;
                if (newSlot == null) return;

                page.currentlySnappedComponent = newSlot;
                page.snapCursorToCurrentSnappedComponent();
                Game1.playSound("shiny4");

                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"[GameMenu] AnimalPage: nav {(direction > 0 ? "down" : "up")} → slot[{newIndex}] ID={newSlot.myID} at ({newSlot.bounds.X},{newSlot.bounds.Y})", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GameMenu] Error in NavigateAnimalSlot: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Handle input on the Social tab. Returns false to skip original method.
        /// Same as Animals but routes to scroll-aware navigation.
        /// </summary>
        private static bool HandleSocialInput(IClickableMenu page, Buttons b)
        {
            Monitor?.Log($"[SocialDiag] HandleSocialInput called, button={b}", LogLevel.Trace);

            // Clear saved return position on any navigation input (A will set a new one)
            if (b != Buttons.A)
                _savedSocialReturnIndex = -1;

            switch (b)
            {
                case Buttons.A:
                {
                    // Save current slot index so we can restore position after gift log closes.
                    // Gift log exit creates a brand new GameMenu/SocialPage, losing all scroll state.
                    var snapped = page.currentlySnappedComponent;
                    if (snapped != null && _socialCharacterSlotsField != null)
                    {
                        var charSlots = _socialCharacterSlotsField.GetValue(page) as IList;
                        if (charSlots != null)
                        {
                            for (int i = 0; i < charSlots.Count; i++)
                            {
                                if (charSlots[i] == snapped)
                                {
                                    _savedSocialReturnIndex = i;
                                    break;
                                }
                            }
                        }
                    }

                    // Block A from reaching scrollbox, let Android touch sim fire naturally.
                    return false;
                }

                case Buttons.DPadDown:
                case Buttons.LeftThumbstickDown:
                    NavigateSocialSlot(page, 1);
                    _heldScrollDirection = 1;
                    _heldScrollStep = 1;
                    _heldScrollStartTick = Game1.ticks;
                    _lastAutoScrollTick = Game1.ticks;
                    return false;

                case Buttons.DPadUp:
                case Buttons.LeftThumbstickUp:
                    NavigateSocialSlot(page, -1);
                    _heldScrollDirection = -1;
                    _heldScrollStep = 1;
                    _heldScrollStartTick = Game1.ticks;
                    _lastAutoScrollTick = Game1.ticks;
                    return false;

                case Buttons.RightThumbstickDown:
                    NavigateSocialSlot(page, 1, 3);
                    _heldScrollDirection = 1;
                    _heldScrollStep = 3;
                    _heldScrollStartTick = Game1.ticks;
                    _lastAutoScrollTick = Game1.ticks;
                    return false;

                case Buttons.RightThumbstickUp:
                    NavigateSocialSlot(page, -1, 3);
                    _heldScrollDirection = -1;
                    _heldScrollStep = 3;
                    _heldScrollStartTick = Game1.ticks;
                    _lastAutoScrollTick = Game1.ticks;
                    return false;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Navigate social slots by step count in the given direction.
        /// direction: +1 = down, -1 = up. step: number of slots to move (1 = left stick, 3 = right stick).
        /// Handles scrolling when navigation moves past the visible area.
        /// </summary>
        private static void NavigateSocialSlot(IClickableMenu page, int direction, int step = 1)
        {
            try
            {
                if (_socialCharacterSlotsField == null || _socialSlotPositionField == null ||
                    _socialSlotHeightField == null || _socialMainBoxField == null)
                    return;

                var charSlots = _socialCharacterSlotsField.GetValue(page) as IList;
                if (charSlots == null || charSlots.Count == 0)
                    return;

                int slotPosition = (int)_socialSlotPositionField.GetValue(page);
                int slotHeight = (int)_socialSlotHeightField.GetValue(page);
                var mainBox = (Rectangle)_socialMainBoxField.GetValue(page);
                int visibleSlots = mainBox.Height / slotHeight;
                int maxSlotPos = charSlots.Count - visibleSlots;

                // If slotPosition changed externally, re-fix bounds
                if (slotPosition != _lastSocialSlotPosition)
                {
                    RefixSocialBounds(page, charSlots, slotPosition, slotHeight, mainBox);
                    _lastSocialSlotPosition = slotPosition;
                }

                // Find current slot index
                var current = page.currentlySnappedComponent;
                int currentIndex = -1;
                for (int i = 0; i < charSlots.Count; i++)
                {
                    if (charSlots[i] == current)
                    {
                        currentIndex = i;
                        break;
                    }
                }
                if (currentIndex < 0) currentIndex = slotPosition;

                // Move by step, clamping to valid range
                int newIndex = Math.Max(0, Math.Min(currentIndex + direction * step, charSlots.Count - 1));
                if (newIndex == currentIndex)
                    return; // at boundary

                // Scroll if new index is outside visible range
                if (newIndex < slotPosition || newIndex >= slotPosition + visibleSlots)
                {
                    int newSlotPos;
                    if (direction > 0)
                        newSlotPos = Math.Min(newIndex - visibleSlots + 1, maxSlotPos);
                    else
                        newSlotPos = Math.Max(newIndex, 0);
                    newSlotPos = Math.Max(0, Math.Min(newSlotPos, maxSlotPos));

                    _socialSlotPositionField.SetValue(page, newSlotPos);

                    // Sync MobileScrollbox yOffsetForScroll (negative when scrolled down)
                    if (_socialScrollAreaField != null && _scrollboxYOffsetField != null)
                    {
                        var scrollArea = _socialScrollAreaField.GetValue(page);
                        if (scrollArea != null)
                            _scrollboxYOffsetField.SetValue(scrollArea, -(newSlotPos * slotHeight));
                    }

                    slotPosition = newSlotPos;
                    RefixSocialBounds(page, charSlots, slotPosition, slotHeight, mainBox);
                    _lastSocialSlotPosition = slotPosition;
                }

                var newSlot = charSlots[newIndex] as ClickableComponent;
                if (newSlot == null)
                    return;

                page.currentlySnappedComponent = newSlot;
                page.snapCursorToCurrentSnappedComponent();
                Game1.playSound("shiny4");
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GameMenu] Error in NavigateSocialSlot: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Sync sprites Y position to match characterSlots Y, and set Height for cell frame alignment.
        /// On Android, receiveLeftClick hit-tests against sprites[i].bounds with +4 height.
        /// The game draws cell frame partition lines at sprites[i].bounds.Y + 4 + Height.
        /// Height=84 is the minimum that places the top partition at mainBox.Y (visible top bar)
        /// while reducing the gap above portraits from 70px to 50px. Touch sim at charSlots
        /// center (Y+69) lands within hit area [Y, Y+88).
        /// </summary>
        private static void SyncSpritesBounds(IClickableMenu page, IList charSlots)
        {
            if (_socialSpritesField == null) return;

            var sprites = _socialSpritesField.GetValue(page) as IList;
            if (sprites == null) return;

            int count = Math.Min(sprites.Count, charSlots.Count);
            for (int i = 0; i < count; i++)
            {
                var sprite = sprites[i] as ClickableComponent;
                var slot = charSlots[i] as ClickableComponent;
                if (sprite == null || slot == null) continue;

                // Sync Y from charSlots, preserve original X/Width, set Height=84 for cell frame alignment
                sprite.bounds = new Rectangle(sprite.bounds.X, slot.bounds.Y, sprite.bounds.Width, 84);
            }
        }

        /// <summary>
        /// Re-fix slot bounds without changing the snap target.
        /// Used after scroll position changes (right stick or receiveScrollWheelAction).
        /// </summary>
        private static void RefixSocialBounds(IClickableMenu page, IList charSlots, int slotPosition, int slotHeight, Rectangle mainBox)
        {
            int slotsYOffset = 0;
            if (_socialSlotsYStartField != null)
                slotsYOffset = (int)_socialSlotsYStartField.GetValue(page);
            int slotsYStart = mainBox.Y + slotsYOffset;

            for (int i = 0; i < charSlots.Count; i++)
            {
                var slot = charSlots[i] as ClickableComponent;
                if (slot == null) continue;

                int visualIndex = i - slotPosition;
                int newY = slotsYStart + (visualIndex * slotHeight);

                var b = slot.bounds;
                slot.bounds = new Rectangle(b.X, newY, b.Width, slotHeight);
            }

            // Also sync sprites bounds after re-fixing charSlots
            SyncSpritesBounds(page, charSlots);
        }

        /// <summary>
        /// Draw the finger cursor on tabs where Android suppresses drawMouse.
        /// Also runs diagnostics for child menus (gift log on SocialPage).
        /// </summary>
        private static void Draw_Postfix(GameMenu __instance, SpriteBatch b)
        {
            if (!ModEntry.Config.EnableGameMenuNavigation)
                return;

            try
            {
                // Diagnostic: detect and dump child menu on SocialPage (gift log)
                if (__instance.pages != null && __instance.currentTab >= 0 && __instance.currentTab < __instance.pages.Count)
                {
                    var page = __instance.pages[__instance.currentTab];
                    if (page != null && page.GetType().Name == "SocialPage")
                    {
                        // Check _childMenu via reflection
                        var childMenuField = AccessTools.Field(page.GetType(), "_childMenu")
                                          ?? AccessTools.Field(typeof(IClickableMenu), "_childMenu");
                        if (childMenuField != null)
                        {
                            var childMenu = childMenuField.GetValue(page) as IClickableMenu;
                            if (childMenu != null)
                            {
                                if (!_dumpedChildMenu)
                                {
                                    _dumpedChildMenu = true;
                                    DumpChildMenu(childMenu);
                                }
                            }
                            else
                            {
                                _dumpedChildMenu = false;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GameMenu] Error in Draw_Postfix: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Dump full diagnostic info for a child menu (gift log/profile).</summary>
        private static void DumpChildMenu(IClickableMenu childMenu)
        {
            Monitor?.Log($"[GameMenuDiag] === Child menu detected ===", LogLevel.Info);
            Monitor?.Log($"[GameMenuDiag] Type: {childMenu.GetType().FullName}", LogLevel.Info);
            Monitor?.Log($"[GameMenuDiag] Bounds: ({childMenu.xPositionOnScreen},{childMenu.yPositionOnScreen},{childMenu.width},{childMenu.height})", LogLevel.Info);

            var snapped = childMenu.currentlySnappedComponent;
            if (snapped != null)
                Monitor?.Log($"[GameMenuDiag] currentlySnappedComponent: ID={snapped.myID} bounds=({snapped.bounds.X},{snapped.bounds.Y},{snapped.bounds.Width},{snapped.bounds.Height}) name='{snapped.name}'", LogLevel.Info);
            else
                Monitor?.Log($"[GameMenuDiag] currentlySnappedComponent: null", LogLevel.Info);

            var allComps = childMenu.allClickableComponents;
            if (allComps != null)
            {
                Monitor?.Log($"[GameMenuDiag] allClickableComponents: count={allComps.Count}", LogLevel.Info);
                for (int i = 0; i < allComps.Count; i++)
                {
                    var c = allComps[i];
                    if (c == null) continue;
                    Monitor?.Log($"[GameMenuDiag]   [{i}] ID={c.myID} name='{c.name}' bounds=({c.bounds.X},{c.bounds.Y},{c.bounds.Width},{c.bounds.Height}) neighbors L={c.leftNeighborID} R={c.rightNeighborID} U={c.upNeighborID} D={c.downNeighborID}", LogLevel.Info);
                }
            }
            else
                Monitor?.Log($"[GameMenuDiag] allClickableComponents: null", LogLevel.Info);

            // Enumerate all fields
            DumpAllFields(childMenu, "ChildMenu");
        }

        #region Diagnostic methods (verbose logging only)

        private static void DumpTabState(GameMenu menu, int tabIndex)
        {
            try
            {
                Monitor.Log($"[GameMenuDiag] === Tab changed to {tabIndex} ===", LogLevel.Info);

                var pages = menu.pages;
                if (pages == null || tabIndex < 0 || tabIndex >= pages.Count)
                {
                    Monitor.Log($"[GameMenuDiag] pages is null or tabIndex {tabIndex} out of range (count={pages?.Count})", LogLevel.Info);
                    return;
                }

                var page = pages[tabIndex];
                if (page == null)
                {
                    Monitor.Log($"[GameMenuDiag] pages[{tabIndex}] is null", LogLevel.Info);
                    return;
                }

                Monitor.Log($"[GameMenuDiag] Page type: {page.GetType().FullName}", LogLevel.Info);

                // currentlySnappedComponent
                var snapped = page.currentlySnappedComponent;
                if (snapped != null)
                    Monitor.Log($"[GameMenuDiag] currentlySnappedComponent: ID={snapped.myID} bounds=({snapped.bounds.X},{snapped.bounds.Y},{snapped.bounds.Width},{snapped.bounds.Height}) name='{snapped.name}'", LogLevel.Info);
                else
                    Monitor.Log($"[GameMenuDiag] currentlySnappedComponent: null", LogLevel.Info);

                // allClickableComponents
                var allComps = page.allClickableComponents;
                if (allComps != null)
                {
                    Monitor.Log($"[GameMenuDiag] allClickableComponents: count={allComps.Count}", LogLevel.Info);
                    for (int i = 0; i < allComps.Count; i++)
                    {
                        var c = allComps[i];
                        if (c == null)
                        {
                            Monitor.Log($"[GameMenuDiag]   [{i}] null", LogLevel.Info);
                            continue;
                        }
                        Monitor.Log($"[GameMenuDiag]   [{i}] ID={c.myID} name='{c.name}' bounds=({c.bounds.X},{c.bounds.Y},{c.bounds.Width},{c.bounds.Height}) neighbors L={c.leftNeighborID} R={c.rightNeighborID} U={c.upNeighborID} D={c.downNeighborID}", LogLevel.Info);
                    }
                }
                else
                {
                    Monitor.Log($"[GameMenuDiag] allClickableComponents: null", LogLevel.Info);
                }

                // Tab-specific diagnostics
                if (page is SocialPage)
                    DumpSocialPage(page);
                else if (page is CollectionsPage)
                    DumpCollectionsPage(page);
                else if (page is OptionsPage)
                    DumpOptionsPage(page);

                // For all tabs, enumerate all fields
                DumpAllFields(page, page.GetType().Name);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[GameMenuDiag] Error in DumpTabState: {ex.Message}\n{ex.StackTrace}", LogLevel.Error);
            }
        }

        private static void DumpSocialPage(IClickableMenu page)
        {
            Monitor.Log($"[GameMenuDiag] --- SocialPage specific data ---", LogLevel.Info);

            // characterSlots
            try
            {
                var charSlotsField = AccessTools.Field(page.GetType(), "characterSlots");
                if (charSlotsField != null)
                {
                    var charSlots = charSlotsField.GetValue(page);
                    if (charSlots is IList charList)
                    {
                        Monitor.Log($"[GameMenuDiag] characterSlots: count={charList.Count}", LogLevel.Info);
                        for (int i = 0; i < charList.Count && i < 30; i++)
                        {
                            var slot = charList[i];
                            if (slot is ClickableTextureComponent ctc)
                                Monitor.Log($"[GameMenuDiag]   charSlot[{i}] ID={ctc.myID} name='{ctc.name}' bounds=({ctc.bounds.X},{ctc.bounds.Y},{ctc.bounds.Width},{ctc.bounds.Height}) neighbors L={ctc.leftNeighborID} R={ctc.rightNeighborID} U={ctc.upNeighborID} D={ctc.downNeighborID}", LogLevel.Info);
                            else if (slot is ClickableComponent cc)
                                Monitor.Log($"[GameMenuDiag]   charSlot[{i}] ID={cc.myID} name='{cc.name}' bounds=({cc.bounds.X},{cc.bounds.Y},{cc.bounds.Width},{cc.bounds.Height}) neighbors L={cc.leftNeighborID} R={cc.rightNeighborID} U={cc.upNeighborID} D={cc.downNeighborID}", LogLevel.Info);
                            else
                                Monitor.Log($"[GameMenuDiag]   charSlot[{i}] type={slot?.GetType().Name ?? "null"}", LogLevel.Info);
                        }
                    }
                    else
                        Monitor.Log($"[GameMenuDiag] characterSlots: not IList, value={charSlots}", LogLevel.Info);
                }
                else
                    Monitor.Log($"[GameMenuDiag] characterSlots: field not found", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[GameMenuDiag] characterSlots error: {ex.Message}", LogLevel.Error);
            }

            // slotPosition
            DumpFieldValue(page, "slotPosition");
            // numFarmers
            DumpFieldValue(page, "numFarmers");
            // sprites
            DumpFieldValue(page, "sprites");
            // names
            DumpFieldValue(page, "names");
            // kpiSlots
            DumpFieldValue(page, "kpiSlots");
        }

        private static void DumpCollectionsPage(IClickableMenu page)
        {
            Monitor.Log($"[GameMenuDiag] --- CollectionsPage specific data ---", LogLevel.Info);

            // sideTabs
            try
            {
                var sideTabsField = AccessTools.Field(page.GetType(), "sideTabs");
                if (sideTabsField != null)
                {
                    var sideTabs = sideTabsField.GetValue(page);
                    if (sideTabs is IList tabList)
                    {
                        Monitor.Log($"[GameMenuDiag] sideTabs: count={tabList.Count}", LogLevel.Info);
                        for (int i = 0; i < tabList.Count; i++)
                        {
                            var tab = tabList[i];
                            if (tab is ClickableTextureComponent ctc)
                                Monitor.Log($"[GameMenuDiag]   sideTab[{i}] ID={ctc.myID} name='{ctc.name}' bounds=({ctc.bounds.X},{ctc.bounds.Y},{ctc.bounds.Width},{ctc.bounds.Height}) neighbors L={ctc.leftNeighborID} R={ctc.rightNeighborID} U={ctc.upNeighborID} D={ctc.downNeighborID}", LogLevel.Info);
                            else if (tab is ClickableComponent cc)
                                Monitor.Log($"[GameMenuDiag]   sideTab[{i}] ID={cc.myID} name='{cc.name}' bounds=({cc.bounds.X},{cc.bounds.Y},{cc.bounds.Width},{cc.bounds.Height}) neighbors L={cc.leftNeighborID} R={cc.rightNeighborID} U={cc.upNeighborID} D={cc.downNeighborID}", LogLevel.Info);
                            else
                                Monitor.Log($"[GameMenuDiag]   sideTab[{i}] type={tab?.GetType().Name ?? "null"}", LogLevel.Info);
                        }
                    }
                    else
                        Monitor.Log($"[GameMenuDiag] sideTabs: not IList, value={sideTabs}", LogLevel.Info);
                }
                else
                    Monitor.Log($"[GameMenuDiag] sideTabs: field not found", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[GameMenuDiag] sideTabs error: {ex.Message}", LogLevel.Error);
            }

            // collections, currentTab, currentPage
            DumpFieldValue(page, "collections");
            DumpFieldValue(page, "currentTab");
            DumpFieldValue(page, "currentPage");
        }

        private static void DumpOptionsPage(IClickableMenu page)
        {
            Monitor.Log($"[GameMenuDiag] --- OptionsPage specific data ---", LogLevel.Info);

            // optionSlots
            try
            {
                var optionSlotsField = AccessTools.Field(page.GetType(), "optionSlots");
                if (optionSlotsField != null)
                {
                    var optionSlots = optionSlotsField.GetValue(page);
                    if (optionSlots is IList slotList)
                    {
                        Monitor.Log($"[GameMenuDiag] optionSlots: count={slotList.Count}", LogLevel.Info);
                        for (int i = 0; i < slotList.Count; i++)
                        {
                            var slot = slotList[i];
                            if (slot is ClickableComponent cc)
                                Monitor.Log($"[GameMenuDiag]   optionSlot[{i}] ID={cc.myID} name='{cc.name}' bounds=({cc.bounds.X},{cc.bounds.Y},{cc.bounds.Width},{cc.bounds.Height}) neighbors L={cc.leftNeighborID} R={cc.rightNeighborID} U={cc.upNeighborID} D={cc.downNeighborID}", LogLevel.Info);
                            else
                                Monitor.Log($"[GameMenuDiag]   optionSlot[{i}] type={slot?.GetType().Name ?? "null"}", LogLevel.Info);
                        }
                    }
                    else
                        Monitor.Log($"[GameMenuDiag] optionSlots: not IList, value={optionSlots}", LogLevel.Info);
                }
                else
                    Monitor.Log($"[GameMenuDiag] optionSlots: field not found", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[GameMenuDiag] optionSlots error: {ex.Message}", LogLevel.Error);
            }

            // options, currentItemIndex, scrollBar
            DumpFieldValue(page, "options");
            DumpFieldValue(page, "currentItemIndex");
            DumpFieldValue(page, "scrollBar");
        }

        /// <summary>Dump a single field's value by name, with null/collection handling.</summary>
        private static void DumpFieldValue(object obj, string fieldName)
        {
            try
            {
                var field = AccessTools.Field(obj.GetType(), fieldName);
                if (field == null)
                {
                    Monitor.Log($"[GameMenuDiag] {fieldName}: field not found", LogLevel.Info);
                    return;
                }

                var value = field.GetValue(obj);
                if (value == null)
                {
                    Monitor.Log($"[GameMenuDiag] {fieldName}: null", LogLevel.Info);
                    return;
                }

                if (value is ICollection coll)
                    Monitor.Log($"[GameMenuDiag] {fieldName}: {value.GetType().Name} count={coll.Count}", LogLevel.Info);
                else if (value is IList list)
                    Monitor.Log($"[GameMenuDiag] {fieldName}: {value.GetType().Name} count={list.Count}", LogLevel.Info);
                else
                    Monitor.Log($"[GameMenuDiag] {fieldName}: {value} (type={value.GetType().Name})", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[GameMenuDiag] {fieldName} error: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Enumerate ALL instance fields on the page and log name, type, and collection count.</summary>
        private static void DumpAllFields(object obj, string label)
        {
            Monitor.Log($"[GameMenuDiag] --- All fields on {label} ({obj.GetType().FullName}) ---", LogLevel.Info);

            try
            {
                var fields = obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Monitor.Log($"[GameMenuDiag] Total fields: {fields.Length}", LogLevel.Info);

                foreach (var field in fields)
                {
                    try
                    {
                        var value = field.GetValue(obj);
                        string valueStr;

                        if (value == null)
                        {
                            valueStr = "null";
                        }
                        else if (value is ICollection coll)
                        {
                            valueStr = $"count={coll.Count}";
                        }
                        else if (value is IList list)
                        {
                            valueStr = $"count={list.Count}";
                        }
                        else if (value is Array arr)
                        {
                            valueStr = $"length={arr.Length}";
                        }
                        else if (field.FieldType.IsValueType || value is string)
                        {
                            valueStr = value.ToString();
                        }
                        else
                        {
                            valueStr = $"({value.GetType().Name})";
                        }

                        bool isCollection = typeof(ICollection).IsAssignableFrom(field.FieldType)
                            || typeof(IList).IsAssignableFrom(field.FieldType)
                            || field.FieldType.IsArray;

                        Monitor.Log($"[GameMenuDiag]   {field.Name} : {field.FieldType.Name}{(isCollection ? " [collection]" : "")} = {valueStr}", LogLevel.Info);
                    }
                    catch (Exception ex)
                    {
                        Monitor.Log($"[GameMenuDiag]   {field.Name} : {field.FieldType.Name} = ERROR: {ex.Message}", LogLevel.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[GameMenuDiag] Error enumerating fields: {ex.Message}", LogLevel.Error);
            }
        }

        #endregion
    }
}
