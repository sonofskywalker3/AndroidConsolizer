using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Handles slingshot ammo management via controller.
    /// Android-adapted behavior:
    /// - A button on ammo = select it (tracked internally)
    /// - Y button on slingshot = attach selected ammo, or detach if nothing selected
    /// </summary>
    internal static class SlingshotPatches
    {
        private static IMonitor Monitor;

        // Track the selected ammo slot (since Android's A button doesn't use CursorSlotItem)
        private static int SelectedAmmoSlot = -1;

        /// <summary>Apply patches (currently none needed - logic is in TryHandleAmmo).</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            Monitor.Log("Slingshot patches applied successfully.", LogLevel.Trace);
        }

        /// <summary>
        /// Called when A button is pressed in inventory. Tracks if ammo was selected.
        /// </summary>
        public static void OnAButtonPressed(GameMenu gameMenu, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                var inventoryPage = gameMenu.pages[GameMenu.inventoryTab] as InventoryPage;
                if (inventoryPage == null) return;

                var pageSnapped = inventoryPage.currentlySnappedComponent;
                if (pageSnapped == null) return;

                int slotId = pageSnapped.myID;
                if (slotId < 0 || slotId >= Game1.player.Items.Count) return;

                Item item = Game1.player.Items[slotId];
                if (item == null) return;

                // Check if it's a valid ammo item (any SObject that isn't a tool/ring/etc)
                if (item is SObject obj && item is not Tool && item is not StardewValley.Objects.Ring)
                {
                    SelectedAmmoSlot = slotId;
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"Slingshot: Selected {item.Name} at slot {slotId} for ammo", LogLevel.Debug);
                }
                else
                {
                    if (SelectedAmmoSlot >= 0)
                    {
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"Slingshot: Cleared ammo selection (selected non-ammo item)", LogLevel.Debug);
                        SelectedAmmoSlot = -1;
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Slingshot OnAButton error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Try to handle Y button press for slingshot ammo management.
        /// Called from ModEntry.OnButtonsChanged when Y is pressed in inventory.
        /// </summary>
        public static bool TryHandleAmmo(GameMenu gameMenu, IModHelper helper, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                var inventoryPage = gameMenu.pages[GameMenu.inventoryTab] as InventoryPage;
                if (inventoryPage == null)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log("Slingshot: Could not get InventoryPage", LogLevel.Debug);
                    return false;
                }

                var pageSnapped = inventoryPage.currentlySnappedComponent;

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"Slingshot: page.snapped={pageSnapped?.myID.ToString() ?? "null"}, selectedAmmoSlot={SelectedAmmoSlot}", LogLevel.Debug);

                int slotId = -1;
                if (pageSnapped != null && pageSnapped.myID >= 0 && pageSnapped.myID < Game1.player.Items.Count)
                    slotId = pageSnapped.myID;

                if (slotId < 0)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log("Slingshot: No valid slot ID found", LogLevel.Debug);
                    return false;
                }

                Item hoveredItem = Game1.player.Items[slotId];

                if (hoveredItem is not Slingshot slingshot)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"Slingshot: Slot {slotId} is not a slingshot: {hoveredItem?.Name ?? "null"}", LogLevel.Debug);
                    return false;
                }

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"Slingshot: Y pressed on slingshot '{slingshot.Name}'", LogLevel.Debug);

                // First check if holding ammo on cursor (console-style inventory management)
                Item cursorItem = Game1.player.CursorSlotItem;
                if (cursorItem is SObject && cursorItem is not Tool && cursorItem is not StardewValley.Objects.Ring)
                {
                    Monitor.Log($"Slingshot: Attaching cursor item {cursorItem.Name} to slingshot", LogLevel.Debug);
                    bool result = HandleAttachFromCursor(slingshot, cursorItem);
                    SelectedAmmoSlot = -1;
                    return result;
                }

                // Fall back to check if we have a selected ammo slot
                if (SelectedAmmoSlot >= 0 && SelectedAmmoSlot < Game1.player.Items.Count)
                {
                    Item selectedItem = Game1.player.Items[SelectedAmmoSlot];

                    if (selectedItem is SObject && selectedItem is not Tool && selectedItem is not StardewValley.Objects.Ring)
                    {
                        Monitor.Log($"Slingshot: Attaching selected {selectedItem.Name} from slot {SelectedAmmoSlot}", LogLevel.Debug);
                        bool result = HandleAttachFromSlot(slingshot, selectedItem, SelectedAmmoSlot);
                        SelectedAmmoSlot = -1;
                        return result;
                    }
                    else
                    {
                        Monitor.Log($"Slingshot: Selected slot {SelectedAmmoSlot} no longer contains valid ammo", LogLevel.Debug);
                        SelectedAmmoSlot = -1;
                    }
                }

                // No selected ammo - try to detach from slingshot
                return HandleDetach(slingshot);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Slingshot error: {ex.Message}", LogLevel.Error);
                Monitor.Log(ex.StackTrace, LogLevel.Debug);
                return false;
            }
        }

        /// <summary>Attach ammo from cursor to slingshot.</summary>
        private static bool HandleAttachFromCursor(Slingshot slingshot, Item ammoItem)
        {
            SObject currentAmmo = slingshot.attachments[0];

            if (currentAmmo == null)
            {
                // Empty slot - attach directly
                slingshot.attachments[0] = (SObject)ammoItem;
                Game1.player.CursorSlotItem = null;
                InventoryManagementPatches.OnCursorItemCleared();
                Game1.playSound("button1");
                Monitor.Log($"Slingshot: Attached {ammoItem.Stack}x {ammoItem.Name} from cursor", LogLevel.Info);
            }
            else if (currentAmmo.canStackWith(ammoItem))
            {
                // Same ammo type - stack
                int spaceAvailable = currentAmmo.maximumStackSize() - currentAmmo.Stack;
                int toAdd = Math.Min(spaceAvailable, ammoItem.Stack);

                if (toAdd > 0)
                {
                    currentAmmo.Stack += toAdd;
                    ammoItem.Stack -= toAdd;

                    if (ammoItem.Stack <= 0)
                    {
                        Game1.player.CursorSlotItem = null;
                        InventoryManagementPatches.OnCursorItemCleared();
                    }

                    Game1.playSound("button1");
                    Monitor.Log($"Slingshot: Stacked {toAdd}x ammo from cursor (now {currentAmmo.Stack})", LogLevel.Info);
                }
                else
                {
                    Game1.playSound("cancel");
                    Monitor.Log("Slingshot: Ammo slot full, cannot stack more", LogLevel.Debug);
                }
            }
            else
            {
                // Different ammo - swap
                slingshot.attachments[0] = (SObject)ammoItem;
                Game1.player.CursorSlotItem = currentAmmo;
                Game1.playSound("button1");
                Monitor.Log($"Slingshot: Swapped ammo - attached {ammoItem.Name} from cursor, put {currentAmmo.Name} on cursor", LogLevel.Info);
            }

            return true;
        }

        /// <summary>Attach ammo from inventory slot to slingshot.</summary>
        private static bool HandleAttachFromSlot(Slingshot slingshot, Item ammoItem, int sourceSlot)
        {
            SObject currentAmmo = slingshot.attachments[0];

            if (currentAmmo == null)
            {
                // Empty slot - attach directly
                slingshot.attachments[0] = (SObject)ammoItem;
                Game1.player.Items[sourceSlot] = null;
                Game1.playSound("button1");
                Monitor.Log($"Slingshot: Attached {ammoItem.Stack}x {ammoItem.Name} to ammo slot", LogLevel.Info);
            }
            else if (currentAmmo.canStackWith(ammoItem))
            {
                // Same ammo type - stack
                int spaceAvailable = currentAmmo.maximumStackSize() - currentAmmo.Stack;
                int toAdd = Math.Min(spaceAvailable, ammoItem.Stack);

                if (toAdd > 0)
                {
                    currentAmmo.Stack += toAdd;
                    ammoItem.Stack -= toAdd;

                    if (ammoItem.Stack <= 0)
                        Game1.player.Items[sourceSlot] = null;

                    Game1.playSound("button1");
                    Monitor.Log($"Slingshot: Stacked {toAdd}x ammo (now {currentAmmo.Stack})", LogLevel.Info);
                }
                else
                {
                    Game1.playSound("cancel");
                    Monitor.Log("Slingshot: Ammo slot full, cannot stack more", LogLevel.Debug);
                }
            }
            else
            {
                // Different ammo - swap
                slingshot.attachments[0] = (SObject)ammoItem;
                Game1.player.Items[sourceSlot] = currentAmmo;
                Game1.playSound("button1");
                Monitor.Log($"Slingshot: Swapped ammo - attached {ammoItem.Name}, removed {currentAmmo.Name} to slot {sourceSlot}", LogLevel.Info);
            }

            return true;
        }

        /// <summary>Detach ammo from slingshot to cursor.</summary>
        private static bool HandleDetach(Slingshot slingshot)
        {
            if (Game1.player.CursorSlotItem != null)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log("Slingshot: Cannot detach - already holding item on cursor", LogLevel.Debug);
                Game1.playSound("cancel");
                return true;
            }

            SObject ammo = slingshot.attachments[0];
            if (ammo != null)
            {
                slingshot.attachments[0] = null;
                Game1.player.CursorSlotItem = ammo;
                InventoryManagementPatches.SetHoldingItem(true);
                Game1.playSound("button1");
                Monitor.Log($"Slingshot: Detached {ammo.Stack}x {ammo.Name} to cursor", LogLevel.Info);
                return true;
            }

            if (ModEntry.Config.VerboseLogging)
                Monitor.Log("Slingshot: No ammo to remove", LogLevel.Debug);
            return false;
        }

        /// <summary>Clear the selected ammo slot (called when leaving inventory menu).</summary>
        public static void ClearSelection()
        {
            SelectedAmmoSlot = -1;
        }
    }
}
