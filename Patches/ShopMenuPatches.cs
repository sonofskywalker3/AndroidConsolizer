using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Triggers;

namespace AndroidConsolizer.Patches
{
    /// <summary>Harmony patches for ShopMenu to fix controller purchasing.</summary>
    internal static class ShopMenuPatches
    {
        private static IMonitor Monitor;

        // Cache reflection fields for performance
        private static FieldInfo InvVisibleField;
        private static FieldInfo HoveredItemField;
        private static FieldInfo QuantityToBuyField;
        private static FieldInfo InventoryButtonField;


        /// <summary>Check if the active menu is a ShopMenu on the buy tab (right stick should be suppressed).</summary>
        internal static bool ShouldSuppressRightStick()
        {
            if (Game1.activeClickableMenu is ShopMenu shop && InvVisibleField != null)
            {
                bool inventoryVisible = (bool)InvVisibleField.GetValue(shop);
                return !inventoryVisible; // suppress on buy tab (inventoryVisible=false)
            }
            return false;
        }

        // Y button sell-one hold tracking
        private static bool _yHeldOnSellTab;
        private static int _yHoldTicks;
        private static Buttons _yHoldRawButton;
        private static Item _yHoldTargetItem;
        private const int SellHoldDelay = 20;   // ~333ms at 60fps before repeat starts
        private const int SellRepeatRate = 3;   // ~50ms at 60fps between repeats

        // LB/RB quantity hold tracking (for hold-to-repeat)
        private static bool _lbHeld;
        private static bool _rbHeld;
        private static int _lbHoldTicks;
        private static int _rbHoldTicks;
        private const int QuantityHoldDelay = 20;   // ~333ms at 60fps before repeat starts
        private const int QuantityRepeatRate = 3;   // ~50ms at 60fps between repeats

        // Right stick buy-tab navigation (jump 5 items)
        private static bool _rsDown;
        private static bool _rsUp;
        private static int _rsHoldTicks;
        private const int RStickHoldDelay = 15;    // ~250ms before repeat starts
        private const int RStickRepeatRate = 6;    // ~100ms between repeats
        private const float RStickThreshold = 0.5f;
        private const int RStickJumpCount = 5;

        // Left stick hold-to-repeat for menu navigation.
        // Game's directionKeyPolling decrements for all menus but only fires
        // receiveGamePadButton repeat for childMenu/textEntry, NOT activeClickableMenu.
        private static int _lsUpTicks, _lsDownTicks, _lsLeftTicks, _lsRightTicks;
        private const int StickNavDelay = 15;      // ~250ms initial delay (matches game's directionKeyPolling)
        private const int StickNavRepeatRate = 4;   // ~70ms between repeats (matches game's directionKeyPolling)
        private const float StickNavThreshold = 0.5f;

        // Sell tab highlight override — greys out items with sell price 0
        private static bool _highlightOverrideActive;
        private static ShopMenu _highlightOverrideShop;

        /// <summary>
        /// Enhanced highlight for sell tab: returns false for items that pass category
        /// check but have sell price 0 (e.g. Mixed Seeds). This greys them out AND
        /// blocks vanilla's receiveLeftClick sell path (which also checks highlight).
        /// </summary>
        private static bool HighlightItemToSellWithPriceCheck(Item i)
        {
            if (_highlightOverrideShop == null || !_highlightOverrideShop.highlightItemToSell(i))
                return false;

            // Item passes category check — now also verify it has a sell price
            if (i is StardewValley.Object obj)
                return obj.sellToStorePrice() > 0;

            int sp = i.salePrice();
            return sp > 0;
        }

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            // Cache reflection lookups
            InvVisibleField = AccessTools.Field(typeof(ShopMenu), "inventoryVisible");
            HoveredItemField = AccessTools.Field(typeof(ShopMenu), "hoveredItem");
            QuantityToBuyField = AccessTools.Field(typeof(ShopMenu), "quantityToBuy");
            InventoryButtonField = AccessTools.Field(typeof(ShopMenu), "inventoryButton");

            try
            {
                // PREFIX on receiveGamePadButton — must run BEFORE vanilla to read
                // hoveredItem before the game moves the selection on A press.
                // Returns false for A button to prevent vanilla from also handling it
                // (which causes the cursor to jump to a different item).
                harmony.Patch(
                    original: AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(ShopMenuPatches), nameof(ReceiveGamePadButton_Prefix))
                );

