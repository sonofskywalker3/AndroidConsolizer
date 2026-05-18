using System;
using System.Reflection;
using HarmonyLib;
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
                monitor.Log($"[GeodeMenu] reflection: _showTooltip={(_showTooltipField != null ? "OK" : "NULL")}, "
                    + $"currentlySelectedItem={(_inventoryCurrentlySelectedItemField != null ? "OK" : "NULL")}, "
                    + $"_selectedItemIndex={(_selectedItemIndexField != null ? "OK" : "NULL")}, "
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
            _lastLoggedSelIdx = -99;
            _lastLoggedCurSel = -99;
            _lastLoggedMouseX = -99999;
            _lastLoggedMouseY = -99999;
        }

        /// <summary>Called from ModEntry.OnMenuChanged when a GeodeMenu OPENS.
        /// Sets _showTooltip = true on the fresh instance so vanilla's
        /// per-press fall-through auto-fires GamePadShowInfoPanel.</summary>
        public static void OnGeodeMenuOpened(GeodeMenu menu)
        {
            if (menu == null || _showTooltipField == null) return;
            if (ModEntry.Config?.EnableConsoleGeodeMenu != true) return;
            try
            {
                _showTooltipField.SetValue(menu, true);
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"[GeodeMenu] couldn't force _showTooltip=true: {ex.Message}", LogLevel.Warn); } catch { }
            }
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
