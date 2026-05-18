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
    /// Console-parity geode menu input on Clint's anvil:
    ///   * single-press A places + cracks the selected geode (Switch
    ///     behaviour, confirmed against Switch hardware 2026-05-18);
    ///   * the touch-sim leftClick Android fires after every A press is
    ///     suppressed so it can't pick up the partially-consumed stack;
    ///   * the item tooltip auto-shows for whatever geode is selected,
    ///     instead of vanilla's "press A to toggle" (we redirected A);
    ///   * the snap cursor sits at the slot center instead of vanilla's
    ///     bounds-right/4 offset (which renders top-left of the slot on
    ///     G Cloud's UI scaling for unclear reasons).
    ///
    /// Vanilla Android GeodeMenu has X = place + crack (atomic, one
    /// press, geode visible on anvil for the ~2.7s animation) and A =
    /// toggle _showTooltip. Our redirect re-fires A as X via the public
    /// receiveGamePadButton, then eats the same-tick synthesised
    /// leftClick. _showTooltip is forced true on menu open so the
    /// vanilla fall-through (which runs at the bottom of every
    /// receiveGamePadButton) calls inventory.GamePadShowInfoPanel each
    /// time, keeping the tooltip in sync with the selected slot.
    /// </summary>
    internal static class GeodeMenuPatches
    {
        private static IMonitor Monitor;

        // Recursion guard: when our prefix re-fires the button as X, our
        // own prefix runs again. Skip the second pass so vanilla X executes.
        [ThreadStatic]
        private static bool _inRedirect;

        // Game1.ticks value on which we last redirected A→X. The
        // touch-sim leftClick from the same A press fires later in the
        // same tick; we suppress one leftClick whose tick matches.
        private static int _redirectTick = -1;

        // Set true when we auto-select on menu open; cleared on the first
        // Update_Postfix tick after we successfully fire GamePadShowInfoPanel.
        // Calling GamePadShowInfoPanel directly from OnMenuChanged / the
        // snap-postfix doesn't visibly show the tooltip — showItemInfo seems
        // to get reset before the first draw. Deferring to the first stable
        // update tick after open works.
        private static bool _pendingTooltipShow = false;

        // Reflected private members. Resolved at startup, null-checked
        // at every use so a missing field on some port silently degrades
        // rather than crashing.
        private static FieldInfo _showTooltipField;
        // InventoryMenu.currentlySelectedItem is Android-only — not in
        // the PC DLL the project compiles against.
        private static FieldInfo _inventoryCurrentlySelectedItemField;
        // GeodeMenu._selectedItemIndex is private — needed for diagnostic
        // observation of vanilla nav loops.
        private static FieldInfo _selectedItemIndexField;
        // InventoryMenu.GamePadShowInfoPanel is Android-only — not in the
        // PC DLL the project compiles against.
        private static MethodInfo _gamePadShowInfoPanelMethod;
        // InventoryMenu._iconShakeTimer is the Dictionary<int,double> the
        // base InventoryMenu.draw reads each tick to wobble individual
        // slots. Adding (slotIndex → now + 0.5s) shakes that slot for 0.5s.
        // See decompile InventoryMenu.cs:523 (set) and :835 (read).
        private static FieldInfo _iconShakeTimerField;

        // Tooltip state fields on InventoryMenu — written every tick from
        // Update_Postfix to keep the GeodeMenu tooltip from flickering.
        // Something (almost certainly Android touch-sim releaseLeftClick at
        // decompile InventoryMenu.cs:1409) resets showItemInfo to false
        // intermittently; re-asserting all the fields GamePadShowInfoPanel
        // writes (decompile InventoryMenu.cs:2059) keeps the tooltip stable.
        // We bypass the playSound call inside GamePadShowInfoPanel by
        // writing fields directly via reflection — calling it 60 times/sec
        // would emit 60 smallSelect sounds.
        private static FieldInfo _showItemInfoField;
        private static FieldInfo _actualItemSelectedField;
        private static FieldInfo _hoverTextField;
        private static FieldInfo _hoverTitleField;
        private static FieldInfo _infoPanelPositionField;
        private static MethodInfo _getPositionOfSellPanelMethod;
        private static MethodInfo _getItemFromClickableComponentMethod;

        // Diagnostic state (v3.7.30 — observation only, throttled to changes).
        private static int _lastLoggedSelIdx = -99;
        private static int _lastLoggedCurSel = -99;
        private static int _lastLoggedMouseX = -99999;
        private static int _lastLoggedMouseY = -99999;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                _showTooltipField = AccessTools.Field(typeof(GeodeMenu), "_showTooltip");
                _inventoryCurrentlySelectedItemField = AccessTools.Field(typeof(InventoryMenu), "currentlySelectedItem");
                _selectedItemIndexField = AccessTools.Field(typeof(GeodeMenu), "_selectedItemIndex");
                _gamePadShowInfoPanelMethod = AccessTools.Method(typeof(InventoryMenu), "GamePadShowInfoPanel");
                _iconShakeTimerField = AccessTools.Field(typeof(InventoryMenu), "_iconShakeTimer");
                _showItemInfoField = AccessTools.Field(typeof(InventoryMenu), "showItemInfo");
                _actualItemSelectedField = AccessTools.Field(typeof(InventoryMenu), "actualItemSelected");
                _hoverTextField = AccessTools.Field(typeof(InventoryMenu), "hoverText");
                _hoverTitleField = AccessTools.Field(typeof(InventoryMenu), "hoverTitle");
                _infoPanelPositionField = AccessTools.Field(typeof(InventoryMenu), "infoPanelPosition");
                _getPositionOfSellPanelMethod = AccessTools.Method(typeof(InventoryMenu), "getPositionOfSellPanel");
                _getItemFromClickableComponentMethod = AccessTools.Method(typeof(InventoryMenu), "getItemFromClickableComponent");
                monitor.Log($"[GeodeMenu] reflection: _showTooltip={(_showTooltipField != null ? "OK" : "NULL")}, "
                    + $"currentlySelectedItem={(_inventoryCurrentlySelectedItemField != null ? "OK" : "NULL")}, "
                    + $"_selectedItemIndex={(_selectedItemIndexField != null ? "OK" : "NULL")}, "
                    + $"_iconShakeTimer={(_iconShakeTimerField != null ? "OK" : "NULL")}, "
                    + $"showItemInfo={(_showItemInfoField != null ? "OK" : "NULL")}, "
                    + $"actualItemSelected={(_actualItemSelectedField != null ? "OK" : "NULL")}, "
                    + $"hoverText={(_hoverTextField != null ? "OK" : "NULL")}, "
                    + $"infoPanelPosition={(_infoPanelPositionField != null ? "OK" : "NULL")}, "
                    + $"getPositionOfSellPanel={(_getPositionOfSellPanelMethod != null ? "OK" : "NULL")}, "
                    + $"getItemFromClickableComponent={(_getItemFromClickableComponentMethod != null ? "OK" : "NULL")}, "
                    + $"GamePadShowInfoPanel={(_gamePadShowInfoPanelMethod != null ? "OK" : "NULL")}", LogLevel.Info);

                harmony.Patch(
                    original: AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(ReceiveGamePadButton_Prefix)),
                    postfix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(ReceiveGamePadButton_Postfix))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.receiveLeftClick)),
                    prefix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(ReceiveLeftClick_Prefix))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.startGeodeCrack)),
                    postfix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(StartGeodeCrack_Postfix))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.update)),
                    postfix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(Update_Postfix))
                );
                // applyMovementKey is declared on IClickableMenu base and
                // ISN'T overridden by GeodeMenu — patch the base. v3.7.30
                // diagnostic showed Game1.UpdateControlInput calls
                // activeClickableMenu.applyMovementKey(direction) AFTER
                // receiveGamePadButton returns, walking
                // currentlySnappedComponent's neighbor IDs and re-running
                // snapCursorToCurrentSnappedComponent — overwriting any
                // snap state we set in our postfix. Suppress for GeodeMenu
                // so our own nav (in the prefix) owns the entire state.
                harmony.Patch(
                    original: AccessTools.Method(typeof(IClickableMenu), nameof(IClickableMenu.applyMovementKey), new System.Type[] { typeof(int) }),
                    prefix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(ApplyMovementKey_Prefix))
                );
                // snapToDefaultClickableComponent runs INSIDE the GeodeMenu
                // constructor (only when SnappyMenus is true) and unconditionally
                // snaps cursor to slot 0. We re-snap to the first geode in inventory
                // in our postfix so the cursor lands on the right slot from the very
                // first rendered frame — calling our own snap from OnGeodeMenuOpened
                // (which fires AFTER the ctor) caused a visible 1-2 frame cursor
                // blink at slot 0 before re-snapping.
                harmony.Patch(
                    original: AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.snapToDefaultClickableComponent)),
                    postfix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(SnapToDefaultClickableComponent_Postfix))
                );
                monitor.Log("GeodeMenu patches attached (A→X + touch-sim + tooltip + spatial nav + diagnostic).", LogLevel.Info);
            }
            catch (Exception ex)
            {
                monitor.Log($"Failed to attach GeodeMenu patch: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Called from ModEntry.OnMenuChanged on every GeodeMenu open/close.</summary>
        public static void OnMenuChanged()
        {
            _redirectTick = -1;
            _pendingTooltipShow = false;
            _lastLoggedSelIdx = -99;
            _lastLoggedCurSel = -99;
            _lastLoggedMouseX = -99999;
            _lastLoggedMouseY = -99999;
        }

        /// <summary>Called from ModEntry.OnMenuChanged when a GeodeMenu OPENS.
        /// Sets _showTooltip = true on the fresh instance so vanilla's
        /// per-press fall-through auto-fires GamePadShowInfoPanel, and
        /// auto-selects the first geode in inventory so the user lands on
        /// something actionable instead of having to press a direction to
        /// pick up a selection (vanilla opens with _selectedItemIndex = -1).</summary>
        public static void OnGeodeMenuOpened(GeodeMenu menu)
        {
            if (menu == null || _showTooltipField == null) return;
            if (ModEntry.Config?.EnableConsoleGeodeMenu != true) return;
            try
            {
                _showTooltipField.SetValue(menu, true);
                AutoSelectFirstGeode(menu);
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"[GeodeMenu] OnGeodeMenuOpened error: {ex.Message}", LogLevel.Warn); } catch { }
            }
        }

        /// <summary>
        /// Find the first geode in inventory and snap the selection +
        /// cursor to it. Mirrors the in-nav pattern used by DoSpatialNav
        /// (both fields set, snap cursor, force transparency, kick info
        /// panel). If the player has no geodes, leaves selection at -1.
        /// </summary>
        private static void AutoSelectFirstGeode(GeodeMenu menu)
        {
            if (_selectedItemIndexField == null || _inventoryCurrentlySelectedItemField == null) return;
            if (menu.inventory?.actualInventory == null || menu.inventory.inventory == null) return;

            int total = menu.inventory.actualInventory.Count;
            int firstGeode = -1;
            for (int i = 0; i < total; i++)
            {
                if (IsGeodeAt(menu.inventory, i)) { firstGeode = i; break; }
            }
            if (firstGeode < 0)
            {
                try { Monitor.Log("[GeodeMenu] auto-select: no geodes in inventory, leaving selection unset", LogLevel.Info); } catch { }
                return;
            }

            _selectedItemIndexField.SetValue(menu, firstGeode);
            _inventoryCurrentlySelectedItemField.SetValue(menu.inventory, firstGeode);

            if (firstGeode < menu.inventory.inventory.Count)
            {
                var slot = menu.inventory.inventory[firstGeode];
                if (slot != null)
                {
                    menu.currentlySnappedComponent = slot;
                    menu.snapCursorToCurrentSnappedComponent();
                    if (Game1.mouseCursorTransparency < 0.99f) Game1.mouseCursorTransparency = 1f;
                }
            }

            // Defer the tooltip-show to Update_Postfix — calling it directly from
            // OnMenuChanged didn't render the tooltip (showItemInfo getting cleared
            // somewhere between here and the first draw, per v3.7.35 test).
            _pendingTooltipShow = true;

            try { Monitor.Log($"[GeodeMenu] auto-selected first geode at slot {firstGeode}", LogLevel.Info); } catch { }
        }

        private static bool ReceiveGamePadButton_Prefix(GeodeMenu __instance, Buttons b)
        {
            if (_inRedirect) return true;
            if (ModEntry.Config?.EnableConsoleGeodeMenu != true) return true;

            // A → X redirect (one-press place + crack, Switch parity).
            if (b == Buttons.A)
            {
                _inRedirect = true;
                try
                {
                    _redirectTick = Game1.ticks;
                    try { Monitor.Log($"[GeodeMenu] A redirect → X at tick {_redirectTick}", LogLevel.Info); } catch { }
                    // Inventory-full rejection feedback before vanilla runs. Vanilla's
                    // inventory-full branch (decompile GeodeMenu.cs:683-688) is silent
                    // and invisible — just descriptionText + wiggle/alert timers. We
                    // add buzzer + slot shake to match the rejection cues other Stardew
                    // menus give. Vanilla X still runs after this so the text/timers fire.
                    TryEmitInventoryFullFeedback(__instance);
                    __instance.receiveGamePadButton(Buttons.X);
                }
                catch (Exception ex)
                {
                    try { Monitor.Log($"[GeodeMenu] A→X redirect error: {ex.Message}", LogLevel.Error); } catch { }
                    return true;
                }
                finally
                {
                    _inRedirect = false;
                }
                return false;
            }

            // Spatial geode-only nav. Replaces vanilla's linear
            // _selectedItemIndex scan (lines 563-622) which moves UP/LEFT
            // by -1 and DOWN/RIGHT by +1 regardless of grid position,
            // making UP feel like LEFT. We compute the spatial neighbour
            // in the requested direction and skip non-geodes within that
            // direction's traversal.
            int dir = NavDirection(b);
            if (dir >= 0)
            {
                try
                {
                    DoSpatialNav(__instance, dir);
                }
                catch (Exception ex)
                {
                    try { Monitor.Log($"[GeodeMenu] spatial nav error: {ex.Message}", LogLevel.Error); } catch { }
                    return true; // fall back to vanilla on error
                }
                return false;
            }

            return true;
        }

        /// <summary>
        /// Mirrors vanilla GeodeMenu.OnPlaceGeodeOnAnvil's inventory-full check
        /// (decompile lines 661-688) and adds buzzer + slot shake so the user
        /// gets a rejection cue instead of vanilla's silent text-only fail.
        /// Called before forwarding A→X; vanilla still runs after this so its
        /// descriptionText + wiggleWordsTimer + alertTimer still fire.
        /// </summary>
        private static void TryEmitInventoryFullFeedback(GeodeMenu menu)
        {
            try
            {
                if (_selectedItemIndexField == null) return;
                if (menu?.inventory?.actualInventory == null) return;

                int idx = (int)_selectedItemIndexField.GetValue(menu);
                if (idx < 0 || idx >= menu.inventory.actualInventory.Count) return;

                var geode = menu.inventory.actualInventory[idx];
                if (geode == null || !Utility.IsGeode(geode)) return;
                if (Game1.player.Money < 25) return;               // vanilla money branch already shakes the money box
                if (Game1.player.freeSpotsInInventory() >= 1) return;
                if (geode.Stack <= 1) return;                       // vanilla allows the last one in even on a full inventory

                Game1.playSound("cancel");
                AddInventorySlotShake(menu.inventory, idx);
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"[GeodeMenu] inventory-full feedback error: {ex.Message}", LogLevel.Warn); } catch { }
            }
        }

        private static void AddInventorySlotShake(InventoryMenu inventory, int slotIndex)
        {
            if (_iconShakeTimerField == null) return;
            var dict = _iconShakeTimerField.GetValue(inventory) as Dictionary<int, double>;
            if (dict == null) return;
            // Vanilla cadence: TotalSeconds + 0.5 — see InventoryMenu.cs:523.
            dict[slotIndex] = Game1.currentGameTime.TotalGameTime.TotalSeconds + 0.5;
        }

        private static int NavDirection(Buttons b)
        {
            switch (b)
            {
                case Buttons.DPadUp:
                case Buttons.LeftThumbstickUp: return 0;
                case Buttons.DPadRight:
                case Buttons.LeftThumbstickRight: return 1;
                case Buttons.DPadDown:
                case Buttons.LeftThumbstickDown: return 2;
                case Buttons.DPadLeft:
                case Buttons.LeftThumbstickLeft: return 3;
                default: return -1;
            }
        }

        // Inventory grid is 12 columns wide on Stardew (constant across
        // all menus). 36 slots = 3 rows. ClickableComponents in
        // inventory.inventory are indexed in row-major order so col = i%12,
        // row = i/12.
        private const int INV_COLS = 12;

        private static void DoSpatialNav(GeodeMenu menu, int direction)
        {
            if (_selectedItemIndexField == null || _inventoryCurrentlySelectedItemField == null) return;
            if (menu.inventory?.actualInventory == null || menu.inventory.inventory == null) return;

            int current = (int)_selectedItemIndexField.GetValue(menu);
            int total = menu.inventory.actualInventory.Count;

            // -1 = no prior selection. First nav lands on the first geode
            // in inventory regardless of direction so the user isn't
            // stuck pressing nothing.
            int target = -1;
            if (current < 0)
            {
                for (int i = 0; i < total; i++)
                {
                    if (IsGeodeAt(menu.inventory, i)) { target = i; break; }
                }
            }
            else
            {
                int row = current / INV_COLS;
                int col = current % INV_COLS;
                int totalRows = (total + INV_COLS - 1) / INV_COLS;
                switch (direction)
                {
                    case 0: // UP — scan column above first, then any geode in rows above (nearest column wins)
                        for (int r = row - 1; r >= 0 && target < 0; r--)
                        {
                            int idx = r * INV_COLS + col;
                            if (idx < total && IsGeodeAt(menu.inventory, idx)) { target = idx; break; }
                        }
                        if (target < 0)
                            target = NearestGeodeInRowRange(menu.inventory, 0, row - 1, col, total);
                        break;
                    case 2: // DOWN — scan column below first, then any geode in rows below
                        for (int r = row + 1; r < totalRows && target < 0; r++)
                        {
                            int idx = r * INV_COLS + col;
                            if (idx < total && IsGeodeAt(menu.inventory, idx)) { target = idx; break; }
                        }
                        if (target < 0)
                            target = NearestGeodeInRowRange(menu.inventory, row + 1, totalRows - 1, col, total);
                        break;
                    case 1: // RIGHT — within row
                        for (int c = col + 1; c < INV_COLS; c++)
                        {
                            int idx = row * INV_COLS + c;
                            if (idx < total && IsGeodeAt(menu.inventory, idx)) { target = idx; break; }
                        }
                        break;
                    case 3: // LEFT — within row
                        for (int c = col - 1; c >= 0; c--)
                        {
                            int idx = row * INV_COLS + c;
                            if (idx >= 0 && IsGeodeAt(menu.inventory, idx)) { target = idx; break; }
                        }
                        break;
                }
            }

            try { Monitor.Log($"[GeodeMenu/nav] dir={direction} from={current} → target={target}", LogLevel.Info); } catch { }
            if (target < 0) return; // no geode in that direction — stay put.

            _selectedItemIndexField.SetValue(menu, target);
            _inventoryCurrentlySelectedItemField.SetValue(menu.inventory, target);
            if (target < menu.inventory.inventory.Count)
            {
                var slot = menu.inventory.inventory[target];
                if (slot != null)
                {
                    menu.currentlySnappedComponent = slot;
                    menu.snapCursorToCurrentSnappedComponent();
                    if (Game1.mouseCursorTransparency < 0.99f) Game1.mouseCursorTransparency = 1f;
                }
            }

            // Mirror vanilla's GamePadShowInfoPanel for the new selection
            // (vanilla normally does this via the receiveGamePadButton
            // fall-through; we returned false above so that didn't run).
            // Reflected because the method is Android-only.
            if (_gamePadShowInfoPanelMethod != null)
            {
                try { _gamePadShowInfoPanelMethod.Invoke(menu.inventory, null); } catch { }
            }
        }

        private static bool IsGeodeAt(InventoryMenu inv, int idx)
        {
            if (idx < 0 || idx >= inv.actualInventory.Count) return false;
            var item = inv.actualInventory[idx];
            if (item == null) return false;
            return inv.highlightMethod == null || inv.highlightMethod(item);
        }

        /// <summary>
        /// Fallback for UP/DOWN when no geode exists directly above/below
        /// in the same column. Scans rows in [rowMin, rowMax] for any
        /// geode and returns the one whose column is closest to
        /// preferredCol. Empty range or no geode found → -1.
        /// </summary>
        private static int NearestGeodeInRowRange(InventoryMenu inv, int rowMin, int rowMax, int preferredCol, int total)
        {
            if (rowMin > rowMax) return -1;
            int best = -1, bestDist = int.MaxValue;
            for (int r = rowMin; r <= rowMax; r++)
            {
                for (int c = 0; c < INV_COLS; c++)
                {
                    int idx = r * INV_COLS + c;
                    if (idx >= total) break;
                    if (!IsGeodeAt(inv, idx)) continue;
                    int dist = Math.Abs(c - preferredCol);
                    // Prefer nearest column. Tie-break: nearer row (rowMin is
                    // already the immediate neighbour row for DOWN, last row
                    // for UP, so iteration order picks nearer row first by
                    // accident — explicit distance check would be:
                    // overall_dist = dist + |r-row|*INV_COLS, but column
                    // preference is good enough.)
                    if (dist < bestDist)
                    {
                        best = idx;
                        bestDist = dist;
                        if (bestDist == 0) return best;
                    }
                }
                // If we already found something in this row, prefer it
                // over geodes farther away. Stop scanning subsequent rows.
                if (best >= 0) return best;
            }
            return best;
        }

        /// <summary>
        /// Postfix on GeodeMenu.snapToDefaultClickableComponent: after vanilla
        /// snaps cursor to slot 0 (id=0) inside the constructor, re-snap to the
        /// first geode slot in inventory. Runs before the first rendered frame,
        /// so the cursor visibly starts on the correct slot — no blink. Also
        /// covers later snap-to-default invocations (e.g. when state changes
        /// after a crack consumes the previously-selected stack).
        /// </summary>
        private static void SnapToDefaultClickableComponent_Postfix(GeodeMenu __instance)
        {
            if (ModEntry.Config?.EnableConsoleGeodeMenu != true) return;
            if (_selectedItemIndexField == null || _inventoryCurrentlySelectedItemField == null) return;
            if (__instance?.inventory?.actualInventory == null || __instance.inventory.inventory == null) return;

            int total = __instance.inventory.actualInventory.Count;
            int firstGeode = -1;
            for (int i = 0; i < total; i++)
            {
                if (IsGeodeAt(__instance.inventory, i)) { firstGeode = i; break; }
            }
            if (firstGeode < 0 || firstGeode >= __instance.inventory.inventory.Count) return;
            var slot = __instance.inventory.inventory[firstGeode];
            if (slot == null) return;

            try
            {
                _selectedItemIndexField.SetValue(__instance, firstGeode);
                _inventoryCurrentlySelectedItemField.SetValue(__instance.inventory, firstGeode);
                __instance.currentlySnappedComponent = slot;
                __instance.snapCursorToCurrentSnappedComponent();
                if (Game1.mouseCursorTransparency < 0.99f) Game1.mouseCursorTransparency = 1f;
                _pendingTooltipShow = true;
                try { Monitor?.Log($"[GeodeMenu] snap-to-default → first geode at slot {firstGeode}", LogLevel.Info); } catch { }
            }
            catch (Exception ex)
            {
                try { Monitor?.Log($"[GeodeMenu] snap-to-default postfix error: {ex.Message}", LogLevel.Warn); } catch { }
            }
        }

        /// <summary>
        /// Suppress IClickableMenu.applyMovementKey for GeodeMenu so the
        /// game's spatial neighbour-ID nav can't fire after our prefix
        /// has already updated everything. Without this, the cursor
        /// jumps to whatever applyMovementKey's neighbour walk lands on
        /// (slot 12 below slot 0 etc.) overwriting our snap.
        /// </summary>
        private static bool ApplyMovementKey_Prefix(IClickableMenu __instance)
        {
            if (!(__instance is GeodeMenu)) return true;
            if (ModEntry.Config?.EnableConsoleGeodeMenu != true) return true;
            return false;
        }

        private static bool ReceiveLeftClick_Prefix()
        {
            if (ModEntry.Config?.EnableConsoleGeodeMenu != true) return true;
            if (_redirectTick != Game1.ticks) return true;

            // Same-tick touch-sim leftClick from the A press we just
            // redirected. Eat it so it can't pick up the partially-
            // consumed geode stack or disturb snap state.
            try { Monitor.Log($"[GeodeMenu] suppressing touch-sim leftClick at tick {Game1.ticks}", LogLevel.Info); } catch { }
            _redirectTick = -1;
            return false;
        }

        private static void StartGeodeCrack_Postfix(GeodeMenu __instance)
        {
            try { Monitor.Log($"[GeodeMenu] startGeodeCrack fired. animTimer={__instance.geodeAnimationTimer}", LogLevel.Info); } catch { }
        }

        /// <summary>
        /// Replicates the writes vanilla GamePadShowInfoPanel performs
        /// (decompile InventoryMenu.cs:2059) without the Game1.playSound call —
        /// safe to invoke every tick. Keeps the tooltip visible for as long as the
        /// menu is open + a geode is selected, overriding whatever resets
        /// showItemInfo (most likely Android touch-sim releaseLeftClick at
        /// InventoryMenu.cs:1409 firing after the A press that opens the menu).
        /// Also logs the computed infoPanelPosition once per fresh open via the
        /// _pendingTooltipShow gate so we have diagnostic data for the
        /// "tooltip positioned wrong" report.
        /// </summary>
        private static void ReassertTooltipState(GeodeMenu menu)
        {
            if (ModEntry.Config?.EnableConsoleGeodeMenu != true) return;
            if (menu?.inventory?.actualInventory == null || menu.inventory.inventory == null) return;
            if (_inventoryCurrentlySelectedItemField == null || _showItemInfoField == null) return;

            try
            {
                int idx = (int)_inventoryCurrentlySelectedItemField.GetValue(menu.inventory);
                if (idx < 0
                    || idx >= menu.inventory.actualInventory.Count
                    || idx >= menu.inventory.inventory.Count) return;

                var item = menu.inventory.actualInventory[idx];
                var slot = menu.inventory.inventory[idx];
                if (item == null || slot == null) return;

                _showItemInfoField.SetValue(menu.inventory, true);

                if (_actualItemSelectedField != null)
                {
                    object actualItem = item;
                    if (_getItemFromClickableComponentMethod != null)
                    {
                        try { actualItem = _getItemFromClickableComponentMethod.Invoke(menu.inventory, new object[] { slot }) ?? item; } catch { }
                    }
                    _actualItemSelectedField.SetValue(menu.inventory, actualItem);
                }
                if (_hoverTextField != null) _hoverTextField.SetValue(menu.inventory, item.getDescription() ?? "");
                if (_hoverTitleField != null) _hoverTitleField.SetValue(menu.inventory, item.DisplayName ?? "");

                Vector2 pos = Vector2.Zero;
                if (_getPositionOfSellPanelMethod != null && _infoPanelPositionField != null)
                {
                    try
                    {
                        pos = (Vector2)_getPositionOfSellPanelMethod.Invoke(menu.inventory, new object[] { slot.bounds.X, slot.bounds.Y, 0 });
                        _infoPanelPositionField.SetValue(menu.inventory, pos);
                    }
                    catch { }
                }

                if (_pendingTooltipShow)
                {
                    _pendingTooltipShow = false;
                    try { Monitor?.Log($"[GeodeMenu] tooltip re-assert ON: slot={idx} bounds={slot.bounds} infoPanelPosition=({pos.X:F0},{pos.Y:F0}) item={item.DisplayName}", LogLevel.Info); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { Monitor?.Log($"[GeodeMenu] tooltip re-assert failed: {ex.Message}", LogLevel.Warn); } catch { }
            }
        }

        /// <summary>
        /// Sync the snap cursor to the selected geode slot after each
        /// nav press. Vanilla GeodeMenu.receiveGamePadButton's D-pad /
        /// stick cases (decompile lines 563-622) walk _selectedItemIndex
        /// directly over geode slots without calling applyMovementKey,
        /// so currentlySnappedComponent never updates and snapCursor
        /// never runs — the mouse cursor stays at slot 0 (set by the
        /// constructor's snapToDefaultClickableComponent) while the
        /// selection highlight tracks elsewhere. Every other inventory
        /// menu delegates nav to applyMovementKey which calls snapCursor
        /// at the end, putting the cursor on the bottom-right of the
        /// newly snapped slot. This postfix replicates that for
        /// GeodeMenu: after vanilla nav updates _selectedItemIndex (and
        /// the fall-through syncs inventory.currentlySelectedItem), we
        /// find the slot at that index, point currentlySnappedComponent
        /// at it, and call snapCursorToCurrentSnappedComponent so
        /// vanilla's own positioning math runs.
        /// </summary>
        private static void ReceiveGamePadButton_Postfix(GeodeMenu __instance, Buttons b)
        {
            bool isNav = b == Buttons.DPadUp || b == Buttons.DPadDown || b == Buttons.DPadLeft || b == Buttons.DPadRight
                || b == Buttons.LeftThumbstickUp || b == Buttons.LeftThumbstickDown
                || b == Buttons.LeftThumbstickLeft || b == Buttons.LeftThumbstickRight;

            // Diagnostic: log every nav button arrival with full state
            // BEFORE and AFTER our cursor-sync attempt.
            if (isNav)
            {
                LogNavState(__instance, b, "POST-VANILLA");
            }

            if (ModEntry.Config?.EnableConsoleGeodeMenu != true) return;
            if (_inventoryCurrentlySelectedItemField == null) return;
            if (!isNav) return;

            try
            {
                if (__instance.inventory?.inventory == null)
                {
                    try { Monitor.Log("[GeodeMenu/diag] cursor sync: inventory or inventory.inventory NULL", LogLevel.Info); } catch { }
                    return;
                }
                int selected = (int)_inventoryCurrentlySelectedItemField.GetValue(__instance.inventory);
                int invCount = __instance.inventory.inventory.Count;
                try { Monitor.Log($"[GeodeMenu/diag] cursor sync: selected={selected}, inventoryComponentCount={invCount}", LogLevel.Info); } catch { }

                if (selected < 0 || selected >= invCount)
                {
                    try { Monitor.Log($"[GeodeMenu/diag] cursor sync bail: selected out of range", LogLevel.Info); } catch { }
                    return;
                }
                var slot = __instance.inventory.inventory[selected];
                if (slot == null)
                {
                    try { Monitor.Log($"[GeodeMenu/diag] cursor sync bail: slot[{selected}] is NULL", LogLevel.Info); } catch { }
                    return;
                }

                int preX = Game1.getMouseX(), preY = Game1.getMouseY();
                __instance.currentlySnappedComponent = slot;
                __instance.snapCursorToCurrentSnappedComponent();
                int postX = Game1.getMouseX(), postY = Game1.getMouseY();
                try { Monitor.Log($"[GeodeMenu/diag] cursor sync ran. slot.bounds={slot.bounds}, mouse {preX},{preY} → {postX},{postY}, mouseCursorTransparency={Game1.mouseCursorTransparency:F2}", LogLevel.Info); } catch { }
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"[GeodeMenu] cursor sync failed: {ex.Message}", LogLevel.Warn); } catch { }
            }
        }

        /// <summary>
        /// Diagnostic — every tick, log _selectedItemIndex and
        /// inventory.currentlySelectedItem and Game1.getMouseX/Y when
        /// any of them changes. Throttled to deltas only (no spam).
        /// Goal: see what vanilla nav and our patches are doing in
        /// real time without manually pressing a button.
        /// </summary>
        private static void Update_Postfix(GeodeMenu __instance)
        {
            // Re-assert tooltip state each tick. v3.7.37's deferred GamePadShowInfoPanel
            // call fired correctly (logs confirm) but the tooltip still flickered out —
            // something (almost certainly Android touch-sim releaseLeftClick at
            // InventoryMenu.cs:1409, which unconditionally sets showItemInfo = false
            // when the click is inside the inventory bounds) was wiping showItemInfo
            // after our first set. Writing all the GamePadShowInfoPanel fields directly
            // each tick keeps the tooltip stable for as long as the menu is open. We
            // skip the smallSelect sound vanilla plays in GamePadShowInfoPanel —
            // calling the method 60×/sec would emit 60 sounds. _pendingTooltipShow
            // remains as a "first tick" signal so we can log the initial positioning
            // numbers for the position-issue diagnostic.
            ReassertTooltipState(__instance);

            if (Monitor == null) return;
            try
            {
                int selIdx = (_selectedItemIndexField != null) ? (int)_selectedItemIndexField.GetValue(__instance) : -99;
                int curSel = (_inventoryCurrentlySelectedItemField != null && __instance.inventory != null)
                    ? (int)_inventoryCurrentlySelectedItemField.GetValue(__instance.inventory)
                    : -99;
                int mx = Game1.getMouseX();
                int my = Game1.getMouseY();

                if (selIdx != _lastLoggedSelIdx || curSel != _lastLoggedCurSel || mx != _lastLoggedMouseX || my != _lastLoggedMouseY)
                {
                    string snapped = __instance.currentlySnappedComponent != null
                        ? $"snap={__instance.currentlySnappedComponent.myID}@{__instance.currentlySnappedComponent.bounds}"
                        : "snap=null";
                    Monitor.Log($"[GeodeMenu/diag] state Δ: _selectedItemIndex={selIdx} currentlySelectedItem={curSel} mouse=({mx},{my}) transparency={Game1.mouseCursorTransparency:F2} {snapped}", LogLevel.Info);
                    _lastLoggedSelIdx = selIdx;
                    _lastLoggedCurSel = curSel;
                    _lastLoggedMouseX = mx;
                    _lastLoggedMouseY = my;
                }
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"[GeodeMenu/diag] update log failed: {ex.Message}", LogLevel.Warn); } catch { }
            }
        }

        private static void LogNavState(GeodeMenu menu, Buttons b, string label)
        {
            try
            {
                int selIdx = (_selectedItemIndexField != null) ? (int)_selectedItemIndexField.GetValue(menu) : -99;
                int curSel = (_inventoryCurrentlySelectedItemField != null && menu.inventory != null)
                    ? (int)_inventoryCurrentlySelectedItemField.GetValue(menu.inventory)
                    : -99;
                int snapID = menu.currentlySnappedComponent?.myID ?? -99;
                Monitor.Log($"[GeodeMenu/diag] {label} button={b}: _selectedItemIndex={selIdx} currentlySelectedItem={curSel} snap.myID={snapID} mouse=({Game1.getMouseX()},{Game1.getMouseY()})", LogLevel.Info);
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"[GeodeMenu/diag] {label} log failed: {ex.Message}", LogLevel.Warn); } catch { }
            }
        }
    }
}
