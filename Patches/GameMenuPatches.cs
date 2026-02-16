using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

        // Cached reflection fields for AnimalPage (and SocialPage — same structure)
        private static FieldInfo _characterSlotsField;
        private static FieldInfo _slotHeightField;
        private static FieldInfo _mainBoxField;
        private static FieldInfo _slotPositionField;

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
            // Future tabs: SocialPage, CollectionsPage, CraftingPage, OptionsPage
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

                // The slots render inside mainBox. The first visible slot starts at
                // mainBox.Y + a header area. From diagnostic data: mainBox.Y=72,
                // and the visual content starts roughly at mainBox.Y + slotHeight
                // (the header row with column labels takes about one slotHeight).
                // We use mainBox.Y + slotHeight as the start of slot 0's visual position.
                int slotsYStart = mainBox.Y + slotHeight;

                // How many slots fit visibly (for scrolling pages)
                int visibleSlots = Math.Max(1, (mainBox.Height - slotHeight) / slotHeight);

                bool verbose = ModEntry.Config.VerboseLogging;
                if (verbose)
                    Monitor?.Log($"[GameMenu] AnimalPage: {charSlots.Count} slots, slotHeight={slotHeight}, mainBox=({mainBox.X},{mainBox.Y},{mainBox.Width},{mainBox.Height}), slotsYStart={slotsYStart}, visibleSlots={visibleSlots}, slotPosition={slotPosition}", LogLevel.Info);

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
        /// Draw the finger cursor on tabs where Android suppresses drawMouse.
        /// </summary>
        private static void Draw_Postfix(GameMenu __instance, SpriteBatch b)
        {
            try
            {
                if (!ModEntry.Config.EnableGameMenuNavigation)
                    return;

                var page = __instance.pages != null && __instance.currentTab >= 0 && __instance.currentTab < __instance.pages.Count
                    ? __instance.pages[__instance.currentTab]
                    : null;
                if (page == null) return;

                string typeName = page.GetType().Name;

                // Only draw cursor on tabs we fix
                if (typeName != "AnimalPage")
                    return;

                var snapped = page.currentlySnappedComponent;
                if (snapped == null || snapped.bounds.Y <= 0)
                    return;

                int cursorTile = Game1.options.snappyMenus ? 44 : Game1.mouseCursor;
                b.Draw(
                    Game1.mouseCursors,
                    new Vector2(snapped.bounds.Center.X, snapped.bounds.Center.Y),
                    Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, cursorTile, 16, 16),
                    Color.White,
                    0f,
                    Vector2.Zero,
                    4f + Game1.dialogueButtonScale / 150f,
                    SpriteEffects.None,
                    1f
                );
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GameMenu] Error in Draw_Postfix: {ex.Message}", LogLevel.Error);
            }
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
