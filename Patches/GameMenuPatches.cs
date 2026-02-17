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

        // Track slotPosition to detect right-stick scroll changes
        private static int _lastSocialSlotPosition = -1;

        // Scrollbox tap simulation: two-frame tap gesture
        // Phase 0 = idle, 1 = receiveLeftClick pending, 2 = releaseLeftClick pending
        private static int _scrollboxTapPhase = 0;
        private static int _scrollboxTapX, _scrollboxTapY;

        // Diagnostic: track child menu on SocialPage (gift log)
        private static bool _dumpedChildMenu = false;

        // Diagnostic: one-time dump of SocialPage fields to find scrollbox
        private static bool _dumpedSocialFields = false;

        // Diagnostic: cached scrollbox yOffset field for logging
        private static FieldInfo _scrollboxYOffsetField;

        // Diagnostic: track last A-press tick for updateSlots correlation
        private static int _lastSocialAPressTickDiag = -999;

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

                // Diagnostic: Patch SocialPage.receiveLeftClick to log ALL calls
                var socialClickMethod = AccessTools.Method(typeof(SocialPage), "receiveLeftClick", new[] { typeof(int), typeof(int), typeof(bool) });
                if (socialClickMethod != null)
                {
                    harmony.Patch(
                        original: socialClickMethod,
                        prefix: new HarmonyMethod(typeof(GameMenuPatches), nameof(SocialReceiveLeftClick_Prefix)),
                        postfix: new HarmonyMethod(typeof(GameMenuPatches), nameof(SocialReceiveLeftClick_Postfix))
                    );
                    Monitor.Log("SocialPage.receiveLeftClick diagnostic patch applied.", LogLevel.Trace);
                }
                else
                {
                    Monitor.Log("SocialPage.receiveLeftClick method not found!", LogLevel.Warn);
                }

                // Diagnostic: Patch SocialPage.releaseLeftClick to track release events
                var socialReleaseMethod = AccessTools.Method(typeof(SocialPage), "releaseLeftClick", new[] { typeof(int), typeof(int) });
                if (socialReleaseMethod != null)
                {
                    harmony.Patch(
                        original: socialReleaseMethod,
                        prefix: new HarmonyMethod(typeof(GameMenuPatches), nameof(SocialReleaseLeftClick_Prefix))
                    );
                    Monitor.Log("SocialPage.releaseLeftClick diagnostic patch applied.", LogLevel.Trace);
                }

                // Diagnostic: Patch SocialPage.updateSlots to track when it fires relative to clicks
                var updateSlotsMethod = AccessTools.Method(typeof(SocialPage), "updateSlots");
                if (updateSlotsMethod != null)
                {
                    harmony.Patch(
                        original: updateSlotsMethod,
                        prefix: new HarmonyMethod(typeof(GameMenuPatches), nameof(SocialUpdateSlots_Prefix))
                    );
                    Monitor.Log("SocialPage.updateSlots diagnostic patch applied.", LogLevel.Trace);
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
                    _socialSelectSlotMethod = pageType.GetMethod("_SelectSlot", BindingFlags.Instance | BindingFlags.NonPublic);

                    // Cache yOffset field from MobileScrollbox (for coordinate diagnostics)
                    if (_socialScrollAreaField != null)
                    {
                        var scrollArea = _socialScrollAreaField.GetValue(page);
                        if (scrollArea != null)
                        {
                            _scrollboxYOffsetField = AccessTools.Field(scrollArea.GetType(), "yOffset")
                                                  ?? AccessTools.Field(scrollArea.GetType(), "_yOffset")
                                                  ?? AccessTools.Field(scrollArea.GetType(), "scrollY");
                            if (_scrollboxYOffsetField != null)
                                Monitor?.Log($"[SocialDiag] Found scrollbox yOffset field: {_scrollboxYOffsetField.Name} (type={_scrollboxYOffsetField.FieldType.Name})", LogLevel.Trace);
                            else
                            {
                                // Try all fields to find something that looks like a Y offset
                                foreach (var f in scrollArea.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                                {
                                    if (f.Name.ToLower().Contains("offset") || f.Name.ToLower().Contains("scroll"))
                                        Monitor?.Log($"[SocialDiag] Candidate scrollbox field: {f.Name} (type={f.FieldType.Name})", LogLevel.Trace);
                                }
                            }
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

                bool verbose = ModEntry.Config.VerboseLogging;
                if (verbose)
                    Monitor?.Log($"[GameMenu] SocialPage: {charSlots.Count} slots, slotHeight={slotHeight}, mainBox=({mainBox.X},{mainBox.Y},{mainBox.Width},{mainBox.Height}), slotsYStart={slotsYStart} (offset={slotsYOffset}), slotPosition={slotPosition}", LogLevel.Info);

                ClickableComponent firstVisibleSlot = null;

                for (int i = 0; i < charSlots.Count; i++)
                {
                    var slot = charSlots[i] as ClickableComponent;
                    if (slot == null) continue;

                    int visualIndex = i - slotPosition;
                    int newY = slotsYStart + (visualIndex * slotHeight);

                    var b = slot.bounds;
                    slot.bounds = new Rectangle(b.X, newY, b.Width, slotHeight);

                    if (i == slotPosition)
                        firstVisibleSlot = slot;

                    if (verbose)
                        Monitor?.Log($"[GameMenu]   slot[{i}] ID={slot.myID} bounds=({slot.bounds.X},{slot.bounds.Y},{slot.bounds.Width},{slot.bounds.Height}) U={slot.upNeighborID} D={slot.downNeighborID}", LogLevel.Info);
                }

                // Snap to first visible slot (on tab entry)
                if (firstVisibleSlot != null)
                {
                    page.currentlySnappedComponent = firstVisibleSlot;
                    page.snapCursorToCurrentSnappedComponent();

                    if (verbose)
                        Monitor?.Log($"[GameMenu] SocialPage: snapped to slot[{slotPosition}] ID={firstVisibleSlot.myID} at ({firstVisibleSlot.bounds.X},{firstVisibleSlot.bounds.Y})", LogLevel.Info);
                }

                _lastSocialSlotPosition = slotPosition;
                Monitor?.Log($"[GameMenu] SocialPage: fixed {charSlots.Count} slot bounds", LogLevel.Trace);

                // One-time: check if sprites and characterSlots share object references
                if (!_dumpedSocialFields)
                {
                    var spritesField = AccessTools.Field(page.GetType(), "sprites");
                    if (spritesField != null)
                    {
                        var spritesList = spritesField.GetValue(page) as IList;
                        if (spritesList != null && charSlots != null)
                        {
                            bool allSame = true;
                            int checkCount = Math.Min(spritesList.Count, charSlots.Count);
                            for (int s = 0; s < checkCount; s++)
                            {
                                if (!ReferenceEquals(spritesList[s], charSlots[s]))
                                {
                                    allSame = false;
                                    var sprite = spritesList[s] as ClickableComponent;
                                    var slot = charSlots[s] as ClickableComponent;
                                    Monitor?.Log($"[SpriteDiag] MISMATCH at [{s}]: sprites bounds=({sprite?.bounds.X},{sprite?.bounds.Y},{sprite?.bounds.Width},{sprite?.bounds.Height}) charSlots bounds=({slot?.bounds.X},{slot?.bounds.Y},{slot?.bounds.Width},{slot?.bounds.Height})", LogLevel.Info);
                                    if (s >= 5) { Monitor?.Log("[SpriteDiag] (truncated, showing first 6 mismatches)", LogLevel.Info); break; }
                                }
                            }
                            if (allSame)
                                Monitor?.Log($"[SpriteDiag] ALL {checkCount} sprites and characterSlots are the SAME object references — fixing one fixes both", LogLevel.Info);
                            else
                                Monitor?.Log($"[SpriteDiag] sprites and characterSlots are DIFFERENT objects — sprites bounds are NOT being fixed!", LogLevel.Info);
                        }
                    }

                    _dumpedSocialFields = true;
                    Monitor?.Log("[SocialDiag] === SocialPage field dump ===", LogLevel.Trace);
                    var pageType = page.GetType();
                    foreach (var field in pageType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        try
                        {
                            var val = field.GetValue(page);
                            string valStr = val?.GetType().Name ?? "null";
                            if (val is int i) valStr = i.ToString();
                            else if (val is bool b) valStr = b.ToString();
                            else if (val is float f) valStr = f.ToString();
                            else if (val is Rectangle r) valStr = $"{{X:{r.X} Y:{r.Y} W:{r.Width} H:{r.Height}}}";
                            else if (val is IList list) valStr = $"{val.GetType().Name}[{list.Count}]";
                            Monitor?.Log($"[SocialDiag]   {field.Name}: {field.FieldType.Name} = {valStr}", LogLevel.Trace);
                        }
                        catch { }
                    }
                    // Also check base class (IClickableMenu) fields
                    foreach (var field in typeof(IClickableMenu).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        try
                        {
                            var val = field.GetValue(page);
                            string valStr = val?.GetType().Name ?? "null";
                            if (val is IList list) valStr = $"{val.GetType().Name}[{list.Count}]";
                            Monitor?.Log($"[SocialDiag]   (base) {field.Name}: {field.FieldType.Name} = {valStr}", LogLevel.Trace);
                        }
                        catch { }
                    }
                    // Dump MobileScrollbox fields to understand scroll API
                    if (_socialScrollAreaField != null)
                    {
                        var scrollArea = _socialScrollAreaField.GetValue(page);
                        if (scrollArea != null)
                        {
                            Monitor?.Log("[SocialDiag] === MobileScrollbox field dump ===", LogLevel.Trace);
                            var scrollType = scrollArea.GetType();
                            foreach (var field in scrollType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                            {
                                try
                                {
                                    var val = field.GetValue(scrollArea);
                                    string valStr = val?.GetType().Name ?? "null";
                                    if (val is int iv) valStr = iv.ToString();
                                    else if (val is bool bv) valStr = bv.ToString();
                                    else if (val is float fv) valStr = fv.ToString();
                                    else if (val is double dv) valStr = dv.ToString();
                                    else if (val is Rectangle rv) valStr = $"{{X:{rv.X} Y:{rv.Y} W:{rv.Width} H:{rv.Height}}}";
                                    else if (val is Vector2 v2) valStr = $"({v2.X},{v2.Y})";
                                    else if (val is IList lv) valStr = $"{val.GetType().Name}[{lv.Count}]";
                                    Monitor?.Log($"[SocialDiag]   scroll.{field.Name}: {field.FieldType.Name} = {valStr}", LogLevel.Trace);
                                }
                                catch { }
                            }
                            // Also check base class fields
                            var baseType = scrollType.BaseType;
                            if (baseType != null && baseType != typeof(object))
                            {
                                Monitor?.Log($"[SocialDiag]   --- base: {baseType.Name} ---", LogLevel.Trace);
                                foreach (var field in baseType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                                {
                                    try
                                    {
                                        var val = field.GetValue(scrollArea);
                                        string valStr = val?.GetType().Name ?? "null";
                                        if (val is int iv) valStr = iv.ToString();
                                        else if (val is bool bv) valStr = bv.ToString();
                                        else if (val is float fv) valStr = fv.ToString();
                                        else if (val is Rectangle rv) valStr = $"{{X:{rv.X} Y:{rv.Y} W:{rv.Width} H:{rv.Height}}}";
                                        Monitor?.Log($"[SocialDiag]   scroll(base).{field.Name}: {field.FieldType.Name} = {valStr}", LogLevel.Trace);
                                    }
                                    catch { }
                                }
                            }
                            // List methods too (for scroll API discovery)
                            Monitor?.Log("[SocialDiag]   --- MobileScrollbox methods ---", LogLevel.Trace);
                            foreach (var method in scrollType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                            {
                                var parms = method.GetParameters();
                                string parmStr = string.Join(", ", Array.ConvertAll(parms, p => $"{p.ParameterType.Name} {p.Name}"));
                                Monitor?.Log($"[SocialDiag]   scroll.{method.Name}({parmStr}) → {method.ReturnType.Name}", LogLevel.Trace);
                            }
                            Monitor?.Log("[SocialDiag] === end MobileScrollbox dump ===", LogLevel.Trace);
                        }
                    }
                    Monitor?.Log("[SocialDiag] === end field dump ===", LogLevel.Trace);
                }
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

        // ===== SocialPage.update() prefix — drive scrollbox tap gesture =====

        private static void SocialUpdate_Prefix(SocialPage __instance)
        {
            if (_scrollboxTapPhase == 0) return;

            var scrollArea = _socialScrollAreaField?.GetValue(__instance);
            if (scrollArea == null) { _scrollboxTapPhase = 0; return; }

            var scrollType = scrollArea.GetType();

            if (_scrollboxTapPhase == 1)
            {
                // Frame 1: touch down
                var clickMethod = scrollType.GetMethod("receiveLeftClick", new[] { typeof(int), typeof(int) });
                clickMethod?.Invoke(scrollArea, new object[] { _scrollboxTapX, _scrollboxTapY });
                _scrollboxTapPhase = 2;
                Monitor?.Log($"[SocialDiag] scrollbox tap phase 1: receiveLeftClick({_scrollboxTapX},{_scrollboxTapY})", LogLevel.Trace);
            }
            else if (_scrollboxTapPhase == 2)
            {
                // Frame 2: touch up at same position (= tap)
                var releaseMethod = scrollType.GetMethod("releaseLeftClick", new[] { typeof(int), typeof(int) });
                releaseMethod?.Invoke(scrollArea, new object[] { _scrollboxTapX, _scrollboxTapY });
                _scrollboxTapPhase = 0;
                Monitor?.Log($"[SocialDiag] scrollbox tap phase 2: releaseLeftClick({_scrollboxTapX},{_scrollboxTapY})", LogLevel.Trace);
            }
        }

        /// <summary>
        /// Diagnostic: log every call to SocialPage.receiveLeftClick with coordinates.
        /// This tells us exactly what the touch simulation sends and whether it fires at all.
        /// </summary>
        private static void SocialReceiveLeftClick_Prefix(SocialPage __instance, int x, int y, bool playSound)
        {
            try
            {
                // Get yOffset from scrollbox
                float yOffset = 0f;
                if (_socialScrollAreaField != null && _scrollboxYOffsetField != null)
                {
                    var scrollArea = _socialScrollAreaField.GetValue(__instance);
                    if (scrollArea != null)
                    {
                        var yOffsetVal = _scrollboxYOffsetField.GetValue(scrollArea);
                        if (yOffsetVal is float f) yOffset = f;
                        else if (yOffsetVal is int iv) yOffset = iv;
                    }
                }

                // Get current snapped component
                var snapped = __instance.currentlySnappedComponent;
                string snappedInfo = snapped != null ? $"ID={snapped.myID} center=({snapped.bounds.Center.X},{snapped.bounds.Center.Y})" : "null";

                // Compute what slot index the click Y maps to
                int slotHeight = _socialSlotHeightField != null ? (int)_socialSlotHeightField.GetValue(__instance) : 0;
                var mainBox = _socialMainBoxField != null ? (Rectangle)_socialMainBoxField.GetValue(__instance) : Rectangle.Empty;
                int slotsYOffset = _socialSlotsYStartField != null ? (int)_socialSlotsYStartField.GetValue(__instance) : 0;
                int baseY = mainBox.Y + slotsYOffset;
                int computedSlot = slotHeight > 0 ? (int)((y - baseY + Math.Abs(yOffset)) / slotHeight) : -1;

                // Get clickedEntry before this call
                int clickedEntryBefore = _socialClickedEntryField != null ? (int)_socialClickedEntryField.GetValue(__instance) : -999;

                Monitor?.Log($"[SocialClickDiag] receiveLeftClick({x},{y},playSound={playSound}) yOffset={yOffset} snapped={snappedInfo} baseY={baseY} computedSlot={computedSlot} clickedEntryBefore={clickedEntryBefore} tick={Game1.ticks}", LogLevel.Info);

                // DIAGNOSTIC: Dump sprites bounds and check if same objects as characterSlots
                var spritesField = AccessTools.Field(__instance.GetType(), "sprites");
                if (spritesField != null)
                {
                    var spritesList = spritesField.GetValue(__instance) as IList;
                    var charSlots = _socialCharacterSlotsField?.GetValue(__instance) as IList;
                    if (spritesList != null)
                    {
                        int dumpCount = Math.Min(6, spritesList.Count);
                        for (int si = 0; si < dumpCount; si++)
                        {
                            var sprite = spritesList[si] as ClickableComponent;
                            var charSlot = (charSlots != null && si < charSlots.Count) ? charSlots[si] as ClickableComponent : null;
                            bool sameRef = (sprite != null && charSlot != null && ReferenceEquals(sprite, charSlot));
                            string spriteBounds = sprite != null ? $"({sprite.bounds.X},{sprite.bounds.Y},{sprite.bounds.Width},{sprite.bounds.Height})" : "null";
                            string charBounds = charSlot != null ? $"({charSlot.bounds.X},{charSlot.bounds.Y},{charSlot.bounds.Width},{charSlot.bounds.Height})" : "null";
                            bool hitTest = sprite != null && new Rectangle(sprite.bounds.X, sprite.bounds.Y, sprite.bounds.Width, sprite.bounds.Height + 4).Contains(x, y);
                            Monitor?.Log($"[SpriteDiag] sprites[{si}] bounds={spriteBounds} charSlots[{si}] bounds={charBounds} sameRef={sameRef} wouldHit({x},{y})={hitTest}", LogLevel.Info);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[SocialClickDiag] ERROR: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Diagnostic postfix: log clickedEntry AFTER receiveLeftClick completes.
        /// </summary>
        private static void SocialReceiveLeftClick_Postfix(SocialPage __instance, int x, int y)
        {
            try
            {
                int clickedEntryAfter = _socialClickedEntryField != null ? (int)_socialClickedEntryField.GetValue(__instance) : -999;
                var childMenuField = AccessTools.Field(typeof(IClickableMenu), "_childMenu")
                                  ?? AccessTools.Field(__instance.GetType(), "_childMenu");
                bool hasChildMenu = false;
                if (childMenuField != null)
                {
                    var child = childMenuField.GetValue(__instance);
                    hasChildMenu = child != null;
                }
                Monitor?.Log($"[SocialClickDiag] AFTER receiveLeftClick({x},{y}): clickedEntry={clickedEntryAfter} hasChildMenu={hasChildMenu} tick={Game1.ticks}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[SocialClickDiag] POSTFIX ERROR: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Diagnostic: log every call to SocialPage.releaseLeftClick.
        /// Confirms whether touch simulation generates release events.
        /// </summary>
        private static void SocialReleaseLeftClick_Prefix(SocialPage __instance, int x, int y)
        {
            try
            {
                int clickedEntry = _socialClickedEntryField != null ? (int)_socialClickedEntryField.GetValue(__instance) : -999;
                var scrollingField = AccessTools.Field(__instance.GetType(), "scrolling");
                bool scrolling = scrollingField != null && (bool)scrollingField.GetValue(__instance);
                Monitor?.Log($"[SocialReleaseDiag] releaseLeftClick({x},{y}) clickedEntry={clickedEntry} scrolling={scrolling} tick={Game1.ticks}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[SocialReleaseDiag] ERROR: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Diagnostic: track when updateSlots() fires relative to click events.
        /// Tests hypothesis that scrollArea.receiveLeftClick triggers updateSlots as side effect.
        /// </summary>
        private static void SocialUpdateSlots_Prefix(SocialPage __instance)
        {
            try
            {
                // Only log when near a click event (within 5 ticks of last A-press)
                if (Math.Abs(Game1.ticks - _lastSocialAPressTickDiag) <= 5)
                {
                    var spritesField = AccessTools.Field(__instance.GetType(), "sprites");
                    var spritesList = spritesField?.GetValue(__instance) as IList;
                    string firstSpriteBounds = "?";
                    if (spritesList != null && spritesList.Count > 0)
                    {
                        var s = spritesList[0] as ClickableComponent;
                        if (s != null) firstSpriteBounds = $"({s.bounds.X},{s.bounds.Y},{s.bounds.Width},{s.bounds.Height})";
                    }
                    Monitor?.Log($"[UpdateSlotsDiag] updateSlots() called! tick={Game1.ticks} sprites[0].bounds={firstSpriteBounds}", LogLevel.Info);
                }
            }
            catch { }
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

            switch (b)
            {
                case Buttons.A:
                {
                    _lastSocialAPressTickDiag = Game1.ticks;

                    // DIAGNOSTIC v3.3.35: Block A completely. Touch sim still fires.
                    // Log details so we can compare working vs non-working slots.
                    var snapped = page.currentlySnappedComponent;

                    // Get yOffset from scrollbox
                    float yOffset = 0f;
                    if (_socialScrollAreaField != null && _scrollboxYOffsetField != null)
                    {
                        var scrollArea = _socialScrollAreaField.GetValue(page);
                        if (scrollArea != null)
                        {
                            var yOffsetVal = _scrollboxYOffsetField.GetValue(scrollArea);
                            if (yOffsetVal is float f) yOffset = f;
                            else if (yOffsetVal is int iv) yOffset = iv;
                        }
                    }

                    // Find slot index in characterSlots
                    int slotIdx = -1;
                    if (snapped != null && _socialCharacterSlotsField != null)
                    {
                        var charSlots = _socialCharacterSlotsField.GetValue(page) as IList;
                        if (charSlots != null)
                        {
                            for (int i = 0; i < charSlots.Count; i++)
                            {
                                if (charSlots[i] == snapped) { slotIdx = i; break; }
                            }
                        }
                    }

                    // Log mouse position (what touch sim will use)
                    var mouseState = Microsoft.Xna.Framework.Input.Mouse.GetState();

                    Monitor?.Log($"[SocialADiag] A-PRESS: slotIdx={slotIdx} snappedID={snapped?.myID} bounds=({snapped?.bounds.X},{snapped?.bounds.Y},{snapped?.bounds.Width},{snapped?.bounds.Height}) center=({snapped?.bounds.Center.X},{snapped?.bounds.Center.Y}) yOffset={yOffset} mouseXY=({mouseState.X},{mouseState.Y}) tick={Game1.ticks}", LogLevel.Info);

                    return false; // block A, let touch sim fire naturally
                }

                case Buttons.DPadDown:
                case Buttons.LeftThumbstickDown:
                    NavigateSocialSlot(page, 1);
                    return false;

                case Buttons.DPadUp:
                case Buttons.LeftThumbstickUp:
                    NavigateSocialSlot(page, -1);
                    return false;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Navigate to the next (+1) or previous (-1) social slot.
        /// Uses the game's own receiveScrollWheelAction to scroll when navigating
        /// past the visible area, keeping the MobileScrollbox in sync.
        /// Also re-fixes bounds when slotPosition changes from right-stick scroll.
        /// </summary>
        private static void NavigateSocialSlot(IClickableMenu page, int direction)
        {
            try
            {
                if (_socialCharacterSlotsField == null || _socialSlotPositionField == null ||
                    _socialSlotHeightField == null || _socialMainBoxField == null)
                {
                    Monitor?.Log($"[SocialDiag] NavigateSocialSlot: reflection fields null, aborting", LogLevel.Trace);
                    return;
                }

                var charSlots = _socialCharacterSlotsField.GetValue(page) as IList;
                if (charSlots == null || charSlots.Count == 0)
                {
                    Monitor?.Log($"[SocialDiag] NavigateSocialSlot: charSlots null/empty", LogLevel.Trace);
                    return;
                }

                int slotPosition = (int)_socialSlotPositionField.GetValue(page);
                int slotHeight = (int)_socialSlotHeightField.GetValue(page);
                var mainBox = (Rectangle)_socialMainBoxField.GetValue(page);
                int visibleSlots = mainBox.Height / slotHeight;

                Monitor?.Log($"[SocialDiag] Nav dir={direction}, slotPos={slotPosition}, lastSlotPos={_lastSocialSlotPosition}, visibleSlots={visibleSlots}, totalSlots={charSlots.Count}", LogLevel.Trace);

                // If slotPosition changed since last fix (e.g., right stick scroll), re-fix bounds
                if (slotPosition != _lastSocialSlotPosition)
                {
                    Monitor?.Log($"[SocialDiag] slotPosition changed ({_lastSocialSlotPosition}→{slotPosition}), refixing bounds", LogLevel.Trace);
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

                Monitor?.Log($"[SocialDiag] currentSnapped ID={current?.myID.ToString() ?? "null"}, matchedIndex={currentIndex}", LogLevel.Trace);
                if (currentIndex < 0) currentIndex = slotPosition;

                int newIndex = currentIndex + direction;

                // Clamp to total range
                if (newIndex < 0 || newIndex >= charSlots.Count)
                {
                    Monitor?.Log($"[SocialDiag] newIndex={newIndex} out of range [0,{charSlots.Count}), clamped", LogLevel.Trace);
                    return;
                }

                // Check if we need to scroll
                if (newIndex < slotPosition || newIndex >= slotPosition + visibleSlots)
                {
                    Monitor?.Log($"[SocialDiag] newIndex={newIndex} outside visible [{slotPosition},{slotPosition + visibleSlots}), scrolling...", LogLevel.Trace);

                    // Use the game's own scroll mechanism to keep MobileScrollbox in sync
                    int scrollDir = direction > 0 ? -1 : 1;
                    Monitor?.Log($"[SocialDiag] calling receiveScrollWheelAction({scrollDir})", LogLevel.Trace);
                    page.receiveScrollWheelAction(scrollDir);

                    // Re-read slotPosition after game scrolled
                    int newSlotPos = (int)_socialSlotPositionField.GetValue(page);
                    Monitor?.Log($"[SocialDiag] after scroll: slotPosition {slotPosition}→{newSlotPos}", LogLevel.Trace);

                    if (newSlotPos != slotPosition)
                    {
                        slotPosition = newSlotPos;
                        RefixSocialBounds(page, charSlots, slotPosition, slotHeight, mainBox);
                        _lastSocialSlotPosition = slotPosition;
                    }
                    else
                    {
                        Monitor?.Log($"[SocialDiag] scroll had no effect (boundary), aborting", LogLevel.Trace);
                        return;
                    }

                    // Verify target is now visible after scroll
                    if (newIndex < slotPosition || newIndex >= slotPosition + visibleSlots)
                    {
                        Monitor?.Log($"[SocialDiag] newIndex={newIndex} still outside visible [{slotPosition},{slotPosition + visibleSlots}) after scroll, aborting", LogLevel.Trace);
                        return;
                    }
                }

                var newSlot = charSlots[newIndex] as ClickableComponent;
                if (newSlot == null)
                {
                    Monitor?.Log($"[SocialDiag] slot[{newIndex}] is null", LogLevel.Trace);
                    return;
                }

                page.currentlySnappedComponent = newSlot;
                page.snapCursorToCurrentSnappedComponent();
                Game1.playSound("shiny4");

                Monitor?.Log($"[SocialDiag] snapped to slot[{newIndex}] ID={newSlot.myID} bounds=({newSlot.bounds.X},{newSlot.bounds.Y},{newSlot.bounds.Width},{newSlot.bounds.Height})", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GameMenu] Error in NavigateSocialSlot: {ex.Message}", LogLevel.Error);
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
