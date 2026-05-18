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
    /// Two-press A/X on the geode-cracking menu at Clint, matching console (Switch)
    /// behaviour. Vanilla Android X immediately calls OnPlaceGeodeOnAnvil →
    /// startGeodeCrack atomically; the geode never visibly "sits" on the anvil
    /// before Clint hammers. A toggles a tooltip that has no console analogue.
    ///
    /// With this patch:
    ///   * First A (or X) on a highlighted geode copies it onto the anvil
    ///     (visible) without consuming the inventory slot.
    ///   * Second A (or X) routes through vanilla OnPlaceGeodeOnAnvil to start
    ///     the crack animation. Money/space checks and the golden coconut mutex
    ///     run normally because we go through the vanilla path.
    ///   * B with anvil empty closes the menu (vanilla). B with anvil occupied
    ///     cancels the placement without closing.
    ///   * All input is blocked while the crack animation runs or the coconut
    ///     mutex is pending (mirrors vanilla's receiveLeftClick guard).
    ///   * D-pad / thumbstick navigation passes through to vanilla untouched —
    ///     selection on the inventory side stays sticky to the on-anvil slot
    ///     until cracked or cancelled (so navigating to a different geode
    ///     mid-placement does NOT swap the anvil; B then A swaps).
    ///
    /// We never draw a cursor sprite (see feedback_console_ux_no_cursor); the
    /// anvil's geode sprite IS the indicator that "this is queued."
    /// </summary>
    internal static class GeodeMenuPatches
    {
        private static IMonitor Monitor;

        // Inventory slot whose copy is currently on the anvil. -1 = empty.
        private static int _geodeOnAnvilIndex = -1;

        // GeodeMenu private members. The Android port often diverges from the
        // PC DLL on private surface area, so resolve by string at startup.
        private static FieldInfo _selectedItemIndexField;
        private static MethodInfo _onPlaceGeodeOnAnvilMethod;
        // InventoryMenu.currentlySelectedItem is Android-only — not in the
        // PC DLL the project compiles against. Reflect to keep the build clean.
        private static FieldInfo _currentlySelectedItemField;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                _selectedItemIndexField = AccessTools.Field(typeof(GeodeMenu), "_selectedItemIndex");
                _onPlaceGeodeOnAnvilMethod = AccessTools.Method(typeof(GeodeMenu), "OnPlaceGeodeOnAnvil");
                _currentlySelectedItemField = AccessTools.Field(typeof(InventoryMenu), "currentlySelectedItem");

                if (_selectedItemIndexField == null)
                    Monitor.Log("[GeodeMenu] _selectedItemIndex field not found — patch will pass through to vanilla.", LogLevel.Warn);
                if (_onPlaceGeodeOnAnvilMethod == null)
                    Monitor.Log("[GeodeMenu] OnPlaceGeodeOnAnvil method not found — patch will pass through to vanilla.", LogLevel.Warn);
                if (_currentlySelectedItemField == null)
                    Monitor.Log("[GeodeMenu] InventoryMenu.currentlySelectedItem not found — sticky-anvil cross-slot crack may consume the wrong slot. Patch still attached.", LogLevel.Warn);

                harmony.Patch(
                    original: AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(ReceiveGamePadButton_Prefix))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.startGeodeCrack)),
                    postfix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(StartGeodeCrack_Postfix))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.update)),
                    postfix: new HarmonyMethod(typeof(GeodeMenuPatches), nameof(Update_Postfix))
                );

                Monitor.Log(
                    $"GeodeMenu two-press patch attached. "
                    + $"_selectedItemIndex={(_selectedItemIndexField != null ? "OK" : "NULL")}, "
                    + $"OnPlaceGeodeOnAnvil={(_onPlaceGeodeOnAnvilMethod != null ? "OK" : "NULL")}, "
                    + $"currentlySelectedItem={(_currentlySelectedItemField != null ? "OK" : "NULL")}",
                    LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to attach GeodeMenu patch: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Called from ModEntry.OnMenuChanged on every GeodeMenu open/close.</summary>
        public static void OnMenuChanged()
        {
            // We never consume from inventory on placement, only copy. Dropping
            // the index is enough — the inventory item is untouched.
            Monitor?.Log($"[GeodeMenu] OnMenuChanged: resetting _geodeOnAnvilIndex (was {_geodeOnAnvilIndex})", LogLevel.Info);
            _geodeOnAnvilIndex = -1;
        }

        private static bool ReceiveGamePadButton_Prefix(GeodeMenu __instance, Buttons b)
        {
            // DIAGNOSTIC v3.7.22 — log every entry so we can prove the prefix is firing.
            Monitor.Log($"[GeodeMenu] Prefix HIT button={b} configEnabled={ModEntry.Config?.EnableConsoleGeodeMenu} _geodeOnAnvilIndex={_geodeOnAnvilIndex} geodeSpotItem={(__instance.geodeSpot?.item == null ? "null" : __instance.geodeSpot.item.Name)} animTimer={__instance.geodeAnimationTimer} waitingForServer={__instance.waitingForServerResponse}", LogLevel.Info);

            if (ModEntry.Config?.EnableConsoleGeodeMenu != true)
            {
                Monitor.Log("[GeodeMenu] Prefix: config disabled, passing through.", LogLevel.Info);
                return true;
            }

            if (_selectedItemIndexField == null || _onPlaceGeodeOnAnvilMethod == null)
            {
                Monitor.Log("[GeodeMenu] Prefix: reflection failed at startup, passing through.", LogLevel.Info);
                return true;
            }

            try
            {
                if (__instance.geodeAnimationTimer > 0 || __instance.waitingForServerResponse)
                {
                    Monitor.Log("[GeodeMenu] Prefix: animation/mutex busy, blocking input.", LogLevel.Info);
                    return false;
                }

                bool anvilOccupied = _geodeOnAnvilIndex >= 0 && __instance.geodeSpot?.item != null;
                Monitor.Log($"[GeodeMenu] Prefix: anvilOccupied={anvilOccupied}", LogLevel.Info);

                switch (b)
                {
                    case Buttons.A:
                    case Buttons.X:
                        if (anvilOccupied)
                        {
                            Monitor.Log($"[GeodeMenu] Prefix: routing {b} → CrackPlacedGeode", LogLevel.Info);
                            CrackPlacedGeode(__instance);
                        }
                        else
                        {
                            Monitor.Log($"[GeodeMenu] Prefix: routing {b} → TryPlaceOnAnvil", LogLevel.Info);
                            TryPlaceOnAnvil(__instance);
                        }
                        return false;

                    case Buttons.B:
                        if (anvilOccupied)
                        {
                            Monitor.Log("[GeodeMenu] Prefix: routing B → CancelPlacement", LogLevel.Info);
                            CancelPlacement(__instance);
                            return false;
                        }
                        Monitor.Log("[GeodeMenu] Prefix: routing B → vanilla close.", LogLevel.Info);
                        return true;

                    default:
                        Monitor.Log($"[GeodeMenu] Prefix: passing {b} through to vanilla.", LogLevel.Info);
                        return true;
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[GeodeMenu] receiveGamePadButton error: {ex}", LogLevel.Error);
                return true;
            }
        }

        private static void TryPlaceOnAnvil(GeodeMenu menu)
        {
            int selected = (int)_selectedItemIndexField.GetValue(menu);
            Monitor.Log($"[GeodeMenu] TryPlaceOnAnvil: _selectedItemIndex={selected} inventoryCount={menu.inventory?.actualInventory?.Count} heldItem={(menu.heldItem == null ? "null" : menu.heldItem.Name)}", LogLevel.Info);

            if (selected < 0)
            {
                Monitor.Log("[GeodeMenu] TryPlaceOnAnvil: no selection, bail.", LogLevel.Info);
                return;
            }

            if (selected >= menu.inventory.actualInventory.Count)
            {
                Monitor.Log("[GeodeMenu] TryPlaceOnAnvil: selected index out of range, bail.", LogLevel.Info);
                return;
            }

            var slot = menu.inventory.actualInventory[selected];
            Monitor.Log($"[GeodeMenu] TryPlaceOnAnvil: slot={(slot == null ? "null" : slot.Name + " x" + slot.Stack)} IsGeode={(slot == null ? "n/a" : Utility.IsGeode(slot).ToString())}", LogLevel.Info);
            if (slot == null || !Utility.IsGeode(slot))
            {
                Monitor.Log("[GeodeMenu] TryPlaceOnAnvil: not a geode, bail.", LogLevel.Info);
                return;
            }

            if (menu.heldItem != null)
            {
                Monitor.Log("[GeodeMenu] TryPlaceOnAnvil: heldItem non-null, bail.", LogLevel.Info);
                return;
            }

            menu.geodeSpot.item = slot.getOne();
            _geodeOnAnvilIndex = selected;
            Game1.playSound("stoneStep");
            Monitor.Log($"[GeodeMenu] TryPlaceOnAnvil: PLACED geodeSpot.item={menu.geodeSpot.item?.Name} _geodeOnAnvilIndex={_geodeOnAnvilIndex}", LogLevel.Info);
        }

        private static void CrackPlacedGeode(GeodeMenu menu)
        {
            int idx = _geodeOnAnvilIndex;
            Monitor.Log($"[GeodeMenu] CrackPlacedGeode: _geodeOnAnvilIndex={idx}", LogLevel.Info);
            if (idx < 0 || idx >= menu.inventory.actualInventory.Count)
            {
                Monitor.Log("[GeodeMenu] CrackPlacedGeode: index invalid, cancel.", LogLevel.Info);
                CancelPlacement(menu);
                return;
            }

            var slot = menu.inventory.actualInventory[idx];
            if (slot == null || !Utility.IsGeode(slot))
            {
                Monitor.Log($"[GeodeMenu] CrackPlacedGeode: slot stale (slot={(slot == null ? "null" : slot.Name)}), cancel.", LogLevel.Info);
                CancelPlacement(menu);
                return;
            }

            // Sync inventory.currentlySelectedItem AND _selectedItemIndex to the
            // on-anvil slot before invoking vanilla. startGeodeCrack uses
            // currentlySelectedItem to remove the inventory entry when the stack
            // hits zero; CrackGoldenCoconut captures it at call time and restores
            // it later. Without this sync, navigating away after placement and
            // then cracking would consume the wrong inventory slot.
            _currentlySelectedItemField?.SetValue(menu.inventory, idx);
            _selectedItemIndexField.SetValue(menu, idx);

            // startGeodeCrack will re-set geodeSpot.item from heldItem.getOne().
            // Clearing here avoids a one-frame ghost when both the queued item
            // and the cracking item would otherwise be set.
            menu.geodeSpot.item = null;
            menu.heldItem = slot;

            Monitor.Log("[GeodeMenu] CrackPlacedGeode: invoking OnPlaceGeodeOnAnvil via reflection.", LogLevel.Info);
            _onPlaceGeodeOnAnvilMethod.Invoke(menu, null);
            Monitor.Log($"[GeodeMenu] CrackPlacedGeode: post-invoke animTimer={menu.geodeAnimationTimer} waitingForServer={menu.waitingForServerResponse} heldItem={(menu.heldItem == null ? "null" : menu.heldItem.Name)} geodeSpotItem={(menu.geodeSpot?.item == null ? "null" : menu.geodeSpot.item.Name)}", LogLevel.Info);

            // If vanilla bailed for no money / no inventory space (neither
            // animation started nor mutex pending), null heldItem so the menu
            // can still close, and restore the anvil visual so the user sees
            // the queued geode and can recover (earn money, drop an item).
            if (menu.geodeAnimationTimer <= 0 && !menu.waitingForServerResponse)
            {
                menu.heldItem = null;
                if (menu.geodeSpot.item == null)
                    menu.geodeSpot.item = slot.getOne();
                Monitor.Log("[GeodeMenu] CrackPlacedGeode: failed-cleanup branch — restored visual, cleared heldItem.", LogLevel.Info);
            }
        }

        private static void CancelPlacement(GeodeMenu menu)
        {
            Monitor.Log("[GeodeMenu] CancelPlacement: clearing anvil.", LogLevel.Info);
            menu.geodeSpot.item = null;
            _geodeOnAnvilIndex = -1;
            Game1.playSound("smallSelect");
        }

        private static void StartGeodeCrack_Postfix()
        {
            // The vanilla startGeodeCrack consumed the geode from inventory
            // and now owns geodeSpot.item for the duration of the animation.
            Monitor.Log($"[GeodeMenu] StartGeodeCrack_Postfix: resetting _geodeOnAnvilIndex (was {_geodeOnAnvilIndex})", LogLevel.Info);
            _geodeOnAnvilIndex = -1;
        }

        private static void Update_Postfix(GeodeMenu __instance)
        {
            // Defensive: when the crack animation completes (update lines
            // 286–298) vanilla nulls geodeSpot.item. If anything left
            // _geodeOnAnvilIndex set in that window, clear it so the next
            // A press doesn't try to re-crack a non-existent geode.
            if (__instance.geodeAnimationTimer <= 0
                && __instance.geodeSpot?.item == null
                && _geodeOnAnvilIndex >= 0)
            {
                Monitor.Log($"[GeodeMenu] Update_Postfix: defensive reset (was {_geodeOnAnvilIndex})", LogLevel.Info);
                _geodeOnAnvilIndex = -1;
            }
        }
    }
}
