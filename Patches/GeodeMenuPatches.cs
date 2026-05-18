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

                Monitor.Log("GeodeMenu two-press patch attached.", LogLevel.Trace);
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
            _geodeOnAnvilIndex = -1;
        }

        private static bool ReceiveGamePadButton_Prefix(GeodeMenu __instance, Buttons b)
        {
            if (ModEntry.Config?.EnableConsoleGeodeMenu != true)
                return true;

            if (_selectedItemIndexField == null || _onPlaceGeodeOnAnvilMethod == null)
                return true;

            try
            {
                if (__instance.geodeAnimationTimer > 0 || __instance.waitingForServerResponse)
                    return false;

                bool anvilOccupied = _geodeOnAnvilIndex >= 0 && __instance.geodeSpot?.item != null;

                switch (b)
                {
                    case Buttons.A:
                    case Buttons.X:
                        if (anvilOccupied)
                            CrackPlacedGeode(__instance);
                        else
                            TryPlaceOnAnvil(__instance);
                        return false;

                    case Buttons.B:
                        if (anvilOccupied)
                        {
                            CancelPlacement(__instance);
                            return false;
                        }
                        return true;

                    default:
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
            if (selected < 0)
                return; // No selection — vanilla X-case has the same guard (line 633).

            if (selected >= menu.inventory.actualInventory.Count)
                return;

            var slot = menu.inventory.actualInventory[selected];
            if (slot == null || !Utility.IsGeode(slot))
                return;

            if (menu.heldItem != null)
                return; // Something is mid-flight; let it resolve.

            menu.geodeSpot.item = slot.getOne();
            _geodeOnAnvilIndex = selected;
            Game1.playSound("stoneStep");
        }

        private static void CrackPlacedGeode(GeodeMenu menu)
        {
            int idx = _geodeOnAnvilIndex;
            if (idx < 0 || idx >= menu.inventory.actualInventory.Count)
            {
                CancelPlacement(menu);
                return;
            }

            var slot = menu.inventory.actualInventory[idx];
            if (slot == null || !Utility.IsGeode(slot))
            {
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

            _onPlaceGeodeOnAnvilMethod.Invoke(menu, null);

            // If vanilla bailed for no money / no inventory space (neither
            // animation started nor mutex pending), null heldItem so the menu
            // can still close, and restore the anvil visual so the user sees
            // the queued geode and can recover (earn money, drop an item).
            if (menu.geodeAnimationTimer <= 0 && !menu.waitingForServerResponse)
            {
                menu.heldItem = null;
                if (menu.geodeSpot.item == null)
                    menu.geodeSpot.item = slot.getOne();
            }
        }

        private static void CancelPlacement(GeodeMenu menu)
        {
            menu.geodeSpot.item = null;
            _geodeOnAnvilIndex = -1;
            Game1.playSound("smallSelect");
        }

        private static void StartGeodeCrack_Postfix()
        {
            // The vanilla startGeodeCrack consumed the geode from inventory
            // and now owns geodeSpot.item for the duration of the animation.
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
                _geodeOnAnvilIndex = -1;
            }
        }
    }
}
