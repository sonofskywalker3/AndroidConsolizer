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
        private static FieldInfo _socialEntriesField; // SocialPage.SocialEntries — for name→index mapping

        // Track slotPosition to detect right-stick scroll changes
        private static int _lastSocialSlotPosition = -1;

        // Save selected villager when gift log opens, restore on return
        private static int _savedSocialReturnIndex = -1;
        private static string _savedSocialReturnName = null; // InternalName — updated by ChangeCharacter postfix
        private static int _savedSocialSlotPosition = -1; // scroll position when gift log opened

        // Held-scroll acceleration: track direction, step size, and timing
        private static int _heldScrollDirection = 0; // -1=up, 0=none, 1=down
        private static int _heldScrollStep = 1; // 1=left stick/dpad, 3=right stick
        private static int _heldScrollStartTick = 0;
        private static int _lastAutoScrollTick = 0;
        private const int HeldScrollInitialDelay = 24; // ~400ms before acceleration starts
        private const int HeldScrollRepeatInterval = 8; // ~133ms between repeats (~2x manual speed)
        private const float StickEngageThreshold = 0.2f; // match game's button event threshold (not 0.5)
        private const int SocialRelStatusLift = 20; // px to move relationship status text up (shrinks bounds.Bottom)
        private const int SocialHeartsYAdjust = 12; // px to shift hearts down for vertical centering
        private const int SocialIconsYAdjust = 20; // px to shift gift/chat icons and dots down

        // Additional cached fields for NPC slot drawing (private layout positions)
        private static FieldInfo _socialNameXField;
        private static FieldInfo _socialHeartsXField;
        private static FieldInfo _socialGiftsXField;
        private static FieldInfo _socialTalkXField;
        private static FieldInfo _socialWidthModField;
        private static bool _npcDrawFieldsCached;
        private static FieldInfo _optionsBigFontsField; // Android-only Options.bigFonts

        // Cached reflection for GameMenu junimoNoteIcon (Community Center tab icon)
        private static FieldInfo _junimoNoteIconField;

        // Scrollbox tap simulation: two-frame tap gesture
        // Phase 0 = idle, 1 = receiveLeftClick pending, 2 = releaseLeftClick pending
        private static int _scrollboxTapPhase = 0;
        private static int _scrollboxTapX, _scrollboxTapY;

        // Right stick initial-press detection for social tab
        private static bool _prevRightStickEngaged = false;

        // Diagnostic: track child menu on SocialPage (gift log)
        private static bool _dumpedChildMenu = false;
        private static int _lastProfileMenuSnappedId = -999; // track snap changes in ProfileMenu

        // Cached reflection fields for CollectionsPage
        private static FieldInfo _collectionsField;       // Dictionary<int, List<List<ClickableTextureComponent>>>
        private static FieldInfo _collectionsCurrentTabField; // int currentTab (the sub-tab index)
        private static FieldInfo _currentlySelectedComponentField; // ClickableTextureComponent[]
        private static FieldInfo _collectionsMobSideTabsField; // Rectangle[]
        private static FieldInfo _collectionsNumTabsField;    // int
        private static FieldInfo _collectionsScrollAreaField;  // MobileScrollbox
        private static FieldInfo _collectionsNumInRowField;    // int
        private static FieldInfo _collectionsSideTabsField;    // Dictionary<int, ClickableTextureComponent>
        private static FieldInfo _collectionsRowField;         // int[] (row counts per tab)
        private static FieldInfo _collectionsNumRowsField;     // int
        private static FieldInfo _collectionsSliderVisibleField; // bool[]
        private static FieldInfo _collectionsSliderPercentField; // float[]
        private static FieldInfo _collectionsNewScrollbarField;  // MobileScrollbar
        private static FieldInfo _collectionsLetterviewerField;  // LetterViewerMenu
        private static FieldInfo _collectionsHighlightField;     // ClickableTextureComponent highlightTexture
        private static FieldInfo _collectionsSelectedItemIndexField; // int _selectedItemIndex

        // CraftingPage: save/restore selectedCraftingItem during draw to suppress red highlight
        private static FieldInfo _craftingSelectedItemField;
        private static ClickableTextureComponent _savedCraftingSelection;

        // PowersTab: reflection fields (Android-only type, not in PC DLL)
        private static Type _powersTabType;
        private static FieldInfo _powersHighlightField;     // ClickableTextureComponent highlightTexture
        private static FieldInfo _powersSelectedIndexField;  // int selectedIndex
        private static FieldInfo _powersPowersField;         // List<List<ClickableTextureComponent>> powers
        private static FieldInfo _powersCurrentPageField;    // int currentPage
        private static MethodInfo _powersDoSelectMethod;     // void doSelect(int index)

        // CollectionsPage navigation state
        private static bool _collectionsInTabMode = false;
        private static int _collectionsSelectedTabIndex = 0;

        // SkillsPage: cached reflection fields and methods
        private static FieldInfo _skillsSkillBarsField;      // List<ClickableTextureComponent> skillBars
        private static FieldInfo _skillsSkillAreasField;      // List<ClickableTextureComponent> skillAreas
        private static FieldInfo _skillsSpecialItemsField;    // List<ClickableTextureComponent> specialItems
        private static MethodInfo _skillsSetSkillBarTooltipMethod;
        private static MethodInfo _skillsSetSkillAreaTooltipMethod;
        private static MethodInfo _skillsSetSpecialItemTooltipMethod;
        private static MethodInfo _skillsHideTooltipMethod;
        private static FieldInfo _skillsHoverBoxField;        // Rectangle hoverBox
        private static FieldInfo _skillsWidthModField;        // float widthMod
        private static FieldInfo _skillsHeightModField;       // float heightMod
        private static bool _skillsFieldsCached;

        // MobileScrollbox.setYOffsetForScroll() method — sets yOffset AND syncs scrollbar visual
        private static MethodInfo _scrollboxSetYOffsetMethod;

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

                // Patch SocialPage.drawNPCSlot — full replacement to adjust per-element Y positions
                var drawNPCSlotMethod = AccessTools.Method(typeof(SocialPage), "drawNPCSlot", new[] { typeof(SpriteBatch), typeof(int) });
                if (drawNPCSlotMethod != null)
                {
                    harmony.Patch(
                        original: drawNPCSlotMethod,
                        prefix: new HarmonyMethod(typeof(GameMenuPatches), nameof(DrawNPCSlot_Prefix))
                    );
                }

                // Patch SocialPage.drawFarmerSlot — Height-only adjustment for relationship status lift
                var drawFarmerSlotMethod = AccessTools.Method(typeof(SocialPage), "drawFarmerSlot", new[] { typeof(SpriteBatch), typeof(int) });
                if (drawFarmerSlotMethod != null)
                {
                    harmony.Patch(
                        original: drawFarmerSlotMethod,
                        prefix: new HarmonyMethod(typeof(GameMenuPatches), nameof(DrawFarmerSlot_Prefix)),
                        postfix: new HarmonyMethod(typeof(GameMenuPatches), nameof(DrawFarmerSlot_Postfix))
                    );
                }

                // Patch ProfileMenu.ChangeCharacter to track villager switches (LB/RB in gift log)
                var changeCharMethod = AccessTools.Method(typeof(ProfileMenu), nameof(ProfileMenu.ChangeCharacter));
                if (changeCharMethod != null)
                {
                    harmony.Patch(
                        original: changeCharMethod,
                        postfix: new HarmonyMethod(typeof(GameMenuPatches), nameof(ChangeCharacter_Postfix))
                    );
                }

                // Patch ProfileMenu.draw to render gamepad cursor
                // ProfileMenu doesn't call drawMouse() — the game's main loop only draws the cursor
                // when no menu is active, so menus must draw their own. ShopMenu/ItemGrabMenu do this;
                // ProfileMenu doesn't because it was designed for touch input only.
                var profileDrawMethod = AccessTools.Method(typeof(ProfileMenu), "draw", new[] { typeof(SpriteBatch) });
                if (profileDrawMethod != null)
                {
                    harmony.Patch(
                        original: profileDrawMethod,
                        postfix: new HarmonyMethod(typeof(GameMenuPatches), nameof(ProfileMenu_Draw_Postfix))
                    );
                }

                // Patch CraftingPage.draw — prefix hides red highlight, postfix draws finger cursor
                var craftingDrawMethod = AccessTools.Method(typeof(CraftingPage), "draw", new[] { typeof(SpriteBatch) });
                if (craftingDrawMethod != null)
                {
                    harmony.Patch(
                        original: craftingDrawMethod,
                        prefix: new HarmonyMethod(typeof(GameMenuPatches), nameof(CraftingDraw_Prefix)),
                        postfix: new HarmonyMethod(typeof(GameMenuPatches), nameof(CraftingDraw_Postfix))
                    );
                }

                // Patch CollectionsPage.receiveGamePadButton — prefix to handle our own navigation
                var collectionsReceiveGPB = AccessTools.Method(typeof(CollectionsPage), nameof(CollectionsPage.receiveGamePadButton));
                if (collectionsReceiveGPB != null)
                {
                    harmony.Patch(
                        original: collectionsReceiveGPB,
                        prefix: new HarmonyMethod(typeof(GameMenuPatches), nameof(CollectionsReceiveGamePadButton_Prefix))
                    );
                }

                // Patch CollectionsPage.draw — prefix hides red highlight, postfix draws finger cursor
                var collectionsDrawMethod = AccessTools.Method(typeof(CollectionsPage), "draw", new[] { typeof(SpriteBatch) });
                if (collectionsDrawMethod != null)
                {
                    harmony.Patch(
                        original: collectionsDrawMethod,
                        prefix: new HarmonyMethod(typeof(GameMenuPatches), nameof(CollectionsDraw_Prefix)),
                        postfix: new HarmonyMethod(typeof(GameMenuPatches), nameof(CollectionsDraw_Postfix))
                    );
                }

                // Patch PowersTab.draw — prefix hides glow highlight, postfix draws finger cursor
                // PowersTab is Android-only (not in PC DLL), so use reflection
                _powersTabType = AccessTools.TypeByName("StardewValley.Menus.PowersTab");
                if (_powersTabType != null)
                {
                    _powersHighlightField = AccessTools.Field(_powersTabType, "highlightTexture");
                    _powersSelectedIndexField = AccessTools.Field(_powersTabType, "selectedIndex");
                    _powersPowersField = AccessTools.Field(_powersTabType, "powers");
                    _powersCurrentPageField = AccessTools.Field(_powersTabType, "currentPage");
                    _powersDoSelectMethod = AccessTools.Method(_powersTabType, "doSelect", new[] { typeof(int) });

                    var powersDrawMethod = AccessTools.Method(_powersTabType, "draw", new[] { typeof(SpriteBatch) });
                    if (powersDrawMethod != null)
                    {
                        harmony.Patch(
                            original: powersDrawMethod,
                            prefix: new HarmonyMethod(typeof(GameMenuPatches), nameof(PowersDraw_Prefix)),
                            postfix: new HarmonyMethod(typeof(GameMenuPatches), nameof(PowersDraw_Postfix))
                        );
                        Monitor.Log("PowersTab draw patches applied.", LogLevel.Trace);
                    }
                }
                else
                {
                    Monitor.Log("PowersTab type not found — skipping draw patches (PC build?).", LogLevel.Trace);
                }

                // Patch SkillsPage.receiveGamePadButton — replace vanilla's broken left/right-only handler
                var skillsPageType = typeof(SkillsPage);
                var skillsReceiveGPB = AccessTools.Method(skillsPageType, nameof(SkillsPage.receiveGamePadButton));
                if (skillsReceiveGPB != null)
                {
                    harmony.Patch(
                        original: skillsReceiveGPB,
                        prefix: new HarmonyMethod(typeof(GameMenuPatches), nameof(SkillsReceiveGamePadButton_Prefix))
                    );
                }

                // Patch SkillsPage.draw — postfix draws finger cursor + repositions tooltip
                var skillsDrawMethod = AccessTools.Method(skillsPageType, "draw", new[] { typeof(SpriteBatch) });
                if (skillsDrawMethod != null)
                {
                    harmony.Patch(
                        original: skillsDrawMethod,
                        prefix: new HarmonyMethod(typeof(GameMenuPatches), nameof(SkillsDraw_Prefix)),
                        postfix: new HarmonyMethod(typeof(GameMenuPatches), nameof(SkillsDraw_Postfix))
                    );
                }

                Monitor.Log("GameMenu patches applied.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply GameMenu patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Postfix on ProfileMenu.ChangeCharacter — tracks which villager the user
        /// switched to via LB/RB so we return to the correct one on gift log exit.
        /// </summary>
        private static void ChangeCharacter_Postfix(object __instance)
        {
            try
            {
                // Read __instance.Current.InternalName via reflection (ProfileMenu.Current is SocialPage.SocialEntry)
                var currentField = __instance.GetType().GetField("Current");
                var current = currentField?.GetValue(__instance);
                if (current != null)
                {
                    var name = current.GetType().GetField("InternalName")?.GetValue(current) as string;
                    if (name != null)
                    {
                        _savedSocialReturnName = name;
                        Monitor?.Log($"[GameMenu] ChangeCharacter: updated return name to '{name}'", LogLevel.Trace);
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GameMenu] Error in ChangeCharacter_Postfix: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Postfix on ProfileMenu.draw — draws the gamepad cursor.
        /// ProfileMenu (gift log) doesn't call drawMouse() because the Android port
        /// was designed for touch only. Without this, D-pad navigation works but
        /// there's no visible cursor to show which item is selected.
        /// </summary>
        private static void ProfileMenu_Draw_Postfix(IClickableMenu __instance, SpriteBatch b)
        {
            if (!ModEntry.Config.EnableGameMenuNavigation)
                return;
            __instance.drawMouse(b);
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
            else if (typeName == "CollectionsPage")
            {
                FixCollectionsPage(page);
                _lastFixedTab = tabIndex;
            }
            else if (typeName == "SkillsPage")
            {
                FixSkillsPage(page);
                _lastFixedTab = tabIndex;
            }
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

        // ===== SocialPage.drawNPCSlot — full replacement with per-element Y adjustments =====

        /// <summary>
        /// Cache private layout fields from SocialPage on first drawNPCSlot call.
        /// </summary>
        /// <summary>Snap vector to 4-pixel grid (Android-only Utility.To4 equivalent).</summary>
        private static Vector2 SnapTo4(Vector2 v)
            => new Vector2((int)(v.X / 4f) * 4, (int)(v.Y / 4f) * 4);

        /// <summary>Read Android-only Options.bigFonts via reflection (false on PC).</summary>
        private static bool GetBigFonts()
        {
            if (_optionsBigFontsField == null)
                _optionsBigFontsField = AccessTools.Field(typeof(Options), "bigFonts");
            return (bool?)_optionsBigFontsField?.GetValue(Game1.options) ?? false;
        }

        private static void CacheNPCDrawFields(object instance)
        {
            if (_npcDrawFieldsCached) return;
            var type = instance.GetType();
            _socialNameXField = AccessTools.Field(type, "nameX");
            _socialHeartsXField = AccessTools.Field(type, "heartsX");
            _socialGiftsXField = AccessTools.Field(type, "giftsX");
            _socialTalkXField = AccessTools.Field(type, "talkX");
            _socialWidthModField = AccessTools.Field(type, "widthMod");
            _npcDrawFieldsCached = true;
        }

        /// <summary>
        /// Replacement prefix for drawNPCSlot. Reproduces the original drawing logic
        /// from the decompiled Android source with adjusted Y positions:
        /// - Hearts: shifted down SocialHeartsYAdjust px for vertical centering
        /// - Gift/chat icons and dots: shifted down SocialIconsYAdjust px
        /// - Portrait, name: unchanged
        /// - Relationship status: lifted via SocialRelStatusLift (reduced Bottom)
        /// Returns false to skip the original method. Falls through on error.
        /// </summary>
        private static bool DrawNPCSlot_Prefix(object __instance, SpriteBatch b, int i)
        {
            try
            {
                CacheNPCDrawFields(__instance);

                var sprites = _socialSpritesField?.GetValue(__instance) as IList;
                var entries = _socialEntriesField?.GetValue(__instance) as IList;
                if (sprites == null || entries == null || i < 0 || i >= sprites.Count || i >= entries.Count)
                    return true;

                var sprite = sprites[i] as ClickableTextureComponent;
                var entryObj = entries[i];
                if (sprite == null || entryObj == null) return false;

                var menu = __instance as IClickableMenu;
                if (menu == null) return true;
                int slotHeight = (int)_socialSlotHeightField.GetValue(__instance);
                int Y = sprite.bounds.Y;

                // Culling (same as original)
                if (Y < menu.yPositionOnScreen - slotHeight || Y > menu.yPositionOnScreen + menu.height)
                    return false;

                var mainBox = (Rectangle)_socialMainBoxField.GetValue(__instance);
                int nameX = (int)(_socialNameXField?.GetValue(__instance) ?? 0);
                int heartsX = (int)(_socialHeartsXField?.GetValue(__instance) ?? 0);
                int giftsX = (int)(_socialGiftsXField?.GetValue(__instance) ?? 0);
                int talkX = (int)(_socialTalkXField?.GetValue(__instance) ?? 0);
                float widthMod = (float)(_socialWidthModField?.GetValue(__instance) ?? 1f);

                // Read SocialEntry fields via reflection
                var eType = entryObj.GetType();
                string internalName = eType.GetField("InternalName")?.GetValue(entryObj) as string ?? "";
                string displayName = eType.GetField("DisplayName")?.GetValue(entryObj) as string ?? "";
                bool isDatable = (bool)(eType.GetField("IsDatable")?.GetValue(entryObj) ?? false);
                bool isChild = (bool)(eType.GetField("IsChild")?.GetValue(entryObj) ?? false);
                var gender = (Gender)(eType.GetField("Gender")?.GetValue(entryObj) ?? Gender.Male);
                var friendship = eType.GetField("Friendship")?.GetValue(entryObj) as Friendship;

                bool isMarried = friendship?.IsMarried() ?? false;
                bool isRoommate = isMarried && ((bool)(eType.GetMethod("IsRoommateForCurrentPlayer")?.Invoke(entryObj, null) ?? false));
                bool isMarriedToAnyone = (bool)(eType.GetMethod("IsMarriedToAnyone")?.Invoke(entryObj, null) ?? false);
                bool isDating = friendship?.IsDating() ?? false;
                bool isDivorced = friendship?.IsDivorced() ?? false;

                // 1. Portrait (unchanged Y)
                sprite.draw(b);

                // 2. Name text (unchanged Y)
                float lineHeight = Game1.smallFont.MeasureString("W").Y;
                float langOff = (LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.ru ||
                                 LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.ko)
                                 ? (-lineHeight / 2f) : 0f;
                var nameFont = (displayName.Length <= 10 || Game1.uiViewport.Width > 1400)
                                ? Game1.dialogueFont : Game1.smallFont;
                bool bigFonts = GetBigFonts();
                Utility.drawTextWithShadow(b, displayName, nameFont,
                    new Vector2(nameX, (float)(Y + 48) + langOff - (float)(isDatable ? (bigFonts ? 40 : 24) : 20)),
                    Game1.textColor);

                // 3. Hearts (adjusted: +SocialHeartsYAdjust for centering)
                int heartLevel = Game1.player.getFriendshipHeartLevelForNPC(internalName);
                int maxHearts = Math.Max(Utility.GetMaximumHeartsForCharacter(Game1.getCharacterFromName(internalName)), 10);
                for (int j = 0; j < maxHearts; j++)
                {
                    int srcX = (j < heartLevel) ? 211 : 218;
                    if (isDatable && friendship != null && !isDating && !isMarried && j >= 8)
                        srcX = 211;

                    Color hc = Color.White;
                    if (isDatable && friendship != null && !isDating &&
                        !(sprite.hoverText?.Split('_')[0].Equals("true") ?? false) && !isMarried && j >= 8)
                        hc = Color.Black * 0.35f;

                    if (j < 10)
                        b.Draw(Game1.mouseCursors,
                            new Vector2((float)(mainBox.X + heartsX) + (float)(j * 32) * widthMod,
                                        Y + 64 - 28 + SocialHeartsYAdjust),
                            new Rectangle(srcX, 428, 7, 6), hc, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.88f);
                    else
                        b.Draw(Game1.mouseCursors,
                            new Vector2((float)(mainBox.X + heartsX) + (float)((j - 10) * 32) * widthMod,
                                        Y + 64 + SocialHeartsYAdjust),
                            new Rectangle(srcX, 428, 7, 6), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.88f);
                }

                // 4. Relationship status text (lifted via SocialRelStatusLift)
                if (isDatable || isRoommate)
                {
                    string relText;
                    if (isRoommate)
                        relText = (gender == Gender.Female)
                            ? Game1.content.LoadString("Strings\\StringsFromCSFiles:SocialPage_Relationship_Housemate_Female")
                            : Game1.content.LoadString("Strings\\StringsFromCSFiles:SocialPage_Relationship_Housemate_Male");
                    else if (isMarried)
                        relText = (gender == Gender.Female)
                            ? Game1.content.LoadString("Strings\\StringsFromCSFiles:SocialPage_Relationship_Wife")
                            : Game1.content.LoadString("Strings\\StringsFromCSFiles:SocialPage_Relationship_Husband");
                    else if (isMarriedToAnyone)
                        relText = (gender == Gender.Female)
                            ? Game1.content.LoadString("Strings\\UI:SocialPage_Relationship_MarriedToOtherPlayer_FemaleNpc")
                            : Game1.content.LoadString("Strings\\UI:SocialPage_Relationship_MarriedToOtherPlayer_MaleNpc");
                    else if (!Game1.player.isMarriedOrRoommates() && isDating)
                        relText = (gender == Gender.Female)
                            ? Game1.content.LoadString("Strings\\StringsFromCSFiles:SocialPage_Relationship_Girlfriend")
                            : Game1.content.LoadString("Strings\\StringsFromCSFiles:SocialPage_Relationship_Boyfriend");
                    else if (isDivorced)
                        relText = (gender == Gender.Female)
                            ? Game1.content.LoadString("Strings\\StringsFromCSFiles:SocialPage_Relationship_ExWife")
                            : Game1.content.LoadString("Strings\\StringsFromCSFiles:SocialPage_Relationship_ExHusband");
                    else
                        relText = (gender == Gender.Female)
                            ? Game1.content.LoadString("Strings\\StringsFromCSFiles:SocialPage_Relationship_Single_Female")
                            : Game1.content.LoadString("Strings\\StringsFromCSFiles:SocialPage_Relationship_Single_Male");

                    int parseWidth = (IClickableMenu.borderWidth * 3 + 128 - 40 + 192) / 2;
                    relText = Game1.parseText(relText, Game1.smallFont, parseWidth);
                    var textSize = Game1.smallFont.MeasureString(relText);
                    int adjustedBottom = Y + sprite.bounds.Height - SocialRelStatusLift;
                    Utility.drawTextWithShadow(b, relText, Game1.smallFont,
                        new Vector2(nameX, (float)adjustedBottom - (textSize.Y - lineHeight) - (float)(bigFonts ? 16 : 0)),
                        Game1.textColor);
                }

                // 5. Gift/chat icons and dots (adjusted: +SocialIconsYAdjust)
                if (!isMarriedToAnyone && !isChild)
                {
                    // Gift icon
                    b.Draw(Game1.mouseCursors, SnapTo4(new Vector2(mainBox.X + giftsX, Y - 4 + SocialIconsYAdjust)),
                        new Rectangle(229, 410, 14, 14), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.88f);
                    // Gift dot 1
                    b.Draw(Game1.mouseCursors, SnapTo4(new Vector2(mainBox.X + giftsX - 12, Y + 32 + 20 + SocialIconsYAdjust)),
                        new Rectangle(227 + ((friendship != null && friendship.GiftsThisWeek == 2) ? 9 : 0), 425, 9, 9),
                        Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.88f);
                    // Gift dot 2
                    b.Draw(Game1.mouseCursors, SnapTo4(new Vector2(mainBox.X + giftsX + 32, Y + 32 + 20 + SocialIconsYAdjust)),
                        new Rectangle(227 + ((friendship != null && friendship.GiftsThisWeek >= 1) ? 9 : 0), 425, 9, 9),
                        Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.88f);
                    // Chat icon
                    b.Draw(Game1.mouseCursors2, SnapTo4(new Vector2(mainBox.X + talkX, Y + SocialIconsYAdjust)),
                        new Rectangle(180, 175, 13, 11), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.88f);
                    // Chat dot
                    b.Draw(Game1.mouseCursors, SnapTo4(new Vector2(mainBox.X + talkX + 8, Y + 32 + 20 + SocialIconsYAdjust)),
                        new Rectangle(227 + ((friendship != null && friendship.TalkedToToday) ? 9 : 0), 425, 9, 9),
                        Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.88f);
                }

                // 6. Ring/bouquet icon (unchanged Y, next to name)
                if (isMarried)
                {
                    if (!isRoommate || internalName == "Krobus")
                    {
                        b.Draw(Game1.objectSpriteSheet,
                            SnapTo4(new Vector2((float)nameX + Game1.dialogueFont.MeasureString(displayName).X, Y)),
                            Game1.getSourceRectForStandardTileSheet(Game1.objectSpriteSheet, isRoommate ? 808 : 460, 16, 16),
                            Color.White, 0f, Vector2.Zero, 2f, SpriteEffects.None, 0.88f);
                    }
                }
                else if (isDating)
                {
                    b.Draw(Game1.objectSpriteSheet,
                        SnapTo4(new Vector2((float)nameX + Game1.dialogueFont.MeasureString(displayName).X, Y)),
                        Game1.getSourceRectForStandardTileSheet(Game1.objectSpriteSheet, isRoommate ? 808 : 458, 16, 16),
                        Color.White, 0f, Vector2.Zero, 2f, SpriteEffects.None, 0.88f);
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[SocialDraw] DrawNPCSlot_Prefix error: {ex.Message}", LogLevel.Error);
                return true; // fall through to original on error
            }
            return false; // skip original
        }

        // ===== SocialPage.drawFarmerSlot — Height-only adjustment for relationship status =====

        private static void DrawFarmerSlot_Prefix(object __instance, int i)
        {
            try
            {
                var sprites = _socialSpritesField?.GetValue(__instance) as IList;
                if (sprites == null || i < 0 || i >= sprites.Count) return;

                var sprite = sprites[i] as ClickableComponent;
                if (sprite == null) return;

                sprite.bounds = new Rectangle(sprite.bounds.X, sprite.bounds.Y,
                    sprite.bounds.Width, sprite.bounds.Height - SocialRelStatusLift);
            }
            catch { }
        }

        private static void DrawFarmerSlot_Postfix(object __instance, int i)
        {
            try
            {
                var sprites = _socialSpritesField?.GetValue(__instance) as IList;
                if (sprites == null || i < 0 || i >= sprites.Count) return;

                var sprite = sprites[i] as ClickableComponent;
                if (sprite == null) return;

                sprite.bounds = new Rectangle(sprite.bounds.X, sprite.bounds.Y,
                    sprite.bounds.Width, sprite.bounds.Height + SocialRelStatusLift);
            }
            catch { }
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
                    _socialEntriesField = AccessTools.Field(pageType, "SocialEntries");
                    _socialSelectSlotMethod = pageType.GetMethod("_SelectSlot", BindingFlags.Instance | BindingFlags.NonPublic);

                    // Cache setYOffsetForScroll method from MobileScrollbox (sets yOffset AND syncs scrollbar)
                    if (_socialScrollAreaField != null)
                    {
                        var scrollArea = _socialScrollAreaField.GetValue(page);
                        if (scrollArea != null)
                        {
                            _scrollboxSetYOffsetMethod = scrollArea.GetType().GetMethod("setYOffsetForScroll", new[] { typeof(int) });
                            if (_scrollboxSetYOffsetMethod != null)
                                Monitor?.Log($"[SocialDiag] Found scrollbox setYOffsetForScroll method", LogLevel.Trace);
                            else
                                Monitor?.Log($"[SocialDiag] WARNING: setYOffsetForScroll method not found on MobileScrollbox", LogLevel.Warn);
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
                // Don't clear here — FixSocialPage may be called twice
                // (once in ChangeTab_Postfix during constructor, once in OnGameMenuOpened).
                // Cleared on next user input in HandleSocialInput instead.
                int snapTargetIndex = slotPosition;

                // If ChangeCharacter updated the name (LB/RB in gift log), resolve name→index
                if (_savedSocialReturnName != null && _socialEntriesField != null)
                {
                    var entries = _socialEntriesField.GetValue(page) as IList;
                    if (entries != null)
                    {
                        for (int i = 0; i < entries.Count; i++)
                        {
                            var entry = entries[i];
                            var name = entry?.GetType().GetField("InternalName")?.GetValue(entry) as string;
                            if (name == _savedSocialReturnName)
                            {
                                _savedSocialReturnIndex = i;
                                Monitor?.Log($"[GameMenu] SocialPage: resolved '{_savedSocialReturnName}' to slot[{i}]", LogLevel.Trace);
                                break;
                            }
                        }
                    }
                }

                if (_savedSocialReturnIndex >= 0 && _savedSocialReturnIndex < charSlots.Count)
                {
                    snapTargetIndex = _savedSocialReturnIndex;

                    // Prefer restoring the original scroll position (so the villager stays
                    // at the same visual position, not snapped to top of the list).
                    // Fall back to scrolling-to-visible if the target is out of range
                    // (e.g. LB/RB moved to a far-away villager in the gift log).
                    int maxSlotPos = charSlots.Count - visibleSlots;
                    if (_savedSocialSlotPosition >= 0
                        && snapTargetIndex >= _savedSocialSlotPosition
                        && snapTargetIndex < _savedSocialSlotPosition + visibleSlots)
                    {
                        // Target is visible at saved scroll position — restore it
                        slotPosition = Math.Max(0, Math.Min(_savedSocialSlotPosition, maxSlotPos));
                    }
                    else
                    {
                        // Target not visible at saved position — scroll to make it visible
                        slotPosition = Math.Max(0, Math.Min(snapTargetIndex, maxSlotPos));
                    }

                    _socialSlotPositionField?.SetValue(page, slotPosition);

                    // Sync MobileScrollbox yOffset + scrollbar visual
                    if (_socialScrollAreaField != null && _scrollboxSetYOffsetMethod != null)
                    {
                        var scrollArea = _socialScrollAreaField.GetValue(page);
                        if (scrollArea != null)
                            _scrollboxSetYOffsetMethod.Invoke(scrollArea, new object[] { -(slotPosition * slotHeight) });
                    }

                    Monitor?.Log($"[GameMenu] SocialPage: restoring position to slot[{snapTargetIndex}], slotPosition={slotPosition} (savedSlotPos={_savedSocialSlotPosition})", LogLevel.Trace);
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
                    else
                    {
                        // At first tab — cycle to Community Center if available
                        ClickJunimoNoteIcon(__instance);
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
                    else
                    {
                        // At last tab — cycle to Community Center if available
                        ClickJunimoNoteIcon(__instance);
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
                if (typeName == "CollectionsPage")
                    return true; // handled by CollectionsPage.receiveGamePadButton prefix

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
            bool rightStickEngaged = Math.Abs(rawRightY) > StickEngageThreshold;

            if (rightStickEngaged && !_prevRightStickEngaged && _heldScrollDirection == 0)
            {
                // Newly engaged — fire 3-slot jump
                int dir = rawRightY < -StickEngageThreshold ? 1 : -1; // Y < 0 = stick pushed down
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

                // Use StickEngageThreshold (0.2) to match the game's own button event threshold.
                // The game fires LeftThumbstickDown at ~0.2-0.4 deflection, so our held check
                // must use the same threshold or it clears immediately on gentle pushes.
                bool downHeld = padState.DPad.Down == ButtonState.Pressed
                             || padState.ThumbSticks.Left.Y < -StickEngageThreshold
                             || rawRStickY < -StickEngageThreshold;
                bool upHeld = padState.DPad.Up == ButtonState.Pressed
                           || padState.ThumbSticks.Left.Y > StickEngageThreshold
                           || rawRStickY > StickEngageThreshold;

                int currentDir = downHeld ? 1 : upHeld ? -1 : 0;

                if (currentDir == 0)
                {
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
            {
                _savedSocialReturnIndex = -1;
                _savedSocialReturnName = null;
                _savedSocialSlotPosition = -1;
            }

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
                                    // Also save name so ChangeCharacter postfix can update it
                                    if (_socialEntriesField != null)
                                    {
                                        var entries = _socialEntriesField.GetValue(page) as IList;
                                        if (entries != null && i < entries.Count)
                                        {
                                            var entry = entries[i];
                                            var nameProp = entry?.GetType().GetField("InternalName");
                                            _savedSocialReturnName = nameProp?.GetValue(entry) as string;
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }

                    // Save scroll position so we can restore the view (not just the slot)
                    if (_socialSlotPositionField != null)
                        _savedSocialSlotPosition = (int)_socialSlotPositionField.GetValue(page);

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

                    // Sync MobileScrollbox yOffsetForScroll + scrollbar visual
                    if (_socialScrollAreaField != null && _scrollboxSetYOffsetMethod != null)
                    {
                        var scrollArea = _socialScrollAreaField.GetValue(page);
                        if (scrollArea != null)
                            _scrollboxSetYOffsetMethod.Invoke(scrollArea, new object[] { -(newSlotPos * slotHeight) });
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
        /// Click the junimoNoteIcon (Community Center tab) on the GameMenu if it exists.
        /// The icon is outside the pages list — it's a separate ClickableTextureComponent
        /// that opens JunimoNoteMenu when clicked.
        /// </summary>
        private static void ClickJunimoNoteIcon(GameMenu menu)
        {
            try
            {
                if (_junimoNoteIconField == null)
                    _junimoNoteIconField = AccessTools.Field(typeof(GameMenu), "junimoNoteIcon");

                var icon = _junimoNoteIconField?.GetValue(menu) as ClickableTextureComponent;
                if (icon != null)
                {
                    Game1.playSound("smallSelect");
                    menu.receiveLeftClick(icon.bounds.X, icon.bounds.Y);
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GameMenu] Error clicking junimoNoteIcon: {ex.Message}", LogLevel.Error);
            }
        }

        // ===== CollectionsPage navigation =====

        /// <summary>
        /// Fix the Collections tab on entry. Cache reflection fields, reset navigation state,
        /// and snap cursor to first item in current sub-tab.
        /// </summary>
        private static void FixCollectionsPage(IClickableMenu page)
        {
            try
            {
                // Cache reflection fields on first use
                if (_collectionsField == null)
                {
                    var pageType = page.GetType();
                    _collectionsField = AccessTools.Field(pageType, "collections");
                    _collectionsCurrentTabField = AccessTools.Field(pageType, "currentTab");
                    _currentlySelectedComponentField = AccessTools.Field(pageType, "currentlySelectedComponent");
                    _collectionsMobSideTabsField = AccessTools.Field(pageType, "mobSideTabs");
                    _collectionsNumTabsField = AccessTools.Field(pageType, "numTabs");
                    _collectionsScrollAreaField = AccessTools.Field(pageType, "scrollArea");
                    _collectionsNumInRowField = AccessTools.Field(pageType, "numInRow");
                    _collectionsSideTabsField = AccessTools.Field(pageType, "sideTabs");
                    _collectionsRowField = AccessTools.Field(pageType, "row");
                    _collectionsNumRowsField = AccessTools.Field(pageType, "numRows");
                    _collectionsSliderVisibleField = AccessTools.Field(pageType, "sliderVisible");
                    _collectionsSliderPercentField = AccessTools.Field(pageType, "sliderPercent");
                    _collectionsNewScrollbarField = AccessTools.Field(pageType, "newScrollbar");
                    _collectionsLetterviewerField = AccessTools.Field(pageType, "letterviewerSubMenu");
                    _collectionsHighlightField = AccessTools.Field(pageType, "highlightTexture");
                    _collectionsSelectedItemIndexField = AccessTools.Field(pageType, "_selectedItemIndex");
                }

                // Reset to items mode on tab entry
                _collectionsInTabMode = false;
                int currentSubTab = (int)(_collectionsCurrentTabField?.GetValue(page) ?? 0);
                _collectionsSelectedTabIndex = currentSubTab;

                // No need to set mouse position here — CollectionsDraw_Postfix draws the cursor
                // at the correct position using bounds that are only valid during draw().

                Monitor?.Log($"[GameMenu] CollectionsPage: fixed, currentSubTab={currentSubTab}", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GameMenu] Error fixing CollectionsPage: {ex.Message}\n{ex.StackTrace}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Prefix on CollectionsPage.receiveGamePadButton — replaces vanilla A-cycles-tabs with
        /// proper grid navigation + side tab selection.
        /// </summary>
        private static bool CollectionsReceiveGamePadButton_Prefix(CollectionsPage __instance, Buttons b)
        {
            try
            {
                if (!ModEntry.Config.EnableGameMenuNavigation)
                    return true;

                // Let letterviewerSubMenu handle its own input
                var letterViewer = _collectionsLetterviewerField?.GetValue(__instance) as LetterViewerMenu;
                if (letterViewer != null)
                {
                    // B closes the letter viewer
                    if (b == Buttons.B)
                    {
                        _collectionsLetterviewerField.SetValue(__instance, null);
                        return false;
                    }
                    // Pass through to letter viewer
                    letterViewer.receiveGamePadButton(b);
                    return false;
                }

                return HandleCollectionsInput(__instance, b);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GameMenu] Error in CollectionsReceiveGamePadButton_Prefix: {ex.Message}", LogLevel.Error);
                return true;
            }
        }

        /// <summary>
        /// Handle input on the Collections tab. Returns false to skip original method.
        /// Manages two modes: items grid navigation and side tab selection.
        /// </summary>
        private static bool HandleCollectionsInput(CollectionsPage page, Buttons b)
        {
            int currentSubTab = (int)(_collectionsCurrentTabField?.GetValue(page) ?? 0);
            int numTabs = (int)(_collectionsNumTabsField?.GetValue(page) ?? 6);
            var collections = _collectionsField?.GetValue(page) as Dictionary<int, List<List<ClickableTextureComponent>>>;
            var mobSideTabs = _collectionsMobSideTabsField?.GetValue(page) as Rectangle[];
            int numInRow = (int)(_collectionsNumInRowField?.GetValue(page) ?? 4);

            if (collections == null || mobSideTabs == null)
                return true;

            if (_collectionsInTabMode)
            {
                // === TAB MODE: navigating side tabs ===
                switch (b)
                {
                    case Buttons.DPadUp:
                    case Buttons.LeftThumbstickUp:
                    {
                        if (_collectionsSelectedTabIndex > 0)
                        {
                            _collectionsSelectedTabIndex--;
                            Game1.playSound("shiny4");
                        }
                        return false;
                    }
                    case Buttons.DPadDown:
                    case Buttons.LeftThumbstickDown:
                    {
                        if (_collectionsSelectedTabIndex < numTabs - 1)
                        {
                            _collectionsSelectedTabIndex++;
                            Game1.playSound("shiny4");
                        }
                        return false;
                    }
                    case Buttons.DPadRight:
                    case Buttons.LeftThumbstickRight:
                    {
                        // Return to items mode
                        _collectionsInTabMode = false;
                        Game1.playSound("shiny4");
                        return false;
                    }
                    case Buttons.A:
                    {
                        // Switch to the selected sub-tab
                        if (_collectionsSelectedTabIndex != currentSubTab)
                        {
                            _collectionsCurrentTabField?.SetValue(page, _collectionsSelectedTabIndex);

                            // Call OnChangeCollectionsTab via reflection
                            var onChangeMethod = page.GetType().GetMethod("OnChangeCollectionsTab", BindingFlags.Instance | BindingFlags.NonPublic);
                            onChangeMethod?.Invoke(page, null);
                        }
                        // Switch to items mode — draw postfix will position cursor
                        _collectionsInTabMode = false;
                        return false;
                    }
                    case Buttons.B:
                        return true; // let B pass through for menu close
                    default:
                        return false; // block all other input in tab mode
                }
            }
            else
            {
                // === ITEMS MODE: navigating the collection items grid ===
                switch (b)
                {
                    case Buttons.DPadUp:
                    case Buttons.LeftThumbstickUp:
                        NavigateCollectionsItem(page, collections, currentSubTab, numInRow, 0, -1);
                        return false;

                    case Buttons.DPadDown:
                    case Buttons.LeftThumbstickDown:
                        NavigateCollectionsItem(page, collections, currentSubTab, numInRow, 0, 1);
                        return false;

                    case Buttons.DPadLeft:
                    case Buttons.LeftThumbstickLeft:
                    {
                        // Check if at leftmost column — switch to tab mode
                        int idx = GetSelectedItemIndex(page, collections, currentSubTab);
                        int col = idx % numInRow;
                        if (col == 0)
                        {
                            // Enter tab mode — draw postfix will position cursor on side tab
                            _collectionsInTabMode = true;
                            _collectionsSelectedTabIndex = currentSubTab;
                            Game1.playSound("shiny4");
                        }
                        else
                        {
                            NavigateCollectionsItem(page, collections, currentSubTab, numInRow, -1, 0);
                        }
                        return false;
                    }

                    case Buttons.DPadRight:
                    case Buttons.LeftThumbstickRight:
                        NavigateCollectionsItem(page, collections, currentSubTab, numInRow, 1, 0);
                        return false;

                    case Buttons.A:
                    {
                        // A on letter tab: open letter viewer
                        if (currentSubTab == 6)
                        {
                            var selectedArr = _currentlySelectedComponentField?.GetValue(page) as ClickableTextureComponent[];
                            if (selectedArr != null && selectedArr[currentSubTab] != null)
                            {
                                var comp = selectedArr[currentSubTab];
                                var dictionary = Game1.content.Load<Dictionary<string, string>>("Data\\mail");
                                string mailKey = comp.name.Split(' ')[0];
                                if (dictionary.ContainsKey(mailKey))
                                {
                                    _collectionsLetterviewerField?.SetValue(page,
                                        new LetterViewerMenu(dictionary[mailKey], mailKey, fromCollection: true));
                                }
                            }
                        }
                        // For other tabs, A does nothing special (item info already shows on nav)
                        return false;
                    }

                    case Buttons.B:
                        return true; // let B pass through for menu close

                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// Get the flat index of the currently selected item in the collections grid.
        /// </summary>
        private static int GetSelectedItemIndex(CollectionsPage page,
            Dictionary<int, List<List<ClickableTextureComponent>>> collections, int subTab)
        {
            var selectedArr = _currentlySelectedComponentField?.GetValue(page) as ClickableTextureComponent[];
            if (selectedArr == null || subTab >= selectedArr.Length || selectedArr[subTab] == null)
                return 0;

            var current = selectedArr[subTab];
            if (!collections.ContainsKey(subTab))
                return 0;

            int idx = 0;
            foreach (var row in collections[subTab])
            {
                foreach (var item in row)
                {
                    if (item == current)
                        return idx;
                    idx++;
                }
            }
            return 0;
        }

        /// <summary>
        /// Navigate the collections grid by dx/dy. Handles row/column math, clamping, and scrolling.
        /// </summary>
        private static void NavigateCollectionsItem(CollectionsPage page,
            Dictionary<int, List<List<ClickableTextureComponent>>> collections,
            int subTab, int numInRow, int dx, int dy)
        {
            try
            {
                if (!collections.ContainsKey(subTab) || collections[subTab].Count == 0)
                    return;

                int count = collections[subTab][0].Count; // items per row
                int numRows = collections[subTab].Count;
                int totalItems = (numRows - 1) * count + collections[subTab][numRows - 1].Count;

                int idx = GetSelectedItemIndex(page, collections, subTab);

                // Apply movement
                if (dy != 0)
                    idx += dy * count;
                if (dx != 0)
                    idx += dx;

                // Clamp
                if (idx < 0) idx = 0;
                if (idx >= totalItems) idx = totalItems - 1;

                // Convert flat index to row/col
                int rowIdx = idx / count;
                int colIdx = idx - rowIdx * count;

                // Bounds check
                if (rowIdx >= collections[subTab].Count)
                    return;
                if (colIdx >= collections[subTab][rowIdx].Count)
                    return;

                var newItem = collections[subTab][rowIdx][colIdx];
                var selectedArr = _currentlySelectedComponentField?.GetValue(page) as ClickableTextureComponent[];
                if (selectedArr != null && subTab < selectedArr.Length)
                    selectedArr[subTab] = newItem;

                // Sync the game's internal _selectedItemIndex so it matches our selection.
                // The game derives currentlySelectedComponent from this index in receiveGamePadButton;
                // keeping them in sync prevents stale state if the game reads the index elsewhere.
                _collectionsSelectedItemIndexField?.SetValue(page, idx);

                // Update info panel via ShowSelectectItemInfo
                var showInfoMethod = page.GetType().GetMethod("ShowSelectectItemInfo", BindingFlags.Instance | BindingFlags.NonPublic);
                showInfoMethod?.Invoke(page, new object[] { newItem.name });

                // Handle scrolling — check if new row is visible
                var scrollArea = _collectionsScrollAreaField?.GetValue(page);
                if (scrollArea != null)
                {
                    var scrollType = scrollArea.GetType();
                    var getYOffset = scrollType.GetMethod("getYOffsetForScroll");
                    var setYOffset = scrollType.GetMethod("setYOffsetForScroll", new[] { typeof(int) });
                    var boundsProp = scrollType.GetProperty("Bounds");

                    if (getYOffset != null && setYOffset != null)
                    {
                        int yOffset = (int)getYOffset.Invoke(scrollArea, null);
                        int itemSize = 128;
                        int visibleTopRow = (int)Math.Floor((float)(-yOffset) / itemSize);
                        int scrollBoundsHeight = 0;
                        if (boundsProp != null)
                        {
                            var scrollBounds = (Rectangle)boundsProp.GetValue(scrollArea);
                            scrollBoundsHeight = scrollBounds.Height;
                        }
                        else
                        {
                            // Fallback: read Bounds field
                            var boundsF = AccessTools.Field(scrollType, "Bounds");
                            if (boundsF != null)
                            {
                                var scrollBounds = (Rectangle)boundsF.GetValue(scrollArea);
                                scrollBoundsHeight = scrollBounds.Height;
                            }
                        }
                        int visibleBottomRow = visibleTopRow + (int)Math.Floor((float)scrollBoundsHeight / itemSize) - 1;

                        var maxYOffsetField = AccessTools.Field(scrollType, "maxYOffset");
                        int maxYOffset = 0;
                        if (maxYOffsetField != null)
                            maxYOffset = (int)maxYOffsetField.GetValue(scrollArea);
                        else
                        {
                            var getMaxMethod = scrollType.GetMethod("getMaxYOffset");
                            if (getMaxMethod != null)
                                maxYOffset = (int)getMaxMethod.Invoke(scrollArea, null);
                        }

                        if (rowIdx > visibleBottomRow)
                        {
                            int newYOffset = Math.Max(-maxYOffset, yOffset - itemSize);
                            setYOffset.Invoke(scrollArea, new object[] { newYOffset });
                        }
                        else if (rowIdx < visibleTopRow)
                        {
                            int newYOffset = Math.Min(0, yOffset + itemSize);
                            setYOffset.Invoke(scrollArea, new object[] { newYOffset });
                        }
                    }
                }

                // Cursor position is handled by CollectionsDraw_Postfix using bounds
                // that are only correct during draw() — don't set mouse here.
                Game1.playSound("shiny4");
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GameMenu] Error in NavigateCollectionsItem: {ex.Message}\n{ex.StackTrace}", LogLevel.Error);
            }
        }

        // SnapToSideTab / SnapToCurrentItem removed — cursor position is handled
        // entirely in CollectionsDraw_Postfix where item bounds.Y is correct.
        // Navigation methods just update _collectionsInTabMode / _collectionsSelectedTabIndex
        // and the selected component; the draw postfix reads these to place the cursor.

        /// <summary>
        /// Prefix on CollectionsPage.draw — hides the red highlight box so only the finger cursor shows.
        /// The game draws highlightTexture around currentlySelectedComponent in draw() lines 977-992.
        /// ClickableTextureComponent.draw() checks visible, so setting it false suppresses the draw.
        /// </summary>
        private static void CollectionsDraw_Prefix(CollectionsPage __instance)
        {
            if (!ModEntry.Config.EnableGameMenuNavigation)
                return;

            var highlight = _collectionsHighlightField?.GetValue(__instance) as ClickableTextureComponent;
            if (highlight != null)
                highlight.visible = false;
        }

        /// <summary>
        /// Postfix on CollectionsPage.draw — draws the finger cursor at the correct position.
        ///
        /// We draw at the tracked position (item or tab) rather than Game1.getMouseX/Y()
        /// because collection item bounds.Y is only correct during draw() — it's set to 0
        /// in the constructor and recalculated dynamically each frame in draw().
        /// </summary>
        private static void CollectionsDraw_Postfix(CollectionsPage __instance, SpriteBatch b)
        {
            if (!ModEntry.Config.EnableGameMenuNavigation)
                return;

            // Don't draw cursor if letter viewer is open (it draws its own UI)
            var letterViewer = _collectionsLetterviewerField?.GetValue(__instance) as LetterViewerMenu;
            if (letterViewer != null)
                return;

            if (!Game1.options.gamepadControls || Game1.options.hardwareCursor)
                return;

            // Determine cursor position based on current mode
            int cursorX, cursorY;

            if (_collectionsInTabMode)
            {
                // Tab mode: cursor on the side tab
                var mobSideTabs = _collectionsMobSideTabsField?.GetValue(__instance) as Rectangle[];
                if (mobSideTabs == null || _collectionsSelectedTabIndex >= mobSideTabs.Length)
                    return;
                var tab = mobSideTabs[_collectionsSelectedTabIndex];
                cursorX = tab.Center.X;
                cursorY = tab.Center.Y;
            }
            else
            {
                // Items mode: cursor on the currently selected item
                // bounds.Y is CORRECT here because draw() just recalculated it
                int currentSubTab = (int)(_collectionsCurrentTabField?.GetValue(__instance) ?? 0);
                var selectedArr = _currentlySelectedComponentField?.GetValue(__instance) as ClickableTextureComponent[];
                if (selectedArr == null || currentSubTab >= selectedArr.Length || selectedArr[currentSubTab] == null)
                    return;
                var comp = selectedArr[currentSubTab];
                cursorX = comp.bounds.Center.X;
                cursorY = comp.bounds.Center.Y;
            }

            b.Draw(Game1.mouseCursors,
                new Vector2(cursorX, cursorY),
                Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 44, 16, 16),
                Color.White, 0f, Vector2.Zero,
                4f + Game1.dialogueButtonScale / 150f,
                SpriteEffects.None, 1f);

            // Restore highlightTexture visibility (in case feature is toggled off mid-session)
            var highlight = _collectionsHighlightField?.GetValue(__instance) as ClickableTextureComponent;
            if (highlight != null)
                highlight.visible = true;
        }

        // ===== CraftingPage draw patches =====

        /// <summary>
        /// Prefix on CraftingPage.draw — temporarily null selectedCraftingItem to suppress
        /// the red highlight box drawn by drawRecipes(). The game draws a 9-sliced border from
        /// mobileSpriteSheet around the selected recipe; with selectedCraftingItem null, the
        /// comparison (selectedCraftingItem == recipeImage[j,i]) is always false.
        /// </summary>
        private static void CraftingDraw_Prefix(CraftingPage __instance)
        {
            if (!ModEntry.Config.EnableGameMenuNavigation)
                return;

            if (_craftingSelectedItemField == null)
                _craftingSelectedItemField = AccessTools.Field(__instance.GetType(), "selectedCraftingItem");

            _savedCraftingSelection = _craftingSelectedItemField?.GetValue(__instance) as ClickableTextureComponent;
            if (_savedCraftingSelection != null)
                _craftingSelectedItemField?.SetValue(__instance, null);
        }

        /// <summary>
        /// Postfix on CraftingPage.draw — restore selectedCraftingItem and draw the finger cursor
        /// at the selected recipe's position. bounds.Y was recalculated during drawRecipes().
        /// </summary>
        private static void CraftingDraw_Postfix(CraftingPage __instance, SpriteBatch b)
        {
            if (!ModEntry.Config.EnableGameMenuNavigation)
                return;

            if (_savedCraftingSelection != null && _craftingSelectedItemField != null)
            {
                _craftingSelectedItemField.SetValue(__instance, _savedCraftingSelection);

                if (Game1.options.gamepadControls && !Game1.options.hardwareCursor)
                {
                    int cursorX = _savedCraftingSelection.bounds.Center.X;
                    int cursorY = _savedCraftingSelection.bounds.Center.Y;

                    b.Draw(Game1.mouseCursors,
                        new Vector2(cursorX, cursorY),
                        Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 44, 16, 16),
                        Color.White, 0f, Vector2.Zero,
                        4f + Game1.dialogueButtonScale / 150f,
                        SpriteEffects.None, 1f);
                }

                _savedCraftingSelection = null;
            }
        }

        // ===== SkillsPage navigation + cursor =====

        /// <summary>Cache reflection fields for SkillsPage on first use.</summary>
        private static void CacheSkillsFields(IClickableMenu page)
        {
            if (_skillsFieldsCached) return;
            var t = page.GetType();
            _skillsSkillBarsField = AccessTools.Field(t, "skillBars");
            _skillsSkillAreasField = AccessTools.Field(t, "skillAreas");
            _skillsSpecialItemsField = AccessTools.Field(t, "specialItems");
            _skillsSetSkillBarTooltipMethod = AccessTools.Method(t, "SetSkillBarTooltip", new[] { typeof(ClickableTextureComponent) });
            _skillsSetSkillAreaTooltipMethod = AccessTools.Method(t, "SetSkillAreaTooltip", new[] { typeof(ClickableTextureComponent) });
            _skillsSetSpecialItemTooltipMethod = AccessTools.Method(t, "SetSpecialItemTooltip", new[] { typeof(ClickableTextureComponent) });
            _skillsHideTooltipMethod = AccessTools.Method(t, "HideTooltip");
            _skillsHoverBoxField = AccessTools.Field(t, "hoverBox");
            _skillsWidthModField = AccessTools.Field(t, "widthMod");
            _skillsHeightModField = AccessTools.Field(t, "heightMod");
            _skillsFieldsCached = true;
        }

        /// <summary>
        /// Fix SkillsPage component wiring when the Skills tab is opened.
        /// skillBars (profession stars) have no myID or neighbors — we assign IDs and wire them
        /// into a grid with skillAreas so snap navigation can reach them.
        /// </summary>
        private static void FixSkillsPage(IClickableMenu page)
        {
            try
            {
                CacheSkillsFields(page);

                var skillBars = _skillsSkillBarsField?.GetValue(page) as IList;
                var skillAreas = _skillsSkillAreasField?.GetValue(page) as IList;
                var specialItems = _skillsSpecialItemsField?.GetValue(page) as IList;

                if (skillAreas == null)
                {
                    Monitor?.Log("[SkillsPage] No skillAreas found", LogLevel.Warn);
                    return;
                }

                float heightMod = _skillsHeightModField != null ? (float)_skillsHeightModField.GetValue(page) : 1f;

                // Build a map of which skillBars exist at which grid position.
                // skillBars are ordered: tier1-row0, tier1-row1, ..., tier1-row4, tier2-row0, ...
                // But only earned professions have bars. Identify by Y position matching skillArea Y.
                // From decompiled source: skillBar Y = num9 + j * heightMod * 60
                //   where num9 = yPositionOnScreen + 90 * heightMod - 4
                // skillArea Y = same formula: num9 + k * heightMod * 60
                // Tier 1 bars (i=4) have X = num10 + 140 (approx), Tier 2 bars (i=9) have X = num10 + 344 (approx)

                // Strategy: match skillBars to rows by Y, then determine tier by X order within a row
                var barsByRow = new Dictionary<int, List<ClickableTextureComponent>>(); // row -> bars sorted by X

                if (skillBars != null)
                {
                    foreach (ClickableTextureComponent bar in skillBars)
                    {
                        // Find which row this bar belongs to by matching Y to skillAreas
                        int bestRow = -1;
                        int bestDist = int.MaxValue;
                        for (int r = 0; r < skillAreas.Count; r++)
                        {
                            var area = skillAreas[r] as ClickableComponent;
                            if (area == null) continue;
                            int dist = Math.Abs(bar.bounds.Y - area.bounds.Y);
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestRow = r;
                            }
                        }
                        if (bestRow >= 0 && bestDist < (int)(heightMod * 30)) // within half a row height
                        {
                            if (!barsByRow.ContainsKey(bestRow))
                                barsByRow[bestRow] = new List<ClickableTextureComponent>();
                            barsByRow[bestRow].Add(bar);
                        }
                    }

                    // Sort each row's bars by X to determine tier (leftmost = tier1, rightmost = tier2)
                    foreach (var kv in barsByRow)
                        kv.Value.Sort((a, b2) => a.bounds.X.CompareTo(b2.bounds.X));
                }

                // Assign IDs: tier1 = 100+row, tier2 = 200+row
                foreach (var kv in barsByRow)
                {
                    int row = kv.Key;
                    var bars = kv.Value;
                    for (int tier = 0; tier < bars.Count && tier < 2; tier++)
                    {
                        int baseId = (tier == 0) ? 100 : 200;
                        bars[tier].myID = baseId + row;
                    }
                }

                // Wire skillAreas: fix rightNeighborID to point to first available bar in that row
                for (int k = 0; k < skillAreas.Count; k++)
                {
                    var area = skillAreas[k] as ClickableComponent;
                    if (area == null) continue;

                    if (barsByRow.ContainsKey(k) && barsByRow[k].Count > 0)
                        area.rightNeighborID = barsByRow[k][0].myID; // tier 1 bar
                    else
                        area.rightNeighborID = -1;
                }

                // Wire skillBars neighbors
                foreach (var kv in barsByRow)
                {
                    int row = kv.Key;
                    var bars = kv.Value;

                    for (int tier = 0; tier < bars.Count && tier < 2; tier++)
                    {
                        var bar = bars[tier];

                        // Left neighbor: previous tier in same row, or skillArea
                        if (tier == 0)
                            bar.leftNeighborID = row; // skillArea myID = row index
                        else if (tier == 1 && bars.Count > 1)
                            bar.leftNeighborID = bars[0].myID; // tier 1 in same row

                        // Right neighbor: next tier in same row, or -1
                        if (tier == 0 && bars.Count > 1)
                            bar.rightNeighborID = bars[1].myID;
                        else
                            bar.rightNeighborID = -1;

                        // Up neighbor: same tier in row above, or tab bar
                        int upRow = row - 1;
                        if (upRow >= 0 && barsByRow.ContainsKey(upRow) && barsByRow[upRow].Count > tier)
                            bar.upNeighborID = barsByRow[upRow][tier].myID;
                        else if (upRow >= 0)
                            bar.upNeighborID = upRow; // fall back to skillArea above
                        else
                            bar.upNeighborID = 12341; // tab bar

                        // Down neighbor: same tier in row below, or specialItems, or -1
                        int downRow = row + 1;
                        if (downRow < skillAreas.Count && barsByRow.ContainsKey(downRow) && barsByRow[downRow].Count > tier)
                            bar.downNeighborID = barsByRow[downRow][tier].myID;
                        else if (downRow < skillAreas.Count)
                            bar.downNeighborID = downRow; // fall back to skillArea below
                        else
                            bar.downNeighborID = -1;
                    }
                }

                // Ensure skillBars are in allClickableComponents so moveCursorInDirection can find them
                var allComps = page.allClickableComponents;
                if (allComps != null && skillBars != null)
                {
                    foreach (ClickableTextureComponent bar in skillBars)
                    {
                        if (bar.myID >= 100 && !allComps.Contains(bar))
                            allComps.Add(bar);
                    }
                }

                bool verbose = ModEntry.Config.VerboseLogging;
                if (verbose)
                {
                    Monitor?.Log($"[SkillsPage] Fixed wiring: {skillBars?.Count ?? 0} skillBars, {skillAreas.Count} skillAreas, {specialItems?.Count ?? 0} specialItems", LogLevel.Info);
                    foreach (var kv in barsByRow)
                    {
                        foreach (var bar in kv.Value)
                            Monitor?.Log($"[SkillsPage]   Bar row={kv.Key} ID={bar.myID} bounds=({bar.bounds.X},{bar.bounds.Y},{bar.bounds.Width},{bar.bounds.Height}) L={bar.leftNeighborID} R={bar.rightNeighborID} U={bar.upNeighborID} D={bar.downNeighborID}", LogLevel.Info);
                    }
                    for (int k = 0; k < skillAreas.Count; k++)
                    {
                        var area = skillAreas[k] as ClickableComponent;
                        if (area != null)
                            Monitor?.Log($"[SkillsPage]   Area row={k} ID={area.myID} R={area.rightNeighborID} U={area.upNeighborID} D={area.downNeighborID}", LogLevel.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[SkillsPage] Error in FixSkillsPage: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Prefix on SkillsPage.receiveGamePadButton — replaces vanilla's broken left/right-only
        /// handler with proper grid navigation using snap nav (moveCursorInDirection).
        /// Shows tooltip for the newly snapped component after each navigation.
        /// </summary>
        private static bool SkillsReceiveGamePadButton_Prefix(SkillsPage __instance, Buttons b)
        {
            if (!ModEntry.Config.EnableGameMenuNavigation)
                return true;

            try
            {
                int direction = -1;
                switch (b)
                {
                    case Buttons.DPadUp:
                    case Buttons.LeftThumbstickUp:
                        direction = 0; // up
                        break;
                    case Buttons.DPadRight:
                    case Buttons.LeftThumbstickRight:
                        direction = 1; // right
                        break;
                    case Buttons.DPadDown:
                    case Buttons.LeftThumbstickDown:
                        direction = 2; // down
                        break;
                    case Buttons.DPadLeft:
                    case Buttons.LeftThumbstickLeft:
                        direction = 3; // left
                        break;
                }

                if (direction >= 0)
                {
                    // Use the game's own snap navigation with our fixed wiring
                    __instance.moveCursorInDirection(direction);
                    ShowSkillsTooltipForSnapped(__instance);
                    return false; // skip vanilla
                }

                // Let other buttons (A, B, etc.) pass through to vanilla
                // B is handled by GameMenu.receiveGamePadButton before it reaches here
                return true;
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[SkillsPage] Error in receiveGamePadButton prefix: {ex.Message}", LogLevel.Error);
                return true;
            }
        }

        /// <summary>Show the appropriate tooltip for the currently snapped component on SkillsPage.</summary>
        private static void ShowSkillsTooltipForSnapped(SkillsPage page)
        {
            try
            {
                CacheSkillsFields(page);
                var snapped = page.currentlySnappedComponent;
                if (snapped == null)
                {
                    _skillsHideTooltipMethod?.Invoke(page, null);
                    return;
                }

                _skillsHideTooltipMethod?.Invoke(page, null);

                int id = snapped.myID;

                // skillBars: IDs 100-104 (tier 1) or 200-204 (tier 2)
                if (id >= 100 && id < 300 && snapped is ClickableTextureComponent barComp)
                {
                    _skillsSetSkillBarTooltipMethod?.Invoke(page, new object[] { barComp });
                }
                // skillAreas: IDs 0-4
                else if (id >= 0 && id <= 4 && snapped is ClickableTextureComponent areaComp)
                {
                    _skillsSetSkillAreaTooltipMethod?.Invoke(page, new object[] { areaComp });
                }
                // specialItems would be 10201+ (from downNeighborID on last skillArea)
                else if (snapped is ClickableTextureComponent specialComp)
                {
                    var specialItems = _skillsSpecialItemsField?.GetValue(page) as IList;
                    if (specialItems != null && specialItems.Contains(specialComp))
                    {
                        _skillsSetSpecialItemTooltipMethod?.Invoke(page, new object[] { specialComp });
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[SkillsPage] Error showing tooltip: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Prefix on SkillsPage.draw — reposition tooltip hoverBox to be near the selected component
        /// instead of hardcoded portrait area. Top-left of tooltip at bottom-right of component.
        /// </summary>
        private static void SkillsDraw_Prefix(SkillsPage __instance)
        {
            if (!ModEntry.Config.EnableGameMenuNavigation || !Game1.options.gamepadControls)
                return;

            try
            {
                CacheSkillsFields(__instance);
                var snapped = __instance.currentlySnappedComponent;
                if (snapped == null || _skillsHoverBoxField == null)
                    return;

                var hoverBox = (Rectangle)_skillsHoverBoxField.GetValue(__instance);

                // Only reposition if a tooltip is actually showing (hoverBox has size)
                if (hoverBox.Width <= 0 || hoverBox.Height <= 0)
                    return;

                // Position: top-left of tooltip at bottom-right of cursor/component
                int newX = snapped.bounds.Right + 16;
                int newY = snapped.bounds.Bottom;

                // Keep tooltip on screen
                if (newX + hoverBox.Width > __instance.xPositionOnScreen + __instance.width)
                    newX = snapped.bounds.Left - hoverBox.Width - 16;
                if (newY + hoverBox.Height > __instance.yPositionOnScreen + __instance.height)
                    newY = snapped.bounds.Top - hoverBox.Height;

                hoverBox.X = newX;
                hoverBox.Y = newY;
                _skillsHoverBoxField.SetValue(__instance, hoverBox);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[SkillsPage] Error in draw prefix: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Postfix on SkillsPage.draw — draws finger cursor at the currently snapped component.
        /// </summary>
        private static void SkillsDraw_Postfix(SkillsPage __instance, SpriteBatch b)
        {
            if (!ModEntry.Config.EnableGameMenuNavigation || !Game1.options.gamepadControls)
                return;

            try
            {
                var snapped = __instance.currentlySnappedComponent;
                if (snapped == null)
                    return;

                int cursorX = snapped.bounds.Center.X;
                int cursorY = snapped.bounds.Center.Y;

                b.Draw(Game1.mouseCursors,
                    new Vector2(cursorX, cursorY),
                    Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 44, 16, 16),
                    Color.White, 0f, Vector2.Zero,
                    4f + Game1.dialogueButtonScale / 150f,
                    SpriteEffects.None, 1f);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[SkillsPage] Error in draw postfix: {ex.Message}", LogLevel.Error);
            }
        }

        // ===== PowersTab draw patches =====

        /// <summary>
        /// Prefix on PowersTab.draw — hides the glow highlight texture and syncs selectedIndex
        /// with currentlySnappedComponent. The game's snap navigation moves currentlySnappedComponent
        /// via D-pad, but selectedIndex (which controls the info panel text) is only updated by
        /// doSelect() which is touch-only. We call doSelect() here to keep them in sync.
        /// </summary>
        private static void PowersDraw_Prefix(IClickableMenu __instance)
        {
            if (!ModEntry.Config.EnableGameMenuNavigation)
                return;

            var highlight = _powersHighlightField?.GetValue(__instance) as ClickableTextureComponent;
            if (highlight != null)
                highlight.visible = false;

            // Sync selectedIndex with currentlySnappedComponent so the info panel updates
            if (Game1.options.gamepadControls && __instance.currentlySnappedComponent != null && _powersDoSelectMethod != null)
            {
                int snappedId = __instance.currentlySnappedComponent.myID;
                int selectedIndex = _powersSelectedIndexField != null ? (int)_powersSelectedIndexField.GetValue(__instance) : -1;
                if (snappedId != selectedIndex && snappedId >= 0)
                {
                    try { _powersDoSelectMethod.Invoke(__instance, new object[] { snappedId }); }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Postfix on PowersTab.draw — restores highlightTexture visibility so it works
        /// if the feature is toggled off mid-session. No cursor drawn — the game's own cursor works.
        /// </summary>
        private static void PowersDraw_Postfix(IClickableMenu __instance, SpriteBatch b)
        {
            var highlight = _powersHighlightField?.GetValue(__instance) as ClickableTextureComponent;
            if (highlight != null)
                highlight.visible = true;
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
                                    _lastProfileMenuSnappedId = -999;
                                    DumpChildMenu(childMenu);
                                }

                                // Track snap component changes in ProfileMenu (log on change only)
                                var snapped = childMenu.currentlySnappedComponent;
                                int snappedId = snapped?.myID ?? -1;
                                if (snappedId != _lastProfileMenuSnappedId)
                                {
                                    _lastProfileMenuSnappedId = snappedId;
                                    if (snapped != null)
                                        Monitor?.Log($"[ProfileDiag] Snap changed: ID={snapped.myID} name='{snapped.name}' bounds=({snapped.bounds.X},{snapped.bounds.Y},{snapped.bounds.Width},{snapped.bounds.Height}) mouse=({Game1.getMouseX()},{Game1.getMouseY()}) drawMouse={Game1.options.hardwareCursor}", LogLevel.Info);
                                    else
                                        Monitor?.Log($"[ProfileDiag] Snap changed: null, mouse=({Game1.getMouseX()},{Game1.getMouseY()})", LogLevel.Info);
                                }
                            }
                            else
                            {
                                _dumpedChildMenu = false;
                                _lastProfileMenuSnappedId = -999;
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

            // ProfileMenu-specific diagnostics
            if (childMenu.GetType().Name == "ProfileMenu")
            {
                Monitor?.Log($"[ProfileDiag] === ProfileMenu cursor diagnostic ===", LogLevel.Info);
                Monitor?.Log($"[ProfileDiag] snappyMenus={Game1.options.snappyMenus} gamepadControls={Game1.options.gamepadControls} lastCursorMotionWasMouse={Game1.lastCursorMotionWasMouse}", LogLevel.Info);
                Monitor?.Log($"[ProfileDiag] mouse=({Game1.getMouseX()},{Game1.getMouseY()}) hardwareCursor={Game1.options.hardwareCursor}", LogLevel.Info);

                // Log clickableProfileItems
                var profileItemsField = AccessTools.Field(childMenu.GetType(), "clickableProfileItems");
                if (profileItemsField != null)
                {
                    var items = profileItemsField.GetValue(childMenu) as IList;
                    if (items != null)
                    {
                        Monitor?.Log($"[ProfileDiag] clickableProfileItems: count={items.Count}", LogLevel.Info);
                        for (int i = 0; i < Math.Min(items.Count, 20); i++)
                        {
                            var item = items[i] as ClickableComponent;
                            if (item != null)
                                Monitor?.Log($"[ProfileDiag]   item[{i}] ID={item.myID} name='{item.name}' bounds=({item.bounds.X},{item.bounds.Y},{item.bounds.Width},{item.bounds.Height}) neighbors L={item.leftNeighborID} R={item.rightNeighborID} U={item.upNeighborID} D={item.downNeighborID}", LogLevel.Info);
                        }
                        if (items.Count > 20)
                            Monitor?.Log($"[ProfileDiag]   ... and {items.Count - 20} more items", LogLevel.Info);
                    }
                }
                else
                    Monitor?.Log($"[ProfileDiag] clickableProfileItems field not found", LogLevel.Warn);

                // Log _currentCategory
                var categoryField = AccessTools.Field(childMenu.GetType(), "_currentCategory");
                if (categoryField != null)
                    Monitor?.Log($"[ProfileDiag] _currentCategory={categoryField.GetValue(childMenu)}", LogLevel.Info);
            }

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
