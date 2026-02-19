using System;
using System.Collections;
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
    /// Adds controller snap navigation to Generic Mod Config Menu (GMCM) pages.
    ///
    /// GMCM uses SpaceShared's custom UI framework (Table/Element), not Stardew's
    /// ClickableComponent system. It has zero gamepad support by design — mouse/touch only.
    ///
    /// This patch adds:
    /// - D-pad/left stick Up/Down to navigate between rows
    /// - D-pad/left stick Left/Right to adjust checkboxes and other interactive elements
    /// - A button to click/toggle the focused element
    /// - B button to go back
    /// - Right stick to scroll
    /// - Left stick suppression (no free cursor)
    ///
    /// Works both in-game (GMCM as GameMenu._childMenu) and on the title screen.
    /// </summary>
    internal static class GmcmPatches
    {
        private static IMonitor Monitor;

        // GMCM type detection (resolved at runtime via reflection)
        private static Type _modConfigMenuType;
        private static Type _specificModConfigMenuType;

        // SpaceShared Table reflection — per-type because ModConfigMenu and SpecificModConfigMenu
        // are sibling classes, each with their own Table field
        private static readonly Dictionary<Type, FieldInfo> _tableFieldCache = new Dictionary<Type, FieldInfo>();
        private static FieldInfo _tableRowsField;     // Table.Rows (List<Element[]>)
        private static PropertyInfo _tableScrollbarProp; // Table.Scrollbar
        private static FieldInfo _tableSizeField;     // Table.size (Vector2)
        private static FieldInfo _tableRowHeightField; // Table.rowHeight (int)

        // SpaceShared Scrollbar reflection
        private static MethodInfo _scrollbarScrollBy;  // Scrollbar.ScrollBy(int)
        private static PropertyInfo _scrollbarTopRow;   // Scrollbar.TopRow
        private static PropertyInfo _scrollbarFrameSize; // Scrollbar.FrameSize
        private static PropertyInfo _scrollbarRows;     // Scrollbar.Rows

        // SpaceShared Element reflection
        private static PropertyInfo _elementBounds;     // Element.Bounds (Rectangle)

        // SpaceShared Checkbox reflection
        private static Type _checkboxType;
        private static PropertyInfo _checkboxChecked;   // Checkbox.Checked (bool)
        private static PropertyInfo _checkboxCallback;  // Checkbox.Callback (Action)

        // SpaceShared Label reflection
        private static Type _labelType;
        private static PropertyInfo _labelString;       // Label.String (string)
        private static PropertyInfo _labelCallback;     // Label.Callback (Action)

        // GMCM SpecificModConfigMenu back navigation
        private static FieldInfo _returnToListField;    // SpecificModConfigMenu.ReturnToList (Action)

        // Navigation state
        private static int _focusedRow = -1;
        private static bool _isActive = false;
        private static object _currentGmcmInstance = null;

        // Same-tick guard for A-press (block touch-sim double-fire)
        private static int _aPressTick = -1;

        // Left stick discrete navigation state
        private static int _stickNavDir = 0;
        private static int _stickNavLastTick = -1;
        private static bool _stickNavInitial = true;
        private const float StickNavThreshold = 0.5f;
        private const int StickNavInitialDelay = 18;
        private const int StickNavRepeatDelay = 9;

        // Right stick scroll
        private const float StickScrollThreshold = 0.2f;

        /// <summary>Whether GMCM snap navigation is currently active (suppresses left stick).</summary>
        public static bool ShouldSuppressLeftStick()
        {
            if (!ModEntry.Config.EnableGameMenuNavigation || !Game1.options.gamepadControls)
                return false;
            if (ModEntry.Config.FreeCursorOnSettings)
                return false;
            return _isActive;
        }

        /// <summary>Try to detect and activate GMCM navigation. Called from OnUpdateTicked.</summary>
        public static void Update()
        {
            if (!ModEntry.Config.EnableGameMenuNavigation || ModEntry.Config.FreeCursorOnSettings)
            {
                if (_isActive) Deactivate();
                return;
            }

            // Check for GMCM menu
            var gmcmMenu = FindGmcmMenu();
            if (gmcmMenu != null)
            {
                if (!_isActive || _currentGmcmInstance != gmcmMenu)
                    Activate(gmcmMenu);
                UpdateNavigation(gmcmMenu);
            }
            else if (_isActive)
            {
                Deactivate();
            }
        }

        /// <summary>Initialize reflection for GMCM types. Called once during mod init.</summary>
        public static void Initialize(IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                // Find GMCM types by name (not available at compile time)
                _modConfigMenuType = AccessTools.TypeByName("GenericModConfigMenu.Framework.ModConfigMenu");
                _specificModConfigMenuType = AccessTools.TypeByName("GenericModConfigMenu.Framework.SpecificModConfigMenu");

                if (_modConfigMenuType == null && _specificModConfigMenuType == null)
                {
                    Monitor.Log("GMCM not found — snap navigation disabled for mod config.", LogLevel.Trace);
                    return;
                }

                // Cache Table fields for both types
                Type tableType = null;
                foreach (var gmcmType in new[] { _modConfigMenuType, _specificModConfigMenuType })
                {
                    if (gmcmType == null) continue;
                    var tf = AccessTools.Field(gmcmType, "Table");
                    if (tf != null)
                    {
                        _tableFieldCache[gmcmType] = tf;
                        tableType = tf.FieldType;
                    }
                }
                if (tableType == null)
                {
                    Monitor.Log("GMCM Table field not found on either menu type.", LogLevel.Warn);
                    return;
                }
                _tableRowsField = AccessTools.Field(tableType, "Rows");
                _tableScrollbarProp = AccessTools.Property(tableType, "Scrollbar");
                _tableSizeField = AccessTools.Field(tableType, "SizeImpl");
                _tableRowHeightField = AccessTools.Field(tableType, "RowHeightImpl");

                if (_tableRowsField == null)
                {
                    // Dump all fields/properties on Table to find the right name
                    Monitor.Log($"GMCM Table.rows not found. Dumping Table type ({tableType.FullName}):", LogLevel.Warn);
                    foreach (var f in tableType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                        Monitor.Log($"  Field: {f.Name} : {f.FieldType.Name}", LogLevel.Warn);
                    foreach (var p in tableType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                        Monitor.Log($"  Prop: {p.Name} : {p.PropertyType.Name}", LogLevel.Warn);
                }

                // SpaceShared.UI.Element (base type)
                var elementType = AccessTools.TypeByName("SpaceShared.UI.Element");
                if (elementType != null)
                {
                    _elementBounds = AccessTools.Property(elementType, "Bounds");
                }

                // SpaceShared.UI.Checkbox
                _checkboxType = AccessTools.TypeByName("SpaceShared.UI.Checkbox");
                if (_checkboxType != null)
                {
                    _checkboxChecked = AccessTools.Property(_checkboxType, "Checked");
                    _checkboxCallback = AccessTools.Property(_checkboxType, "Callback");
                }

                // SpaceShared.UI.Label
                _labelType = AccessTools.TypeByName("SpaceShared.UI.Label");
                if (_labelType != null)
                {
                    _labelString = AccessTools.Property(_labelType, "String");
                    _labelCallback = AccessTools.Property(_labelType, "Callback");
                }

                // Scrollbar
                if (_tableScrollbarProp != null)
                {
                    var scrollbarType = _tableScrollbarProp.PropertyType;
                    _scrollbarScrollBy = AccessTools.Method(scrollbarType, "ScrollBy", new[] { typeof(int) });
                    _scrollbarTopRow = AccessTools.Property(scrollbarType, "TopRow");
                    _scrollbarFrameSize = AccessTools.Property(scrollbarType, "FrameSize");
                    _scrollbarRows = AccessTools.Property(scrollbarType, "Rows");

                    if (_scrollbarScrollBy == null || _scrollbarTopRow == null)
                    {
                        Monitor.Log($"Scrollbar reflection incomplete. Dumping Scrollbar type ({scrollbarType.FullName}):", LogLevel.Warn);
                        foreach (var f in scrollbarType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                            Monitor.Log($"  Field: {f.Name} : {f.FieldType.Name}", LogLevel.Warn);
                        foreach (var p in scrollbarType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                            Monitor.Log($"  Prop: {p.Name} : {p.PropertyType.Name}", LogLevel.Warn);
                        foreach (var m in scrollbarType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                            if (!m.IsSpecialName && m.DeclaringType == scrollbarType)
                                Monitor.Log($"  Method: {m.Name}({string.Join(", ", Array.ConvertAll(m.GetParameters(), p2 => p2.ParameterType.Name))})", LogLevel.Warn);
                    }
                }

                // SpecificModConfigMenu back navigation
                if (_specificModConfigMenuType != null)
                {
                    _returnToListField = AccessTools.Field(_specificModConfigMenuType, "ReturnToList");
                }

                Monitor.Log($"GMCM patches initialized. Table type: {tableType?.FullName}", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to initialize GMCM patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Apply Harmony patches for GMCM input interception.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            if (_modConfigMenuType == null && _specificModConfigMenuType == null)
                return;

            try
            {
                // GMCM doesn't override receiveGamePadButton — patch the base
                // IClickableMenu.receiveGamePadButton. This is already patched by
                // OptionsPagePatches, but Harmony allows multiple prefixes.
                // Our prefix checks IsGmcmMenu() so it only fires for GMCM instances.
                var receiveGPB = AccessTools.Method(typeof(IClickableMenu),
                    nameof(IClickableMenu.receiveGamePadButton), new[] { typeof(Buttons) });
                if (receiveGPB != null)
                {
                    harmony.Patch(
                        original: receiveGPB,
                        prefix: new HarmonyMethod(typeof(GmcmPatches), nameof(ReceiveGamePadButton_Prefix))
                    );
                    Monitor.Log("GMCM receiveGamePadButton prefix applied (on IClickableMenu base).", LogLevel.Trace);
                }

                // Patch receiveLeftClick on both GMCM menu types (they override it independently)
                foreach (var gmcmType in new[] { _modConfigMenuType, _specificModConfigMenuType })
                {
                    if (gmcmType == null) continue;
                    var receiveLeftClick = AccessTools.Method(gmcmType, "receiveLeftClick",
                        new[] { typeof(int), typeof(int), typeof(bool) });
                    if (receiveLeftClick != null)
                    {
                        harmony.Patch(
                            original: receiveLeftClick,
                            prefix: new HarmonyMethod(typeof(GmcmPatches), nameof(ReceiveLeftClick_Prefix))
                        );
                        Monitor.Log($"GMCM receiveLeftClick prefix applied on {gmcmType.Name}.", LogLevel.Trace);
                    }
                }

                Monitor.Log("GMCM Harmony patches applied.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply GMCM patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Check if the given menu is a GMCM menu type.</summary>
        public static bool IsGmcmMenu(IClickableMenu menu)
        {
            if (menu == null || (_modConfigMenuType == null && _specificModConfigMenuType == null))
                return false;
            var menuType = menu.GetType();
            return (_modConfigMenuType != null && menuType == _modConfigMenuType)
                || (_specificModConfigMenuType != null && menuType == _specificModConfigMenuType);
        }

        /// <summary>
        /// Find the deepest active GMCM menu by walking the _childMenu chain.
        /// Chain can be: GameMenu -> ModConfigMenu -> SpecificModConfigMenu
        /// We want the deepest GMCM menu (that's the one receiving input).
        /// </summary>
        private static IClickableMenu FindGmcmMenu()
        {
            if (_modConfigMenuType == null && _specificModConfigMenuType == null)
                return null;

            var active = Game1.activeClickableMenu;
            if (active == null)
                return null;

            // Walk the _childMenu chain to find the deepest GMCM menu
            IClickableMenu deepestGmcm = null;
            var current = active;
            // Check each level (active itself, then children)
            for (int depth = 0; depth < 5; depth++) // safety limit
            {
                if (IsGmcmMenu(current))
                    deepestGmcm = current;

                var child = current.GetChildMenu();
                if (child == null) break;
                current = child;
            }

            return deepestGmcm;
        }

        private static void Activate(object gmcmMenu)
        {
            _isActive = true;
            _currentGmcmInstance = gmcmMenu;
            _focusedRow = 0;
            _stickNavDir = 0;
            _stickNavInitial = true;

            // Log activation details
            var rows = GetRows(gmcmMenu);
            var table = GetTable(gmcmMenu);
            var tableBounds = GetElementBounds(table);
            int rowHeight = 0;
            if (_tableRowHeightField != null && table != null)
            {
                try { rowHeight = (int)_tableRowHeightField.GetValue(table); } catch { }
            }

            // Also dump Table's Size for layout understanding
            string sizeStr = "?";
            if (_tableSizeField != null && table != null)
            {
                try { sizeStr = _tableSizeField.GetValue(table)?.ToString(); } catch { }
            }

            Monitor?.Log($"[GMCM] Activated on {gmcmMenu.GetType().Name}. Rows: {rows?.Count ?? -1}, RowHeight: {rowHeight}, Table.Bounds: ({tableBounds.X},{tableBounds.Y},{tableBounds.Width}x{tableBounds.Height}), Table.Size: {sizeStr}", LogLevel.Info);

            // Log scrollbar state
            if (table != null && _tableScrollbarProp != null)
            {
                try
                {
                    var sb = GetScrollbar(table);
                    if (sb != null)
                    {
                        int topRow = _scrollbarTopRow != null ? (int)_scrollbarTopRow.GetValue(sb) : -1;
                        int frameSize = _scrollbarFrameSize != null ? (int)_scrollbarFrameSize.GetValue(sb) : -1;
                        int totalRows = _scrollbarRows != null ? (int)_scrollbarRows.GetValue(sb) : -1;
                        Monitor?.Log($"[GMCM] Scrollbar: TopRow={topRow}, FrameSize={frameSize}, TotalRows={totalRows}", LogLevel.Info);
                    }
                }
                catch { }
            }

            if (rows != null && rows.Count > 0)
            {
                // Log first few rows with element bounds for debugging
                for (int i = 0; i < Math.Min(rows.Count, 8); i++)
                {
                    var row = rows[i] as object[];
                    if (row == null)
                    {
                        Monitor?.Log($"[GMCM]   Row[{i}]: null (raw type: {rows[i]?.GetType().FullName ?? "null"})", LogLevel.Info);
                        continue;
                    }
                    string elems = "";
                    foreach (var e in row)
                    {
                        var b = GetElementBounds(e);
                        elems += $"{e?.GetType().Name ?? "null"}@({b.X},{b.Y},{b.Width}x{b.Height}), ";
                    }
                    Monitor?.Log($"[GMCM]   Row[{i}]: [{elems.TrimEnd(',', ' ')}] interactive={IsRowInteractive(row)}", LogLevel.Info);
                }
                if (rows.Count > 8)
                    Monitor?.Log($"[GMCM]   ... {rows.Count - 8} more rows", LogLevel.Info);
            }

            SnapCursorToRow(gmcmMenu);
        }

        private static void Deactivate()
        {
            _isActive = false;
            _currentGmcmInstance = null;
            _focusedRow = -1;
            _aPressTick = -1;
            _stickNavDir = 0;
        }

        /// <summary>Get the Table field for the given menu instance's type.</summary>
        private static FieldInfo GetTableField(object gmcmMenu)
        {
            if (gmcmMenu == null) return null;
            var menuType = gmcmMenu.GetType();
            if (_tableFieldCache.TryGetValue(menuType, out var cached))
                return cached;
            // Try resolving at runtime for unknown subtypes
            var tf = AccessTools.Field(menuType, "Table");
            if (tf != null)
                _tableFieldCache[menuType] = tf;
            return tf;
        }

        /// <summary>Get the Table's rows list via reflection.</summary>
        private static IList GetRows(object gmcmMenu)
        {
            if (_tableRowsField == null) return null;
            var table = GetTable(gmcmMenu);
            if (table == null) return null;
            return _tableRowsField.GetValue(table) as IList;
        }

        /// <summary>Get the Table object from the GMCM menu.</summary>
        private static object GetTable(object gmcmMenu)
        {
            return GetTableField(gmcmMenu)?.GetValue(gmcmMenu);
        }

        /// <summary>Get the Scrollbar from the Table.</summary>
        private static object GetScrollbar(object table)
        {
            return _tableScrollbarProp?.GetValue(table);
        }

        /// <summary>Get bounds of an Element via reflection.</summary>
        private static Rectangle GetElementBounds(object element)
        {
            if (_elementBounds == null || element == null)
                return Rectangle.Empty;
            try
            {
                return (Rectangle)_elementBounds.GetValue(element);
            }
            catch
            {
                return Rectangle.Empty;
            }
        }

        /// <summary>Check if a row contains any interactive element (checkbox, clickable label).</summary>
        private static bool IsRowInteractive(object[] row)
        {
            if (row == null) return false;
            foreach (var elem in row)
            {
                if (elem == null) continue;
                if (_checkboxType != null && _checkboxType.IsInstanceOfType(elem))
                    return true;
                if (_labelType != null && _labelType.IsInstanceOfType(elem))
                {
                    // Labels with callbacks are interactive (clickable links/buttons)
                    var callback = _labelCallback?.GetValue(elem);
                    if (callback != null) return true;
                }
                // Other element types (dropdowns, sliders, keybinds) are also interactive
                // If it's not a plain label without callback, treat as interactive
                var elemType = elem.GetType();
                if (_labelType != null && _labelType.IsInstanceOfType(elem))
                    continue; // Plain label, skip
                if (_checkboxType != null && _checkboxType.IsInstanceOfType(elem))
                    return true;
                // Any non-label element is likely interactive
                return true;
            }
            return false;
        }

        /// <summary>Find the primary interactive element in a row (rightmost non-label element).</summary>
        private static object FindInteractiveElement(object[] row)
        {
            if (row == null) return null;

            // GMCM rows typically have: [Label, Interactive] or just [Interactive]
            // Return the rightmost interactive element
            for (int i = row.Length - 1; i >= 0; i--)
            {
                var elem = row[i];
                if (elem == null) continue;
                if (_checkboxType != null && _checkboxType.IsInstanceOfType(elem))
                    return elem;
                if (_labelType != null && _labelType.IsInstanceOfType(elem))
                {
                    var callback = _labelCallback?.GetValue(elem);
                    if (callback != null) return elem;
                    continue;
                }
                // Non-label = interactive
                return elem;
            }
            return null;
        }

        /// <summary>Find the label element in a row (for display purposes).</summary>
        private static object FindLabelElement(object[] row)
        {
            if (row == null) return null;
            foreach (var elem in row)
            {
                if (elem == null) continue;
                if (_labelType != null && _labelType.IsInstanceOfType(elem))
                    return elem;
            }
            return null;
        }

        /// <summary>Snap the mouse cursor to the focused row's interactive element.</summary>
        private static void SnapCursorToRow(object gmcmMenu)
        {
            var rows = GetRows(gmcmMenu);
            if (rows == null || _focusedRow < 0 || _focusedRow >= rows.Count)
                return;

            var row = rows[_focusedRow] as object[];
            if (row == null) return;

            // Find the interactive element to snap to
            var interactive = FindInteractiveElement(row);
            var target = interactive ?? (row.Length > 0 ? row[0] : null);
            if (target == null) return;

            // Element.Bounds Y values are table-local (all rows report the same Y).
            // Compute screen Y from: tableBounds.Y + (rowIndex - topRow) * rowHeight + rowHeight/2
            var table = GetTable(gmcmMenu);
            if (table == null) return;

            var tableBounds = GetElementBounds(table);
            int rowHeight = 0;
            if (_tableRowHeightField != null)
            {
                try { rowHeight = (int)_tableRowHeightField.GetValue(table); } catch { }
            }

            int topRow = 0;
            var scrollbar = GetScrollbar(table);
            if (scrollbar != null && _scrollbarTopRow != null)
            {
                try { topRow = (int)_scrollbarTopRow.GetValue(scrollbar); } catch { }
            }

            if (rowHeight > 0)
            {
                // Compute screen position from table layout
                int screenY = tableBounds.Y + (_focusedRow - topRow) * rowHeight + rowHeight / 2;
                // Element.Bounds.X is already an absolute screen coordinate (same as Table.Bounds.X)
                // Don't add tableBounds.X again — just use elemBounds.X directly
                var elemBounds = GetElementBounds(target);
                int screenX = elemBounds.Width > 0
                    ? elemBounds.X + elemBounds.Width / 2
                    : tableBounds.X + tableBounds.Width / 2;
                Game1.setMousePosition(screenX, screenY);
            }
            else
            {
                // Fallback to raw element bounds
                var bounds = GetElementBounds(target);
                if (bounds != Rectangle.Empty)
                    Game1.setMousePosition(bounds.Center.X, bounds.Center.Y);
            }
        }

        /// <summary>Ensure the focused row is visible by scrolling if needed.</summary>
        private static void EnsureRowVisible(object gmcmMenu)
        {
            var table = GetTable(gmcmMenu);
            if (table == null) return;

            var scrollbar = GetScrollbar(table);
            if (scrollbar == null) return;

            try
            {
                int topRow = (int)_scrollbarTopRow.GetValue(scrollbar);
                int frameSize = (int)_scrollbarFrameSize.GetValue(scrollbar);

                if (_focusedRow < topRow)
                {
                    // Need to scroll up
                    _scrollbarScrollBy?.Invoke(scrollbar, new object[] { _focusedRow - topRow });
                }
                else if (_focusedRow >= topRow + frameSize)
                {
                    // Need to scroll down
                    _scrollbarScrollBy?.Invoke(scrollbar, new object[] { _focusedRow - topRow - frameSize + 1 });
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GMCM] EnsureRowVisible error: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Navigate up/down to the next interactive row.</summary>
        private static void NavigateVertical(object gmcmMenu, bool up)
        {
            var rows = GetRows(gmcmMenu);
            if (rows == null || rows.Count == 0) return;

            int startRow = _focusedRow;
            int dir = up ? -1 : 1;
            int current = _focusedRow + dir;

            // Search for next interactive row, wrapping around
            for (int i = 0; i < rows.Count; i++)
            {
                if (current < 0) current = rows.Count - 1;
                else if (current >= rows.Count) current = 0;

                var row = rows[current] as object[];
                if (IsRowInteractive(row))
                {
                    _focusedRow = current;
                    EnsureRowVisible(gmcmMenu);
                    SnapCursorToRow(gmcmMenu);
                    Game1.playSound("shiny4");
                    return;
                }
                current += dir;
            }
        }

        /// <summary>Handle A-button press — click the focused element.</summary>
        private static void HandleAPress(object gmcmMenu)
        {
            var rows = GetRows(gmcmMenu);
            if (rows == null || _focusedRow < 0 || _focusedRow >= rows.Count)
                return;

            var row = rows[_focusedRow] as object[];
            var interactive = FindInteractiveElement(row);
            if (interactive == null) return;

            // Set same-tick guard
            _aPressTick = Game1.ticks;

            if (_checkboxType != null && _checkboxType.IsInstanceOfType(interactive))
            {
                // Toggle checkbox directly
                bool current = (bool)_checkboxChecked.GetValue(interactive);
                _checkboxChecked.SetValue(interactive, !current);
                var callback = _checkboxCallback?.GetValue(interactive) as Action;
                callback?.Invoke();
                Game1.playSound("drumkit6");
                return;
            }

            if (_labelType != null && _labelType.IsInstanceOfType(interactive))
            {
                // Click the label (triggers callback — opens mod config page, etc.)
                var callback = _labelCallback?.GetValue(interactive) as Action;
                if (callback != null)
                {
                    callback.Invoke();
                    Game1.playSound("breathin");
                    // Deactivate — the callback likely changed the menu
                    Deactivate();
                    return;
                }
            }

            // For other element types (dropdowns, keybinds, etc.), simulate a mouse click
            // by snapping cursor and letting the next frame's touch-sim handle it
            var bounds = GetElementBounds(interactive);
            if (bounds != Rectangle.Empty)
            {
                Game1.setMousePosition(bounds.Center.X, bounds.Center.Y);
                // The touch-sim from Game1.updateActiveMenu will fire receiveLeftClick
                // after this receiveGamePadButton returns, which will hit the element
            }
        }

        /// <summary>Handle B-button press — go back or close GMCM.</summary>
        private static void HandleBPress(object gmcmMenu)
        {
            // SpecificModConfigMenu: use ReturnToList callback to go back to mod list
            if (_specificModConfigMenuType != null && _specificModConfigMenuType.IsInstanceOfType(gmcmMenu))
            {
                if (_returnToListField != null)
                {
                    var returnToList = _returnToListField.GetValue(gmcmMenu) as Action;
                    if (returnToList != null)
                    {
                        returnToList.Invoke();
                        Game1.playSound("bigDeSelect");
                        Deactivate();
                        return;
                    }
                }
            }

            // ModConfigMenu (mod list): close the GMCM overlay entirely
            // Simulate clicking the upper-right close button
            var menu = gmcmMenu as IClickableMenu;
            if (menu?.upperRightCloseButton != null)
            {
                menu.receiveLeftClick(
                    menu.upperRightCloseButton.bounds.Center.X,
                    menu.upperRightCloseButton.bounds.Center.Y,
                    true);
                Deactivate();
                return;
            }

            // Fallback: call exitThisMenu
            menu?.exitThisMenu(true);
            Deactivate();
        }

        /// <summary>Handle left/right — toggle checkboxes, or navigate mod list.</summary>
        private static void HandleLeftRight(object gmcmMenu, bool right)
        {
            var rows = GetRows(gmcmMenu);
            if (rows == null || _focusedRow < 0 || _focusedRow >= rows.Count)
                return;

            var row = rows[_focusedRow] as object[];
            var interactive = FindInteractiveElement(row);
            if (interactive == null) return;

            if (_checkboxType != null && _checkboxType.IsInstanceOfType(interactive))
            {
                // Left/Right toggles checkbox
                bool current = (bool)_checkboxChecked.GetValue(interactive);
                bool target = right; // Right = ON, Left = OFF
                if (current != target)
                {
                    _checkboxChecked.SetValue(interactive, target);
                    var callback = _checkboxCallback?.GetValue(interactive) as Action;
                    callback?.Invoke();
                    Game1.playSound("drumkit6");
                }
            }
        }

        /// <summary>Handle right stick scrolling.</summary>
        private static void HandleRightStickScroll(object gmcmMenu)
        {
            float rightY = GameplayButtonPatches.RawRightStickY;
            if (Math.Abs(rightY) <= StickScrollThreshold)
                return;

            var table = GetTable(gmcmMenu);
            if (table == null) return;

            var scrollbar = GetScrollbar(table);
            if (scrollbar == null) return;

            try
            {
                // Scroll direction: stick up (positive Y) = scroll up (negative delta)
                int delta = rightY > 0 ? -1 : 1;
                _scrollbarScrollBy?.Invoke(scrollbar, new object[] { delta });

                // Update focus to stay visible
                int topRow = (int)_scrollbarTopRow.GetValue(scrollbar);
                int frameSize = (int)_scrollbarFrameSize.GetValue(scrollbar);
                if (_focusedRow < topRow || _focusedRow >= topRow + frameSize)
                {
                    // Focus scrolled off-screen — snap to nearest visible interactive row
                    var rows = GetRows(gmcmMenu);
                    if (rows != null)
                    {
                        int viewCenter = topRow + frameSize / 2;
                        int bestRow = -1;
                        int bestDist = int.MaxValue;
                        for (int i = topRow; i < Math.Min(topRow + frameSize, rows.Count); i++)
                        {
                            var row = rows[i] as object[];
                            if (IsRowInteractive(row))
                            {
                                int dist = Math.Abs(i - viewCenter);
                                if (dist < bestDist) { bestDist = dist; bestRow = i; }
                            }
                        }
                        if (bestRow >= 0)
                        {
                            _focusedRow = bestRow;
                            SnapCursorToRow(gmcmMenu);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[GMCM] HandleRightStickScroll error: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Tick update — handles stick navigation and right stick scroll.</summary>
        private static void UpdateNavigation(object gmcmMenu)
        {
            if (!Game1.options.gamepadControls)
                return;

            // Left stick → discrete navigation
            float lsX = GameplayButtonPatches.RawLeftStickX;
            float lsY = GameplayButtonPatches.RawLeftStickY;
            int newDir = 0;
            if (Math.Abs(lsY) >= Math.Abs(lsX))
            {
                if (lsY > StickNavThreshold) newDir = 1;       // Up
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
                _stickNavDir = newDir;
                _stickNavLastTick = Game1.ticks;
                _stickNavInitial = true;
                HandleStickDirection(gmcmMenu, newDir);
            }
            else
            {
                int elapsed = Game1.ticks - _stickNavLastTick;
                int delay = _stickNavInitial ? StickNavInitialDelay : StickNavRepeatDelay;
                if (elapsed >= delay)
                {
                    _stickNavLastTick = Game1.ticks;
                    _stickNavInitial = false;
                    HandleStickDirection(gmcmMenu, newDir);
                }
            }

            // Right stick scroll
            HandleRightStickScroll(gmcmMenu);

            // Re-snap cursor every frame to track the focused element
            SnapCursorToRow(gmcmMenu);
        }

        private static void HandleStickDirection(object gmcmMenu, int dir)
        {
            switch (dir)
            {
                case 1: NavigateVertical(gmcmMenu, up: true); break;
                case 2: NavigateVertical(gmcmMenu, up: false); break;
                case 3: HandleLeftRight(gmcmMenu, right: false); break;
                case 4: HandleLeftRight(gmcmMenu, right: true); break;
            }
        }

        /// <summary>
        /// Prefix for GMCM's receiveGamePadButton (or IClickableMenu base if GMCM doesn't override).
        /// Intercepts D-pad, A, B for snap navigation.
        /// </summary>
        private static bool ReceiveGamePadButton_Prefix(IClickableMenu __instance, Buttons b)
        {
            if (!_isActive || !IsGmcmMenu(__instance))
                return true;
            if (!ModEntry.Config.EnableGameMenuNavigation || ModEntry.Config.FreeCursorOnSettings)
                return true;
            if (!Game1.options.gamepadControls)
                return true;

            var gmcmMenu = __instance;

            // B button: close/go back
            if (b == Buttons.B)
            {
                HandleBPress(gmcmMenu);
                return false;
            }

            // D-pad Up/Down
            if (b == Buttons.DPadUp || b == Buttons.LeftThumbstickUp)
            {
                NavigateVertical(gmcmMenu, up: true);
                return false;
            }
            if (b == Buttons.DPadDown || b == Buttons.LeftThumbstickDown)
            {
                NavigateVertical(gmcmMenu, up: false);
                return false;
            }

            // D-pad Left/Right
            if (b == Buttons.DPadLeft || b == Buttons.LeftThumbstickLeft)
            {
                HandleLeftRight(gmcmMenu, right: false);
                return false;
            }
            if (b == Buttons.DPadRight || b == Buttons.LeftThumbstickRight)
            {
                HandleLeftRight(gmcmMenu, right: true);
                return false;
            }

            // A button
            if (b == Buttons.A)
            {
                HandleAPress(gmcmMenu);
                return false;
            }

            return true;
        }

        /// <summary>Block touch-sim clicks on the same tick as our A-press.</summary>
        private static bool ReceiveLeftClick_Prefix(IClickableMenu __instance, int x, int y)
        {
            if (!_isActive || !IsGmcmMenu(__instance))
                return true;
            if (!ModEntry.Config.EnableGameMenuNavigation || ModEntry.Config.FreeCursorOnSettings)
                return true;

            if (_aPressTick == Game1.ticks)
            {
                _aPressTick = -1;
                return false;
            }
            return true;
        }

        /// <summary>Called when GMCM menu closes.</summary>
        public static void OnMenuClosed()
        {
            Deactivate();
        }
    }
}