                // Patch update — postfix handles hold-to-repeat and right stick navigation
                harmony.Patch(
                    original: AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.update)),
                    postfix: new HarmonyMethod(typeof(ShopMenuPatches), nameof(Update_Postfix))
                );

                // Block touch inventoryButton (sell tab toggle) when controller is connected
                harmony.Patch(
                    original: AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.receiveLeftClick)),
                    prefix: new HarmonyMethod(typeof(ShopMenuPatches), nameof(ReceiveLeftClick_Prefix))
                );

                // Patch draw to show sell price tooltip on sell tab
                harmony.Patch(
                    original: AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.draw), new[] { typeof(SpriteBatch) }),
                    postfix: new HarmonyMethod(typeof(ShopMenuPatches), nameof(ShopMenu_Draw_Postfix))
                );

                Monitor.Log("ShopMenu patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply ShopMenu patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Prefix for receiveGamePadButton — handles A button purchase BEFORE vanilla code.
        /// Returns false for A button to skip vanilla handler (which moves selection).
        /// Returns true for all other buttons to let vanilla handle them normally.
        /// </summary>
        private static bool ReceiveGamePadButton_Prefix(ShopMenu __instance, Buttons b)
        {
            try
            {
                Buttons remapped = ButtonRemapper.Remap(b);

                // Only handle A and Y buttons — let everything else pass through
                if (remapped != Buttons.A && remapped != Buttons.Y)
                    return true;

                if (!ModEntry.Config?.EnableConsoleShops ?? true)
                    return true; // Disabled — let vanilla handle it

                // Y button — sell one item on sell tab (hold Y to sell repeatedly)
                if (remapped == Buttons.Y)
                {
                    if (InvVisibleField != null)
                    {
                        bool inventoryVisible = (bool)InvVisibleField.GetValue(__instance);
                        if (inventoryVisible)
                        {
                            Item sellItem = GetSellTabSelectedItem(__instance);
                            if (sellItem != null)
                            {
                                bool sold = SellOneItem(__instance, sellItem);
                                if (sold)
                                {
                                    _yHeldOnSellTab = true;
                                    _yHoldTicks = 0;
                                    _yHoldRawButton = b;
                                    _yHoldTargetItem = sellItem;
                                }
                                return false; // Block vanilla Y when cursor is on an item
                            }
                        }
                    }
                    return true; // Not on sell tab or no item — let vanilla handle Y (tab switching)
                }

                // Check sell mode via inventoryVisible field.
                // inventoryVisible=False on buy tab, True on sell tab.
                if (InvVisibleField != null)
                {
                    bool inventoryVisible = (bool)InvVisibleField.GetValue(__instance);
                    if (inventoryVisible)
                    {
                        // Sell tab — A button sells the full stack (console behavior)
                        // Use snap navigation to find selected item (hoveredItem is not set
                        // on the sell tab with controller — performHoverAction uses mouse pos)
                        Item sellItem = GetSellTabSelectedItem(__instance);
                        if (sellItem == null)
                        {
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log("Sell tab: no item at snapped slot, passing to vanilla", LogLevel.Trace);
                            return true;
                        }

                        // Storage shops (dresser, fish tank): onSell deposits the item into the
                        // container instead of selling for cash. Skipping this would destroy
                        // clothing and pay the player gold.
                        if (__instance.onSell != null)
                        {
                            if (TryDepositToStorageShop(__instance, sellItem))
                                return false;
                            // onSell returned false (rejected). Don't fall through to cash-sell —
                            // dressers don't sell items for money.
                            Game1.playSound("cancel");
                            return false;
                        }

                        // Check if this shop accepts the item (matches vanilla grayed-out logic)
                        if (!__instance.highlightItemToSell(sellItem))
                        {
                            Game1.playSound("cancel");
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log($"Sell tab: shop doesn't accept {sellItem.DisplayName} (category {sellItem.Category})", LogLevel.Debug);
                            return false;
                        }

                        int sellPrice;
                        if (sellItem is StardewValley.Object obj)
                            sellPrice = obj.sellToStorePrice();
                        else
                        {
                            int sp = sellItem.salePrice();
                            sellPrice = sp > 0 ? sp / 2 : -1;
                        }

                        if (sellPrice <= 0)
                        {
                            Game1.playSound("cancel");
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log($"Sell tab: {sellItem.DisplayName} cannot be sold (price={sellPrice})", LogLevel.Debug);
                            return false;
                        }

                        int stack = sellItem.Stack;
                        int totalPrice = sellPrice * stack;

                        // Credit player with gold (selling always gives gold regardless of shop currency)
                        Game1.player.Money += totalPrice;

                        // Remove item from player inventory
                        int idx = Game1.player.Items.IndexOf(sellItem);
                        if (idx >= 0)
                        {
                            Game1.player.Items[idx] = null;
                        }

                        // Clear hovered item to avoid stale reference
                        HoveredItemField?.SetValue(__instance, null);

                        Game1.playSound("purchaseClick");
                        Monitor.Log($"Sold {stack}x {sellItem.DisplayName} for {totalPrice}g ({sellPrice}g each)", LogLevel.Info);
                        return false;
                    }
                }

                // Read hoveredItem BEFORE vanilla code can change it
                ISalable selectedItem = HoveredItemField?.GetValue(__instance) as ISalable;

                if (selectedItem == null)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log("No hoveredItem — passing A to vanilla", LogLevel.Trace);
                    return true;
                }

                // Verify it's actually a for-sale item. If not, BLOCK vanilla — vanilla's
                // receiveGamePadButton accesses forSale[N] using a stale index when the
                // hovered item isn't in the price dict (happens with storage shops after
                // a deposit refresh, or when hoveredItem points at a removed forSale entry),
                // and crashes the update loop with ArgumentOutOfRangeException every tick.
                // ALSO refresh forSaleButtons + re-snap by ID so the cursor lands on a live
                // button — without this, the cursor sits on the dead reference and every
                // OTHER A-press hits this path and does nothing until the user nudges the
                // joystick (the v3.5.25 snap fix in the buy success path didn't cover this
                // missing-price path because we returned early before reaching it).
                if (!__instance.itemPriceAndStock.TryGetValue(selectedItem, out var priceAndStock))
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"No price info for {selectedItem.DisplayName} — refreshing snap", LogLevel.Trace);
                    Game1.playSound("cancel");
                    RebuildSaleButtonsAndRestoreSnap(__instance);
                    return false;
                }

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"Selected item: {selectedItem.DisplayName}", LogLevel.Debug);

                int unitPrice = priceAndStock.Price;
                int stock = priceAndStock.Stock;
                int playerMoney = ShopMenu.getPlayerCurrencyAmount(Game1.player, __instance.currency);
                string tradeItem = priceAndStock.TradeItem;
                int tradeItemCost = priceAndStock.TradeItemCount ?? 0;
                int totalTradeItems = 0;

                // Get the quantity to buy (set by LT/RT)
                int quantity = 1;
                if (QuantityToBuyField != null)
                {
                    quantity = Math.Max(1, (int)QuantityToBuyField.GetValue(__instance));
                }

                // Limit to available stock
                if (stock != int.MaxValue && stock > 0)
                {
                    quantity = Math.Min(quantity, stock);
                }

                int totalCost = unitPrice * quantity;

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"Item: {selectedItem.DisplayName}, Unit price: {unitPrice}, Quantity: {quantity}, Total: {totalCost}, Player has: {playerMoney}", LogLevel.Debug);

                // Limit to what player can afford
                if (playerMoney < totalCost)
                {
                    int affordableQty = unitPrice > 0 ? playerMoney / unitPrice : 0;
                    if (affordableQty <= 0)
                    {
                        Game1.playSound("cancel");
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log("Cannot afford any", LogLevel.Debug);
                        return false; // Block vanilla A handler
                    }
                    quantity = affordableQty;
                    totalCost = unitPrice * quantity;
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"Reduced quantity to {quantity} (affordable)", LogLevel.Debug);
                }

                // Limit quantity by available trade items (Desert Trader, tool upgrades, etc.)
                // Delegate to ShopMenu.HasTradeItem so other mods (e.g. ChestCheck) can
                // augment the check with items from farm chests or other sources.
                if (!string.IsNullOrEmpty(tradeItem) && tradeItemCost > 0)
                {
                    // Reduce quantity until we have enough trade items
                    while (quantity > 0 && !__instance.HasTradeItem(tradeItem, tradeItemCost * quantity))
                    {
                        quantity--;
                    }

                    if (quantity <= 0)
                    {
                        Game1.playSound("cancel");
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"Not enough trade items ({tradeItem}): need {tradeItemCost} per unit", LogLevel.Debug);
                        return false;
                    }

                    totalCost = unitPrice * quantity;
                    totalTradeItems = tradeItemCost * quantity;
                }

                // Check inventory space
                if (selectedItem is Item item && !Game1.player.couldInventoryAcceptThisItem(item))
                {
                    Game1.playSound("cancel");
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log("Inventory full", LogLevel.Debug);
                    return false; // Block vanilla A handler
                }

                // Deduct money
                ShopMenu.chargePlayer(Game1.player, __instance.currency, totalCost);

                // Consume trade items if required
                // Delegate to ShopMenu.ConsumeTradeItem so other mods (e.g. ChestCheck)
                // can pull from farm chests when inventory is insufficient.
                if (totalTradeItems > 0)
                {
                    __instance.ConsumeTradeItem(tradeItem, totalTradeItems);
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"Consumed {totalTradeItems}x {tradeItem}", LogLevel.Debug);
                }

                // Call actionWhenPurchased — handles recipes, tool upgrades, trash can upgrades, etc.
                string shopId = __instance.ShopId;
                bool handled = selectedItem.actionWhenPurchased(shopId);

                if (handled)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"actionWhenPurchased handled {selectedItem.DisplayName} (shopId={shopId})", LogLevel.Debug);
                }
                else if (selectedItem is Item purchaseItem)
                {
                    // Item wasn't handled by special logic — add to inventory normally
                    var newItem = purchaseItem.getOne();
                    newItem.Stack = quantity;
                    if (!Game1.player.addItemToInventoryBool(newItem))
                    {
                        // Inventory full — refund money and trade items
                        ShopMenu.chargePlayer(Game1.player, __instance.currency, -totalCost);
                        if (totalTradeItems > 0)
                        {
                            var refundItem = ItemRegistry.Create(tradeItem, totalTradeItems);
                            Game1.player.addItemToInventoryBool(refundItem);
                        }
                        Game1.playSound("cancel");
                        Monitor.Log($"Inventory full — refunded {totalCost} money" + (totalTradeItems > 0 ? $" and {totalTradeItems}x {tradeItem}" : ""), LogLevel.Warn);
                        return false;
                    }
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"Added {quantity}x {newItem.DisplayName} to inventory", LogLevel.Debug);
                }

                // Decrement stock the way vanilla does — HandleSynchedItemPurchase mutates the
                // shared ItemStockInformation in place and pushes the new value into
                // team.synchronizedShopStock so other farmhands see the depletion. Our previous
                // code rebuilt the entry with `new ItemStockInformation(price, remaining, ...)`,
                // which silently dropped SyncedKey, LimitedStockMode, ItemToSyncStack, and
                // ActionsOnPurchase — breaking multiplayer sync after the first purchase and
                // making 1.6+ shop-data action hooks fire only once before the entry was
                // overwritten. Mirror vanilla ShopMenu.cs:1685-1691.
                bool stockChanged = false;
                if (priceAndStock.Stock != int.MaxValue && !selectedItem.IsInfiniteStock())
                {
                    __instance.HandleSynchedItemPurchase(selectedItem, Game1.player, quantity);
                    stockChanged = true;

                    // Sync the linked display stack if the shop wires one (festival shops do this).
                    if (priceAndStock.ItemToSyncStack != null)
                        priceAndStock.ItemToSyncStack.Stack = priceAndStock.Stock;

                    // Drop depleted items from the for-sale list — preserves our pre-existing
                    // UX where sold-out items disappear from the shop instead of showing 0 stock.
                    if (priceAndStock.Stock <= 0)
                    {
                        __instance.forSale.Remove(selectedItem);
                        __instance.itemPriceAndStock.Remove(selectedItem);
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log("Removed depleted item from shop", LogLevel.Debug);
                    }
                    else if (ModEntry.Config.VerboseLogging)
                    {
                        Monitor.Log($"Stock decremented to {priceAndStock.Stock} via HandleSynchedItemPurchase", LogLevel.Debug);
                    }
                }

                // Refresh forSaleButtons so the cursor's snap component doesn't end up on a
                // dead button reference. Critical for storage shops (dresser, aquarium) where
                // each successful buy removes an item from forSale; without the rebuild, the
                // cursor still points at the now-orphaned button — A-presses find no hovered
                // item and "do nothing" until the user nudges the joystick to re-snap.
                if (stockChanged)
                    RebuildSaleButtonsAndRestoreSnap(__instance);

                // Run any 1.6+ shop-data ActionsOnPurchase hooks — used by content-pack /
                // festival shops to spawn NPCs, mark quests, etc. Vanilla ShopMenu.cs:1693-1702.
                if (priceAndStock.ActionsOnPurchase != null && priceAndStock.ActionsOnPurchase.Count > 0)
                {
                    foreach (string action in priceAndStock.ActionsOnPurchase)
                    {
                        if (!TriggerActionManager.TryRunAction(action, out var error, out var exception))
                            Monitor.Log($"Shop {shopId} ignored invalid action '{action}' on purchase of '{selectedItem.QualifiedItemId}': {error}", LogLevel.Warn);
                    }
                }

                // Invoke the shop's onPurchase callback if set. For storage shops (dresser,
                // fish tank/aquarium) this routes to onDresserItemWithdrawn, which removes the
                // original from heldItems — without it, the player gets a getOne() copy AND
                // the original stays in the storage, duplicating the item. Other shops use
                // this for side effects (e.g. spawning buildings); a true return asks vanilla
                // to close the menu, which we honour by exiting.
                if (__instance.onPurchase != null)
                {
                    try
                    {
                        bool shouldExit = __instance.onPurchase(selectedItem, Game1.player, quantity, priceAndStock);
                        if (shouldExit)
                        {
                            __instance.exitThisMenu();
                            return false;
                        }
                    }
                    catch (Exception cbEx)
                    {
                        Monitor.Log($"ShopMenu onPurchase callback threw: {cbEx.Message}", LogLevel.Warn);
                    }
                }

                // Reset quantity selector to 1 after purchase
                if (QuantityToBuyField != null)
                {
                    QuantityToBuyField.SetValue(__instance, 1);
                }

                Game1.playSound("purchaseClick");
                Monitor.Log($"Purchase complete! Bought {quantity}x {selectedItem.DisplayName}", LogLevel.Info);

                return false; // Skip vanilla A handler — prevents cursor jump
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error in shop purchase prefix: {ex.Message}", LogLevel.Error);
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log(ex.StackTrace, LogLevel.Debug);
                return true; // On error, let vanilla handle it
            }
        }

        /// <summary>
        /// Get the inventory item at the currently snapped component on the sell tab.
        /// On Android, hoveredItem is not set for controller users on the sell tab because
        /// performHoverAction uses mouse position which doesn't track snap navigation.
        /// Instead, we find the snapped component in the inventory slot list directly.
        /// </summary>
        private static Item GetSellTabSelectedItem(ShopMenu shop)
        {
            var snapped = shop.currentlySnappedComponent;
            if (snapped == null) return null;

            int slotIndex = shop.inventory.inventory.IndexOf(snapped);
            if (slotIndex < 0 || slotIndex >= shop.inventory.actualInventory.Count) return null;

            return shop.inventory.actualInventory[slotIndex];
        }

        /// <summary>
        /// Rebuild the for-sale button list (refresh display after stock/heldItems change)
        /// and restore the cursor snap by component ID. forSaleButtons IDs are position-
        /// based, so re-snapping by the previous ID lands the cursor on whichever item is
        /// now at that position (or first valid button if the previous ID is gone).
        /// rebuildSaleButtons is private on Android — accessed via reflection.
        /// </summary>
        private static void RebuildSaleButtonsAndRestoreSnap(ShopMenu shop)
        {
            try
            {
                int prevSnapId = shop.currentlySnappedComponent?.myID ?? -1;
                AccessTools.Method(typeof(ShopMenu), "rebuildSaleButtons")?.Invoke(shop, null);
                if (prevSnapId == -1) return;

                ClickableComponent newSnap = shop.getComponentWithID(prevSnapId);
                if (newSnap == null && shop.forSaleButtons != null && shop.forSaleButtons.Count > 0)
                    newSnap = shop.forSaleButtons[0];
                if (newSnap == null) return;

                shop.currentlySnappedComponent = newSnap;
                shop.snapCursorToCurrentSnappedComponent();

                // Refresh hoveredItem (the field our buy code reads to know what's under
                // the cursor). vanilla sets it via performHoverAction at ShopMenu.cs:1422,
                // matching the button at (x,y) to forSale[currentItemIndex + i]. Without
                // this call, hoveredItem still references the just-removed item, so the
                // next A-press hits the missing-price branch and cancels — visible to
                // user as "first A picks up but second A on the highlighted item does
                // nothing." (Reported on v3.5.26.)
                shop.performHoverAction(newSnap.bounds.Center.X, newSnap.bounds.Center.Y);
            }
            catch { /* best-effort — older builds may not expose rebuildSaleButtons */ }
        }

        /// <summary>
        /// Deposit an item into a storage shop (dresser, fish tank) by invoking onSell.
        /// Returns true if the item was deposited and removed from inventory.
        /// </summary>
        private static bool TryDepositToStorageShop(ShopMenu shop, Item sellItem)
        {
            try
            {
                if (shop.onSell == null) return false;

                // Respect vanilla's per-shop deposit rules. Dresser's highlightItemToSell
                // returns true only for clothing/hat/boots/ring (categoriesToSellHere
                // populated for Dresser context); aquarium's returns false for everything
                // (deposits via held-fish + world A-press only, NOT through the shop UI).
                // Without this check we'd accept anything into either shop, which v3.5.11
                // - v3.5.25 silently did. User confirmed the vanilla behaviour via PC.
                if (!shop.highlightItemToSell(sellItem))
                {
                    Game1.playSound("cancel");
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"Storage shop rejects {sellItem.DisplayName} (highlightItemToSell=false)", LogLevel.Debug);
                    return true; // we handled it (with a cancel) — don't fall through to cash-sell
                }

                bool deposited = shop.onSell(sellItem);
                if (!deposited) return false;

                int idx = Game1.player.Items.IndexOf(sellItem);
                if (idx >= 0)
                    Game1.player.Items[idx] = null;

                HoveredItemField?.SetValue(shop, null);

                // Refresh the menu so the just-deposited item shows in the buy tab,
                // and re-snap the cursor by ID so navigation doesn't end up on a dead
                // button reference. Vanilla calls rebuildSaleButtons after onSell at
                // ShopMenu.cs:857 (and switchTab(0) which re-snaps as a side effect).
                RebuildSaleButtonsAndRestoreSnap(shop);

                Game1.playSound("dwop");
                Monitor.Log($"Deposited {sellItem.DisplayName} into storage shop", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                Monitor.Log($"TryDepositToStorageShop error: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>Sell one unit of the given item. Returns true if sold successfully.</summary>
        private static bool SellOneItem(ShopMenu shop, Item sellItem)
        {
            // Storage shops (dresser, fish tank): deposit instead of cash-sell.
            // Y on a single piece of clothing is the same as A — there's no "sell one of a stack"
            // for dressers because clothing rarely stacks.
            if (shop.onSell != null)
            {
                if (TryDepositToStorageShop(shop, sellItem))
                    return true;
                Game1.playSound("cancel");
                return false;
            }

            // Check if this shop accepts the item (matches vanilla grayed-out logic)
            if (!shop.highlightItemToSell(sellItem))
            {
                Game1.playSound("cancel");
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"Sell tab: shop doesn't accept {sellItem.DisplayName} (category {sellItem.Category})", LogLevel.Debug);
                return false;
            }

            int sellPrice;
            if (sellItem is StardewValley.Object obj)
                sellPrice = obj.sellToStorePrice();
            else
            {
                int sp = sellItem.salePrice();
                sellPrice = sp > 0 ? sp / 2 : -1;
            }

            if (sellPrice <= 0)
            {
                Game1.playSound("cancel");
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"Sell tab: {sellItem.DisplayName} cannot be sold (price={sellPrice})", LogLevel.Debug);
                return false;
            }

            Game1.player.Money += sellPrice;

            if (sellItem.Stack > 1)
            {
                sellItem.Stack -= 1;
                Monitor.Log($"Sold 1x {sellItem.DisplayName} for {sellPrice}g ({sellItem.Stack} remaining)", LogLevel.Info);
            }
            else
            {
                // Last item in stack — remove from inventory
                int idx = Game1.player.Items.IndexOf(sellItem);
                if (idx >= 0)
                    Game1.player.Items[idx] = null;
                HoveredItemField?.SetValue(shop, null);
                Monitor.Log($"Sold last {sellItem.DisplayName} for {sellPrice}g", LogLevel.Info);
            }

            Game1.playSound("purchaseClick");
            return true;
        }

        /// <summary>
        /// Adjust the buy quantity by the given delta, respecting all limits.
        /// Called from ModEntry for initial LB/RB press, and from Update_Postfix for hold-to-repeat.
        /// </summary>
        public static void AdjustQuantity(ShopMenu shop, int delta)
        {
            try
            {
                if (QuantityToBuyField == null || InvVisibleField == null)
                    return;

                // Don't adjust quantity when on sell tab
                if ((bool)InvVisibleField.GetValue(shop))
                    return;

                int currentQuantity = (int)QuantityToBuyField.GetValue(shop);

                // Calculate max quantity based on selected item
                int maxQuantity = GetMaxBuyQuantity(shop);

                int newQuantity = Math.Max(1, Math.Min(maxQuantity, currentQuantity + delta));
                if (newQuantity != currentQuantity)
                {
                    QuantityToBuyField.SetValue(shop, newQuantity);
                    Game1.playSound("smallSelect");
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"Shop quantity: {currentQuantity} -> {newQuantity} (max: {maxQuantity})", LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error adjusting shop quantity: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Get the maximum quantity that can be purchased for the currently selected item.
        /// Considers: stock, player money, trade items, and item stack size.
        /// </summary>
        private static int GetMaxBuyQuantity(ShopMenu shop)
        {
            try
            {
                // Find the currently selected item via hoveredItem
                ISalable selectedItem = HoveredItemField?.GetValue(shop) as ISalable;

                if (selectedItem == null || !shop.itemPriceAndStock.TryGetValue(selectedItem, out var priceAndStock))
                    return 999; // Default max if we can't determine

                int unitPrice = priceAndStock.Price;
                int stock = priceAndStock.Stock;
                int playerMoney = ShopMenu.getPlayerCurrencyAmount(Game1.player, shop.currency);
                string tradeItem = priceAndStock.TradeItem;
                int tradeItemCost = priceAndStock.TradeItemCount ?? 0;

                // Max limited by stock
                int maxByStock = stock == int.MaxValue ? 999 : stock;

                // Max limited by money
                int maxByMoney = unitPrice > 0 ? playerMoney / unitPrice : 999;

                // Max limited by trade items (Desert Trader, etc.)
                int maxByTradeItems = 999;
                if (!string.IsNullOrEmpty(tradeItem) && tradeItemCost > 0)
                {
                    int playerTradeItems = 0;
                    foreach (Item invItem in Game1.player.Items)
                    {
                        if (invItem != null && (invItem.QualifiedItemId == tradeItem || invItem.ItemId == tradeItem))
                            playerTradeItems += invItem.Stack;
                    }
                    maxByTradeItems = playerTradeItems / tradeItemCost;
                }

                // Max limited by item stack size
                int maxByStackSize = selectedItem.maximumStackSize();
                if (maxByStackSize <= 0) maxByStackSize = 999;

                return Math.Max(1, Math.Min(Math.Min(Math.Min(maxByStock, maxByMoney), maxByTradeItems), maxByStackSize));
            }
            catch
            {
                return 999; // Default max on error
            }
        }

        /// <summary>
        /// Jump the shop buy list selection by N items using simulated DPad presses.
        /// Positive count = down, negative = up.
        /// </summary>
        private static void JumpShopSelection(ShopMenu shop, int count)
        {
            try
            {
                Buttons btn = count > 0 ? Buttons.DPadDown : Buttons.DPadUp;
                int steps = Math.Abs(count);

                // Mute during loop so 5 nav sounds don't stack, play one at the end
                float savedVolume = Game1.options.soundVolumeLevel;
                Game1.options.soundVolumeLevel = 0f;
                for (int i = 0; i < steps; i++)
                {
                    shop.receiveGamePadButton(btn);
                }
                Game1.options.soundVolumeLevel = savedVolume;
                Game1.playSound("shiny4");

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"Right stick: jumped {count} items", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error in JumpShopSelection: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Start tracking LB hold for repeat quantity adjustment.</summary>
        public static void StartLBHold()
        {
            _lbHeld = true;
            _lbHoldTicks = 0;
        }

        /// <summary>Start tracking RB hold for repeat quantity adjustment.</summary>
        public static void StartRBHold()
        {
            _rbHeld = true;
            _rbHoldTicks = 0;
        }

        /// <summary>Postfix for update — hold-to-repeat, right stick navigation, quantity reset.</summary>
        private static void Update_Postfix(ShopMenu __instance, GameTime time)
        {
            // Get gamepad state once for all hold-to-repeat checks
            var gpState = GamePad.GetState(PlayerIndex.One);

            // Y button sell hold-to-repeat
            if (_yHeldOnSellTab)
            {
                bool yStillHeld = gpState.IsButtonDown(_yHoldRawButton);
                bool stillOnSellTab = InvVisibleField != null && (bool)InvVisibleField.GetValue(__instance);

                if (yStillHeld && stillOnSellTab)
                {
                    _yHoldTicks++;

                    if (_yHoldTicks > SellHoldDelay &&
                        (_yHoldTicks - SellHoldDelay) % SellRepeatRate == 0)
                    {
                        Item sellItem = GetSellTabSelectedItem(__instance);
                        if (sellItem != null && sellItem == _yHoldTargetItem)
                        {
                            SellOneItem(__instance, sellItem);
                        }
                        else
                        {
                            // Item gone or changed — stop repeating
                            _yHeldOnSellTab = false;
                        }
                    }
                }
                else
                {
                    // Y released or left sell tab — stop repeating
                    _yHeldOnSellTab = false;
                    _yHoldTicks = 0;
                    _yHoldTargetItem = null;
                }
            }

            // LB/RB quantity hold-to-repeat
            bool onBuyTab = InvVisibleField == null || !(bool)InvVisibleField.GetValue(__instance);

            if (_lbHeld)
            {
                bool lbStillHeld = gpState.IsButtonDown(Buttons.LeftShoulder);
                if (lbStillHeld && onBuyTab)
                {
                    _lbHoldTicks++;
                    if (_lbHoldTicks > QuantityHoldDelay &&
                        (_lbHoldTicks - QuantityHoldDelay) % QuantityRepeatRate == 0)
                    {
                        // In bumper mode: -1, in non-bumper mode: -10
                        int delta = ModEntry.Config.UseBumpersInsteadOfTriggers ? -1 : -10;
                        AdjustQuantity(__instance, delta);
                    }
                }
                else
                {
                    _lbHeld = false;
                    _lbHoldTicks = 0;
                }
            }

            if (_rbHeld)
            {
                bool rbStillHeld = gpState.IsButtonDown(Buttons.RightShoulder);
                if (rbStillHeld && onBuyTab)
                {
                    _rbHoldTicks++;
                    if (_rbHoldTicks > QuantityHoldDelay &&
                        (_rbHoldTicks - QuantityHoldDelay) % QuantityRepeatRate == 0)
                    {
                        // In bumper mode: +1, in non-bumper mode: +10
                        int delta = ModEntry.Config.UseBumpersInsteadOfTriggers ? 1 : 10;
                        AdjustQuantity(__instance, delta);
                    }
                }
                else
                {
                    _rbHeld = false;
                    _rbHoldTicks = 0;
                }
            }

            // Right stick navigation — jump 5 items at a time on buy tab.
            // Vanilla right stick scroll is blocked at the source via SuppressRightStick flag
            // in GameplayButtonPatches.GetState_Postfix (zeroes out right thumbstick for vanilla).
            // We read the REAL right stick from the raw hardware state cached during GetState_Postfix,
            // then simulate DPad presses which move both selection AND view together.
            if (onBuyTab)
            {
                float rsY = GameplayButtonPatches.RawRightStickY;
                bool stickDown = rsY < -RStickThreshold;
                bool stickUp = rsY > RStickThreshold;

                if (stickDown || stickUp)
                {
                    bool directionChanged = (stickDown && !_rsDown) || (stickUp && !_rsUp);
                    bool justStarted = directionChanged || (!_rsDown && !_rsUp);

                    _rsDown = stickDown;
                    _rsUp = stickUp;

                    if (justStarted)
                    {
                        _rsHoldTicks = 0;
                        JumpShopSelection(__instance, stickDown ? RStickJumpCount : -RStickJumpCount);
                    }
                    else
                    {
                        _rsHoldTicks++;
                        if (_rsHoldTicks > RStickHoldDelay &&
                            (_rsHoldTicks - RStickHoldDelay) % RStickRepeatRate == 0)
                        {
                            JumpShopSelection(__instance, stickDown ? RStickJumpCount : -RStickJumpCount);
                        }
                    }
                }
                else
                {
                    _rsDown = false;
                    _rsUp = false;
                    _rsHoldTicks = 0;
                }
            }

            // Reset buy quantity to 1 while on sell tab — prevents vanilla trigger input
            // from modifying quantityToBuy while the sell tab is active
            if (InvVisibleField != null && QuantityToBuyField != null)
            {
                bool inventoryVisible = (bool)InvVisibleField.GetValue(__instance);
                if (inventoryVisible)
                {
                    int qty = (int)QuantityToBuyField.GetValue(__instance);
                    if (qty != 1)
                    {
                        QuantityToBuyField.SetValue(__instance, 1);
                    }
                }
            }

            // Left stick hold-to-repeat — fire receiveGamePadButton at repeat rate
            // while stick is held in a direction. SMAPI only fires on state transitions.
            float lsX = gpState.ThumbSticks.Left.X;
            float lsY = gpState.ThumbSticks.Left.Y;

            if (lsY > StickNavThreshold)
            {
                _lsUpTicks++;
                if (_lsUpTicks > StickNavDelay && (_lsUpTicks - StickNavDelay) % StickNavRepeatRate == 0)
                    __instance.receiveGamePadButton(Buttons.LeftThumbstickUp);
            }
            else
                _lsUpTicks = 0;

            if (lsY < -StickNavThreshold)
            {
                _lsDownTicks++;
                if (_lsDownTicks > StickNavDelay && (_lsDownTicks - StickNavDelay) % StickNavRepeatRate == 0)
                    __instance.receiveGamePadButton(Buttons.LeftThumbstickDown);
            }
            else
                _lsDownTicks = 0;

            if (lsX < -StickNavThreshold)
            {
                _lsLeftTicks++;
                if (_lsLeftTicks > StickNavDelay && (_lsLeftTicks - StickNavDelay) % StickNavRepeatRate == 0)
                    __instance.receiveGamePadButton(Buttons.LeftThumbstickLeft);
            }
            else
                _lsLeftTicks = 0;

            if (lsX > StickNavThreshold)
            {
                _lsRightTicks++;
                if (_lsRightTicks > StickNavDelay && (_lsRightTicks - StickNavDelay) % StickNavRepeatRate == 0)
                    __instance.receiveGamePadButton(Buttons.LeftThumbstickRight);
            }
            else
                _lsRightTicks = 0;

            // Sell-tab highlight selection.
            //   - Cash-sell shop sell tab: install HighlightItemToSellWithPriceCheck so
            //     0-price items (e.g. Mixed Seeds) grey out and vanilla's receiveLeftClick
            //     sell path also rejects them.
            //   - Storage shop (dresser, fish tank) sell tab: use vanilla's own
            //     `__instance.highlightItemToSell`, which checks categoriesToSellHere
            //     (ShopMenu.cs:793). Dresser populates that with clothing/hat/boots/ring
            //     categories; aquarium leaves it empty. So in vanilla:
            //       Dresser sell tab — only clothing-type items are clickable
            //       Aquarium sell tab — everything is greyed (deposits go via held-fish + A
            //                           on the placed tank in the world, not the shop UI)
            //     v3.5.23 forced highlightAllItems for storage shops, which incorrectly
            //     allowed any item to be deposited. v3.5.26 reverts to vanilla's restrictions.
            //   - Buy tab: revert to vanilla highlightItemToSell (the shop's default).
            if (InvVisibleField != null)
            {
                bool onSellTab = (bool)InvVisibleField.GetValue(__instance);
                bool isStorageShop = __instance.onSell != null;
                InventoryMenu.highlightThisItem desired;
                if (onSellTab && !isStorageShop)
                    desired = HighlightItemToSellWithPriceCheck;
                else
                    desired = __instance.highlightItemToSell;

                if (__instance.inventory.highlightMethod != desired)
                {
                    __instance.inventory.highlightMethod = desired;
                    _highlightOverrideShop = __instance;
                    _highlightOverrideActive = (desired != __instance.highlightItemToSell);
                }
            }

        }

        /// <summary>
        /// Draw a circle outline using Bresenham's midpoint circle algorithm.
        /// </summary>
        private static void DrawCircleOutline(SpriteBatch b, int cx, int cy, int radius, Color color, int thickness = 2)
        {
            int x = radius;
            int y = 0;
            int d = 1 - x;

            while (y <= x)
            {
                b.Draw(Game1.staminaRect, new Rectangle(cx + x, cy + y, thickness, thickness), color);
                b.Draw(Game1.staminaRect, new Rectangle(cx - x, cy + y, thickness, thickness), color);
                b.Draw(Game1.staminaRect, new Rectangle(cx + x, cy - y, thickness, thickness), color);
                b.Draw(Game1.staminaRect, new Rectangle(cx - x, cy - y, thickness, thickness), color);
                b.Draw(Game1.staminaRect, new Rectangle(cx + y, cy + x, thickness, thickness), color);
                b.Draw(Game1.staminaRect, new Rectangle(cx - y, cy + x, thickness, thickness), color);
                b.Draw(Game1.staminaRect, new Rectangle(cx + y, cy - x, thickness, thickness), color);
                b.Draw(Game1.staminaRect, new Rectangle(cx - y, cy - x, thickness, thickness), color);

                y++;
                if (d <= 0)
                    d += 2 * y + 1;
                else
                {
                    x--;
                    d += 2 * (y - x) + 1;
                }
            }
        }

        /// <summary>
        /// Draw postfix — shows controller tab-switch button hint on the inventoryButton,
        /// and sell price tooltip when hovering items on the sell tab.
        /// </summary>
        private static void ShopMenu_Draw_Postfix(ShopMenu __instance, SpriteBatch b)
        {
            try
            {
                if (!ModEntry.Config?.EnableConsoleShops ?? true)
                    return;

                // Draw controller button icon on the inventoryButton (tab-switch hint)
                // Positioned on the left side (between backpack icon and "Inventory" text)
                // to avoid overlapping vanilla's "Owned: X" display on the right side.
                // Dimmed when on sell tab to match the grayed-out button appearance.
                if (GamePad.GetState(PlayerIndex.One).IsConnected && InventoryButtonField != null)
                {
                    var invButton = InventoryButtonField.GetValue(__instance) as ClickableComponent;
                    if (invButton != null && invButton.bounds.Width > 0)
                    {
                        const int iconRadius = 18;
                        const int iconThickness = 3;
                        const int iconXFromLeft = 75; // right of backpack icon, left of text

                        int cx = invButton.bounds.Left + iconXFromLeft;
                        int cy = invButton.bounds.Center.Y;

                        // Dim icon when on sell tab (button is grayed out)
                        bool onSellTab = InvVisibleField != null && (bool)InvVisibleField.GetValue(__instance);
                        Color iconColor = onSellTab ? Game1.textColor * 0.4f : Game1.textColor;

                        DrawCircleOutline(b, cx, cy, iconRadius, iconColor, iconThickness);

                        var layout = ModEntry.Config?.ControllerLayout ?? ControllerLayout.Switch;
                        if (layout == ControllerLayout.PlayStation)
                        {
                            // Outlined square centered in the circle, thickness matches circle
                            int sqSize = iconRadius;
                            int sqX = cx - sqSize / 2 + 2;
                            int sqY = cy - sqSize / 2 + 2;
                            b.Draw(Game1.staminaRect, new Rectangle(sqX, sqY, sqSize, iconThickness), iconColor);
                            b.Draw(Game1.staminaRect, new Rectangle(sqX, sqY + sqSize - iconThickness, sqSize, iconThickness), iconColor);
                            b.Draw(Game1.staminaRect, new Rectangle(sqX, sqY, iconThickness, sqSize), iconColor);
                            b.Draw(Game1.staminaRect, new Rectangle(sqX + sqSize - iconThickness, sqY, iconThickness, sqSize), iconColor);
                        }
                        else
                        {
                            // Letter icon: Y for Switch, X for Xbox
                            string letter = layout == ControllerLayout.Xbox ? "X" : "Y";
                            const int iconYOffset = 5;
                            const int iconXOffset = 3;

                            Vector2 letterSize = Game1.smallFont.MeasureString(letter);
                            float tx = cx - letterSize.X / 2 + iconXOffset;
                            float ty = cy - letterSize.Y / 2 + iconYOffset;

                            // Faux-bold: draw at multiple 1px offsets for thicker appearance
                            // Use DrawString directly instead of Utility.drawTextWithShadow —
                            // drawTextWithShadow ignores the alpha/color multiplication,
                            // causing the letter to turn yellowish-orange instead of dimming on sell tab.
                            b.DrawString(Game1.smallFont, letter, new Vector2(tx, ty), iconColor);
                            b.DrawString(Game1.smallFont, letter, new Vector2(tx + 1, ty), iconColor);
                            b.DrawString(Game1.smallFont, letter, new Vector2(tx, ty + 1), iconColor);
                        }
                    }
                }

                // Draw cursor when vanilla's drawMouse() doesn't:
                // - Buy tab: drawMouse() skipped entirely when SnappyMenus && !inventoryVisible
                // - Sell tab at Blacksmith/Joja: drawMouse() called but mouseCursorTransparency=0
                if (GamePad.GetState(PlayerIndex.One).IsConnected && InvVisibleField != null)
                {
                    bool invVisible = (bool)InvVisibleField.GetValue(__instance);
                    bool needCursor = (!invVisible && Game1.options.snappyMenus)
                                   || (invVisible && Game1.mouseCursorTransparency < 0.01f);
                    if (needCursor)
                    {
                        int cursorX = Game1.getMouseX();
                        int cursorY = Game1.getMouseY();

                        // On buy tab, getMouseX/Y() and currentlySnappedComponent are both
                        // unreliable (stale from sell tab, or null). Find the forSaleButton
                        // that matches hoveredItem instead — hoveredItem IS reliably set
                        // because our purchase code depends on it.
                        if (!invVisible)
                        {
                            bool found = false;
                            var hovItem = HoveredItemField?.GetValue(__instance) as ISalable;
                            if (hovItem != null)
                            {
                                for (int i = 0; i < __instance.forSaleButtons.Count; i++)
                                {
                                    int itemIdx = __instance.currentItemIndex + i;
                                    if (itemIdx < __instance.forSale.Count && __instance.forSale[itemIdx] == hovItem)
                                    {
                                        var btn = __instance.forSaleButtons[i];
                                        cursorX = btn.bounds.Right - btn.bounds.Width / 4;
                                        cursorY = btn.bounds.Bottom - btn.bounds.Height / 4;
                                        found = true;
                                        break;
                                    }
                                }
                            }
                            if (!found && __instance.forSaleButtons.Count > 0)
                            {
                                var btn = __instance.forSaleButtons[0];
                                cursorX = btn.bounds.Right - btn.bounds.Width / 4;
                                cursorY = btn.bounds.Bottom - btn.bounds.Height / 4;
                            }
                        }

                        int cursorTile = Game1.options.snappyMenus ? 44 : Game1.mouseCursor;
                        b.Draw(
                            Game1.mouseCursors,
                            new Vector2(cursorX, cursorY),
                            Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, cursorTile, 16, 16),
                            Color.White,
                            0f,
                            Vector2.Zero,
                            4f + Game1.dialogueButtonScale / 150f,
                            SpriteEffects.None,
                            1f
                        );
                    }
                }

                // --- Sell tab tooltip (below here is sell-tab only) ---
                if (InvVisibleField == null)
                    return;

                bool inventoryVisible = (bool)InvVisibleField.GetValue(__instance);
                if (!inventoryVisible)
                    return;

                Item sellItem = GetSellTabSelectedItem(__instance);
                if (sellItem == null)
                    return;

                // Don't show tooltip for items this shop doesn't accept (grayed-out items)
                if (!__instance.highlightItemToSell(sellItem))
                    return;

                // Compute sell price — same logic as the sell code in ReceiveGamePadButton_Prefix.
                // Object items use sellToStorePrice(), everything else (weapons, rings, boots) uses salePrice()/2.
                int sellPrice;
                if (sellItem is StardewValley.Object obj)
                    sellPrice = obj.sellToStorePrice();
                else
                {
                    int sp = sellItem.salePrice();
                    sellPrice = sp > 0 ? sp / 2 : 0;
                }

                if (sellPrice <= 0)
                    return;

                // Build compact sell price text
                string priceText;
                int total = sellPrice * sellItem.Stack;
                if (sellItem.Stack > 1)
                    priceText = $" {total}g ({sellPrice}g each)";
                else
                    priceText = $" {sellPrice}g";

                // Manually position a small tooltip box near the selected inventory slot
                // (drawToolTip/drawHoverText position at mouse cursor which is wrong on sell tab)
                var snapped = __instance.currentlySnappedComponent;
                if (snapped == null)
                    return;

                // Gold coin sprite: Game1.mouseCursors at (193, 373, 9, 10), drawn at 4x
                int coinScale = 4;
                int coinW = 9 * coinScale;  // 36px
                int coinH = 10 * coinScale; // 40px

                Vector2 textSize = Game1.smallFont.MeasureString(priceText);
                int contentW = coinW + (int)textSize.X;
                int contentH = Math.Max(coinH, (int)textSize.Y);
                int pad = 20;
                int boxW = contentW + pad * 2;
                int boxH = contentH + pad * 2;

                // Position to the right of the selected slot
                int boxX = snapped.bounds.Right + 8;
                int boxY = snapped.bounds.Center.Y - boxH / 2;

                // Keep on screen
                if (boxX + boxW > Game1.uiViewport.Width)
                    boxX = snapped.bounds.Left - boxW - 8;
                if (boxY < 0)
                    boxY = 0;
                if (boxY + boxH > Game1.uiViewport.Height)
                    boxY = Game1.uiViewport.Height - boxH;

                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    boxX, boxY, boxW, boxH, Color.White);

                // Draw gold coin icon
                int innerX = boxX + pad;
                int innerY = boxY + pad;
                b.Draw(Game1.mouseCursors,
                    new Vector2(innerX, innerY + (contentH - coinH) / 2),
                    new Rectangle(193, 373, 9, 10),
                    Color.White, 0f, Vector2.Zero, coinScale, SpriteEffects.None, 1f);

                // Draw price text to the right of the coin
                Utility.drawTextWithShadow(b, priceText, Game1.smallFont,
                    new Vector2(innerX + coinW, innerY + (contentH - (int)textSize.Y) / 2),
                    Game1.textColor);
            }
            catch
            {
                // Silently ignore tooltip draw errors — never crash the draw loop
            }
        }

        /// <summary>
        /// Prefix for receiveLeftClick — blocks the touch "inventoryButton" (sell tab toggle)
        /// when a controller is connected. Tab switching should only happen via controller button.
        /// </summary>
        private static bool ReceiveLeftClick_Prefix(ShopMenu __instance, int x, int y)
        {
            try
            {
                if (!ModEntry.Config?.EnableConsoleShops ?? true)
                    return true;

                if (!GamePad.GetState(PlayerIndex.One).IsConnected)
                    return true;

                if (InventoryButtonField == null)
                    return true;

                var invButton = InventoryButtonField.GetValue(__instance) as ClickableComponent;
                if (invButton != null && invButton.containsPoint(x, y))
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log("Blocked touch inventoryButton tap (controller connected)", LogLevel.Debug);
                    return false; // Block the click — don't let vanilla toggle inventoryVisible
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error in receiveLeftClick prefix: {ex.Message}", LogLevel.Error);
            }

            return true;
        }
    }
}
