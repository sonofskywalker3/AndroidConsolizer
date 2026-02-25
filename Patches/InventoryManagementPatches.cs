using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.Tools;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Handles console-style inventory management via controller.
    /// Makes the A button work like Nintendo Switch:
    /// - A on item = pick up to cursor (visually attached, draggable)
    /// - A on another slot = swap/place item
    /// - A on empty slot = place item
    ///
    /// Y button for single-stack pickup:
    /// - Y on stack = pick up 1 item from stack
    /// - Hold Y = continue picking up 1 at a time
    /// </summary>
    internal static class InventoryManagementPatches
    {
        private static IMonitor Monitor;

        // Track whether we're currently "holding" an item
        private static bool IsHoldingItem = false;

        // The source slot we picked up from (for visual feedback)
        private static int SourceSlotId = -1;

        // For Y button hold detection
        private static bool IsYButtonHeld = false;
        private static int YButtonHoldTicks = 0;
        private const int YButtonHoldDelay = 15; // Ticks before repeat (about 250ms at 60fps)
        private const int YButtonRepeatRate = 8;  // Ticks between repeats (about 133ms at 60fps)

        // For controller hover tooltip
        private static int LastSnappedComponentId = -1;

        // Track previous Y button state for edge detection
        private static bool WasYButtonDown = false;

        // Track the slot we're picking from with Y button hold
        private static int YButtonHoldSlotId = -1;

        // Track previous A button state for blocking hold behavior
        private static bool WasAButtonDown = false;

        // Cached A-button state from OnUpdateTicked — avoids redundant GamePad.GetState() in draw postfix
        private static bool CachedAButtonDown = false;

        // Drop zone component ID (below trash 105, bottom-right of inventory page)
        private const int DropZoneId = 110;

        // When HandleAButton declines to process a non-inventory slot (equipment, sort, trash),
        // this flag tells the prefix patches in InventoryPagePatches to let the A press through
        // to the game's own handler. Cleared each tick in OnUpdateTicked.
        public static bool AllowGameAPress = false;

        // Cached reflection fields for hover/tooltip (avoids per-tick AccessTools.Field lookups)
        private static FieldInfo InvPage_HoverTextField;
        private static FieldInfo InvPage_HoverTitleField;
        private static FieldInfo InvPage_HoveredItemField;
        private static FieldInfo InvMenu_HoveredItemField;

        // For InventoryMenu.draw finger cursor — save/restore currentlySelectedItem
        // currentlySelectedItem is Android-only (not in PC DLL), must use reflection
        private static FieldInfo InvMenu_CurrentlySelectedItemField;
        private static int _savedSelectedItem = -1;

        // For drop zone positioning — bottomBoxY marks the top of the bottom half of the inventory page
        private static FieldInfo InvPage_BottomBoxYField;

        /// <summary>Apply Harmony patches for cursor item rendering.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            // Cache reflection lookups for hover/tooltip fields
            InvPage_HoverTextField = AccessTools.Field(typeof(InventoryPage), "hoverText");
            InvPage_HoverTitleField = AccessTools.Field(typeof(InventoryPage), "hoverTitle");
            InvPage_HoveredItemField = AccessTools.Field(typeof(InventoryPage), "hoveredItem");
            InvMenu_HoveredItemField = AccessTools.Field(typeof(InventoryMenu), "hoveredItem");
            InvMenu_CurrentlySelectedItemField = AccessTools.Field(typeof(InventoryMenu), "currentlySelectedItem");
            InvPage_BottomBoxYField = AccessTools.Field(typeof(InventoryPage), "bottomBoxY");

            try
            {
                // Patch InventoryPage.draw to render our held item on cursor
                harmony.Patch(
                    original: AccessTools.Method(typeof(InventoryPage), nameof(InventoryPage.draw), new[] { typeof(SpriteBatch) }),
                    postfix: new HarmonyMethod(typeof(InventoryManagementPatches), nameof(InventoryPage_Draw_Postfix))
                );

                // Patch InventoryMenu.draw to replace red selection box (tile 56) with finger cursor
                harmony.Patch(
                    original: AccessTools.Method(typeof(InventoryMenu), nameof(InventoryMenu.draw),
                        new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(int) }),
                    prefix: new HarmonyMethod(typeof(InventoryManagementPatches), nameof(InventoryMenu_Draw_Prefix)),
                    postfix: new HarmonyMethod(typeof(InventoryManagementPatches), nameof(InventoryMenu_Draw_Postfix))
                );

                // Note: A button blocking is handled in InventoryPagePatches to avoid duplicate patches

                Monitor.Log("InventoryManagement patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply InventoryManagement patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Postfix for InventoryPage.draw to render the held item and tooltips.
        /// </summary>
        private static void InventoryPage_Draw_Postfix(InventoryPage __instance, SpriteBatch b)
        {
            if (!ModEntry.Config.EnableConsoleInventory)
                return;

            // Draw held item at cursor position (bottom-right of the slot, like console)
            if (IsHoldingItem && Game1.player.CursorSlotItem != null)
            {
                try
                {
                    // Get the current snapped component to determine cursor position
                    var snapped = __instance.currentlySnappedComponent;
                    if (snapped != null)
                    {
                        // Position item at bottom-right corner of the slot
                        // This matches console behavior where the item follows the cursor
                        int itemX = snapped.bounds.X + snapped.bounds.Width - 16;
                        int itemY = snapped.bounds.Y + snapped.bounds.Height - 16;

                        // Draw the item slightly larger for visibility
                        Game1.player.CursorSlotItem.drawInMenu(
                            b,
                            new Vector2(itemX, itemY),
                            0.75f,  // Slightly smaller scale to not overlap too much
                            1f,     // Transparency
                            0.9f,   // Layer depth (draw on top)
                            StackDrawType.Draw,
                            Color.White,
                            true    // Draw shadow
                        );
                    }
                }
                catch (Exception ex)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor?.Log($"InventoryManagement: Draw held item error: {ex.Message}", LogLevel.Debug);
                }
            }

            // Draw the drop zone box (visible "Drop" button in the bottom-right)
            try
            {
                ClickableComponent dropZone = null;
                foreach (var cc in __instance.allClickableComponents)
                {
                    if (cc.myID == DropZoneId)
                    {
                        dropZone = cc;
                        break;
                    }
                }

                if (dropZone != null)
                {
                    var bounds = dropZone.bounds;

                    // Draw 9-slice texture box border (same style as other menu elements)
                    Color boxColor = IsHoldingItem ? Color.Yellow : Color.White;
                    IClickableMenu.drawTextureBox(
                        b,
                        Game1.menuTexture,
                        new Rectangle(0, 256, 60, 60),
                        bounds.X, bounds.Y, bounds.Width, bounds.Height,
                        boxColor,
                        1f, // scale
                        false); // drawShadow

                    // Draw "Drop" text centered inside
                    string dropText = "Drop";
                    var textSize = Game1.smallFont.MeasureString(dropText);
                    float textX = bounds.X + (bounds.Width - textSize.X) / 2f;
                    float textY = bounds.Y + (bounds.Height - textSize.Y) / 2f;
                    Color textColor = IsHoldingItem ? new Color(220, 50, 50) : Color.Gray;
                    Utility.drawTextWithShadow(b, dropText, Game1.smallFont, new Vector2(textX, textY), textColor);
                }
            }
            catch (Exception ex)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"InventoryManagement: Draw drop zone error: {ex.Message}", LogLevel.Debug);
            }

            // Draw tooltip for hovered item (console-style hover tooltips)
            // Show when:
            // - NOT holding an item and A button is not pressed, OR
            // - Holding bait/tackle and hovering over a fishing rod (to see rod info)
            try
            {
                if (!CachedAButtonDown && Game1.options.gamepadControls)
                {
                    var snapped = __instance.currentlySnappedComponent;
                    if (snapped != null && snapped.myID >= 0 && snapped.myID < Game1.player.Items.Count)
                    {
                        Item hoveredItem = Game1.player.Items[snapped.myID];
                        if (hoveredItem != null)
                        {
                            bool shouldDrawTooltip = false;

                            if (!IsHoldingItem)
                            {
                                // Not holding anything - always show tooltip
                                shouldDrawTooltip = true;
                            }
                            else if (hoveredItem is FishingRod)
                            {
                                // Holding something and hovering over fishing rod
                                // Only show tooltip if holding bait or tackle
                                Item cursorItem = Game1.player.CursorSlotItem;
                                if (cursorItem != null && (cursorItem.Category == -21 || cursorItem.Category == -22))
                                {
                                    // -21 = Bait, -22 = Tackle
                                    shouldDrawTooltip = true;
                                }
                            }

                            if (shouldDrawTooltip)
                            {
                                // Replicate drawToolTip's edibility/buff preprocessing
                                int healAmount = -1;
                                string[] buffIcons = null;
                                if (hoveredItem is StardewValley.Object edibleObj
                                    && edibleObj.Edibility != -300)
                                {
                                    healAmount = edibleObj.Edibility;
                                    try
                                    {
                                        if (Game1.objectData.TryGetValue(hoveredItem.ItemId, out var rawData))
                                        {
                                            var getBuffIcons = typeof(IClickableMenu).GetMethod("GetBuffIcons",
                                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                                            if (getBuffIcons != null)
                                                buffIcons = (string[])getBuffIcons.Invoke(null, new object[] { hoveredItem, rawData });
                                        }
                                    }
                                    catch { }
                                }

                                // Measure actual tooltip height to position below or above slot
                                string desc = hoveredItem.getDescription();
                                string title = hoveredItem.DisplayName;
                                int tooltipH = MeasureTooltipHeight(hoveredItem, desc, title, healAmount, buffIcons);

                                var safeArea = Utility.getSafeArea();
                                int overrideX = snapped.bounds.Left;
                                int cursorBottom = snapped.bounds.Bottom + 32; // cursor extends 32px below slot
                                int overrideY;
                                if (cursorBottom + 8 + tooltipH <= safeArea.Bottom)
                                    overrideY = cursorBottom + 8;
                                else
                                    overrideY = Math.Max(safeArea.Top, snapped.bounds.Top - tooltipH - 8);

                                IClickableMenu.drawHoverText(
                                    b,
                                    desc,
                                    Game1.smallFont,
                                    overrideX: overrideX,
                                    overrideY: overrideY,
                                    boldTitleText: title,
                                    healAmountToDisplay: healAmount,
                                    buffIconsToDisplay: buffIcons,
                                    hoveredItem: hoveredItem);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"InventoryManagement: Draw tooltip error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Called every tick while in inventory menu.
        /// Handles Y button hold-to-repeat for single stack pickup.
        /// Also handles controller hover tooltips.
        /// </summary>
        public static void OnUpdateTicked()
        {
            // Poll Y button state directly for hold detection
            // This is more reliable than SMAPI events on Android
            GamePadState gpState = GamePad.GetState(PlayerIndex.One);
            bool isYButtonDown = gpState.Buttons.Y == ButtonState.Pressed;

            // Also check remapped button (if using Xbox layout, Y might be remapped)
            // For now, check raw Y button state

            if (isYButtonDown && !WasYButtonDown)
            {
                // Y button just pressed - do initial pickup and start hold tracking
                IsYButtonHeld = true;
                YButtonHoldTicks = 0;

                // Store which slot we're on for held pickup
                if (Game1.activeClickableMenu is GameMenu gm && gm.currentTab == GameMenu.inventoryTab)
                {
                    var invPage = gm.pages[GameMenu.inventoryTab] as InventoryPage;
                    if (invPage?.currentlySnappedComponent != null)
                    {
                        YButtonHoldSlotId = invPage.currentlySnappedComponent.myID;

                        // Do the initial single-item pickup
                        if (YButtonHoldSlotId >= 0 && YButtonHoldSlotId < Game1.player.Items.Count)
                        {
                            Item item = Game1.player.Items[YButtonHoldSlotId];
                            if (item != null)
                            {
                                // Check if fishing rod is hovered - let fishing rod patches handle that
                                if (item is not FishingRod && item is not Slingshot)
                                {
                                    PickupSingleItem(invPage, YButtonHoldSlotId, item);
                                }
                            }
                        }
                    }
                }

                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"InventoryManagement: Y button pressed (poll), slot={YButtonHoldSlotId}", LogLevel.Debug);
            }
            else if (!isYButtonDown && WasYButtonDown)
            {
                // Y button just released
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"InventoryManagement: Y button released (poll) after {YButtonHoldTicks} ticks", LogLevel.Debug);

                IsYButtonHeld = false;
                YButtonHoldTicks = 0;
                YButtonHoldSlotId = -1;
            }

            WasYButtonDown = isYButtonDown;

            // Handle Y button hold for continuous single-stack pickup
            if (IsYButtonHeld && isYButtonDown)
            {
                YButtonHoldTicks++;

                if (ModEntry.Config.VerboseLogging && YButtonHoldTicks % 30 == 0)
                {
                    Monitor?.Log($"InventoryManagement: Y held for {YButtonHoldTicks} ticks, slot={YButtonHoldSlotId}", LogLevel.Debug);
                }

                // After initial delay, repeat at regular intervals
                if (YButtonHoldTicks > YButtonHoldDelay &&
                    (YButtonHoldTicks - YButtonHoldDelay) % YButtonRepeatRate == 0)
                {
                    // Try to pick up another single item from the SAME slot we started on
                    if (Game1.activeClickableMenu is GameMenu gameMenu && gameMenu.currentTab == GameMenu.inventoryTab)
                    {
                        if (ModEntry.Config.VerboseLogging)
                            Monitor?.Log($"InventoryManagement: Y hold repeat at tick {YButtonHoldTicks}", LogLevel.Debug);
                        TryPickupSingleFromSlot(gameMenu, YButtonHoldSlotId);
                    }
                }
            }

            // Block A button hold tooltip behavior (Android-specific)
            bool isAButtonDown = gpState.Buttons.A == ButtonState.Pressed;
            CachedAButtonDown = isAButtonDown;
            if (ModEntry.Config.EnableConsoleInventory && isAButtonDown)
            {
                // Clear any tooltip/hover state when A is held to prevent Android tooltip popup
                if (Game1.activeClickableMenu is GameMenu gm && gm.currentTab == GameMenu.inventoryTab)
                {
                    var invPage = gm.pages[GameMenu.inventoryTab] as InventoryPage;
                    if (invPage != null)
                    {
                        ClearHoverState(invPage);
                    }
                }
            }
            WasAButtonDown = isAButtonDown;

            // Handle controller hover tooltips - trigger when snapped component changes
            // But only when A button is NOT held
            if (ModEntry.Config.EnableConsoleInventory && !isAButtonDown)
            {
                TriggerHoverTooltip();
            }

            // Sync holding state: if we think we're holding but CursorSlotItem is gone,
            // the game consumed it (e.g. equipped to an equipment slot, trashed it).
            if (IsHoldingItem && Game1.player.CursorSlotItem == null)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log("InventoryManagement: CursorSlotItem cleared externally, syncing hold state", LogLevel.Debug);
                IsHoldingItem = false;
                SourceSlotId = -1;
            }

            // Clear the pass-through flag. It was set during HandleAButton (via OnButtonsChanged)
            // and consumed by prefix patches during this tick's input processing.
            AllowGameAPress = false;

            // Inject drop zone component between sort (106) and trash (105) if not already present
            if (Game1.activeClickableMenu is GameMenu gm2 && gm2.currentTab == GameMenu.inventoryTab)
            {
                var invPage2 = gm2.pages[GameMenu.inventoryTab] as InventoryPage;
                if (invPage2 != null)
                    EnsureDropZoneComponent(invPage2);
            }
        }

        /// <summary>
        /// Inject the drop zone component below trash (105) in the bottom-right area.
        /// Visible box with "Drop" text, drawn in InventoryPage_Draw_Postfix.
        /// Navigation: Trash (105) ↔ Drop Zone (110). Sort (106) → Trash (105) direct.
        /// </summary>
        private static void EnsureDropZoneComponent(InventoryPage inventoryPage)
        {
            if (!ModEntry.Config.EnableConsoleInventory)
                return;

            try
            {
                // Check if drop zone already exists
                foreach (var cc in inventoryPage.allClickableComponents)
                {
                    if (cc.myID == DropZoneId)
                        return; // Already injected
                }

                // Find trash (105) to position below it
                ClickableComponent sort = null;
                ClickableComponent trash = null;
                foreach (var cc in inventoryPage.allClickableComponents)
                {
                    if (cc.myID == 106) sort = cc;
                    else if (cc.myID == 105) trash = cc;
                }

                if (trash == null)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor?.Log("InventoryManagement: Cannot find trash for drop zone placement", LogLevel.Debug);
                    return;
                }

                // Position: same X as trash, below trash with padding
                // Use bottomBoxY via reflection if available, otherwise fall back to trash position
                int dropX = trash.bounds.X;
                int dropY = trash.bounds.Y + trash.bounds.Height + 16; // 16px gap below trash
                int dropSize = 80; // Slightly larger than 64x64 equipment slots for "Drop" text

                // Center horizontally on trash column
                dropX = trash.bounds.X + (trash.bounds.Width - dropSize) / 2;

                var dropZone = new ClickableComponent(
                    new Rectangle(dropX, dropY, dropSize, dropSize),
                    "dropZone")
                {
                    myID = DropZoneId,
                    upNeighborID = 105,         // trash
                    downNeighborID = -1,         // nothing below
                    rightNeighborID = -1,        // right edge
                    leftNeighborID = -1          // will wire to equipment below
                };

                inventoryPage.allClickableComponents.Add(dropZone);

                // Ensure sort (106) goes directly to trash (105) — NOT through drop zone
                if (sort != null)
                    sort.downNeighborID = 105;

                // Wire trash (105) down → drop zone
                trash.downNeighborID = DropZoneId;

                // Wire equipment slots ↔ drop zone
                // Find the nearest equipment slot to the left at similar Y height
                // Equipment slots: boots (104), pants (109), trinket (120)
                int dropCenterY = dropY + dropSize / 2;
                int bestEquipId = -1;
                int bestDist = int.MaxValue;

                foreach (var cc in inventoryPage.allClickableComponents)
                {
                    // Check equipment slot IDs in the bottom half
                    if (cc.myID == 104 || cc.myID == 109 || cc.myID == 120)
                    {
                        int equipCenterY = cc.bounds.Y + cc.bounds.Height / 2;
                        int dist = Math.Abs(equipCenterY - dropCenterY);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestEquipId = cc.myID;
                        }
                    }
                }

                if (bestEquipId >= 0)
                {
                    dropZone.leftNeighborID = bestEquipId;

                    // Wire the equipment slot's right → drop zone
                    foreach (var cc in inventoryPage.allClickableComponents)
                    {
                        if (cc.myID == bestEquipId)
                        {
                            cc.rightNeighborID = DropZoneId;
                            break;
                        }
                    }
                }

                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"InventoryManagement: Injected drop zone at ({dropX},{dropY}) size {dropSize}x{dropSize}, below trash, leftNeighbor={dropZone.leftNeighborID}", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"InventoryManagement: EnsureDropZoneComponent error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Measure tooltip height using the same logic as drawHoverText.
        /// For weapons/boots/rings, getExtraSpaceNeededForTooltipSpecialIcons provides accurate height.
        /// For regular items, computed from text + title + category + edibility + buffs.
        /// </summary>
        private static int MeasureTooltipHeight(Item item, string description, string title, int healAmount, string[] buffIcons)
        {
            var font = Game1.smallFont;
            int lineH = (int)font.MeasureString("T").Y;

            // Base: text + padding + optional title (matches drawHoverText lines 1227-1229)
            int textH = (int)font.MeasureString(description).Y;
            int titleH = title != null ? (int)(Game1.dialogueFont.MeasureString(title).Y + 16f) : 0;
            int height = Math.Max(60, textH + 32 + titleH);

            // Buff icons (added before item-specific, lines 1239-1249)
            if (buffIcons != null)
            {
                foreach (string buff in buffIcons)
                    if (buff != "0" && buff != "")
                        height += 39;
                height += 4;
            }

            // Category line (lines 1272-1277)
            if (item.getCategoryName().Length > 0)
                height += lineH;

            // Item-specific height via virtual method (lines 1280-1282)
            // Weapons, boots, rings override this to return full height
            try
            {
                var sb = new System.Text.StringBuilder(description);
                Point extra = item.getExtraSpaceNeededForTooltipSpecialIcons(font, 300, 92, height, sb, title, -1);
                if (extra.Y != 0)
                    height = extra.Y;
            }
            catch { }

            // Edibility — energy/health bars (lines 1285-1294, non-weapons only)
            if (healAmount != -1 && !(item is Tool))
            {
                if (item is StardewValley.Object obj)
                {
                    int stamina = obj.staminaRecoveredOnConsumption();
                    height += (stamina > 0 && obj.healthRecoveredOnConsumption() > 0) ? 80 : 40;
                }
            }

            // Attachment slots — fishing rod, etc. (lines 1257-1271)
            if (item.attachmentSlots() > 0)
            {
                if (item.attachmentSlots() == 1)
                    height += 68;
                else
                    height += 144;
                height += 8;
            }

            return height + 16; // small safety padding
        }

        /// <summary>
        /// Clear the hover/tooltip state to prevent tooltips from appearing.
        /// </summary>
        private static void ClearHoverState(InventoryPage inventoryPage)
        {
            try
            {
                // Clear hover text and title using cached fields
                InvPage_HoverTextField?.SetValue(inventoryPage, "");
                InvPage_HoverTitleField?.SetValue(inventoryPage, "");
                InvPage_HoveredItemField?.SetValue(inventoryPage, null);

                // Also clear from inventory menu
                if (inventoryPage.inventory != null)
                {
                    InvMenu_HoveredItemField?.SetValue(inventoryPage.inventory, null);
                }
            }
            catch (Exception ex)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"InventoryManagement: ClearHoverState error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Try to pick up a single item from a specific slot (for hold-repeat).
        /// </summary>
        private static void TryPickupSingleFromSlot(GameMenu gameMenu, int slotId)
        {
            try
            {
                if (slotId < 0 || slotId >= Game1.player.Items.Count)
                    return;

                var inventoryPage = gameMenu.pages[GameMenu.inventoryTab] as InventoryPage;
                if (inventoryPage == null)
                    return;

                Item item = Game1.player.Items[slotId];
                if (item == null)
                    return;

                PickupSingleItem(inventoryPage, slotId, item);
            }
            catch (Exception ex)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"InventoryManagement: TryPickupSingleFromSlot error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Trigger hover tooltip when controller cursor moves to a new slot.
        /// This makes tooltips appear on hover like on console, not just on press-and-hold.
        /// </summary>
        private static void TriggerHoverTooltip()
        {
            try
            {
                if (!(Game1.activeClickableMenu is GameMenu gameMenu) || gameMenu.currentTab != GameMenu.inventoryTab)
                    return;

                var inventoryPage = gameMenu.pages[GameMenu.inventoryTab] as InventoryPage;
                if (inventoryPage == null)
                    return;

                var snapped = inventoryPage.currentlySnappedComponent;
                if (snapped == null)
                {
                    LastSnappedComponentId = -1;
                    return;
                }

                // Check if we moved to a different component
                if (snapped.myID != LastSnappedComponentId)
                {
                    LastSnappedComponentId = snapped.myID;

                    // Calculate the center of the snapped component
                    int hoverX = snapped.bounds.X + snapped.bounds.Width / 2;
                    int hoverY = snapped.bounds.Y + snapped.bounds.Height / 2;

                    // Call performHoverAction to trigger tooltip display
                    inventoryPage.performHoverAction(hoverX, hoverY);

                    // Also try to directly set hoveredItem for inventory slots
                    if (snapped.myID >= 0 && snapped.myID < Game1.player.Items.Count)
                    {
                        Item item = Game1.player.Items[snapped.myID];
                        if (item != null && inventoryPage.inventory != null)
                        {
                            // Set hover state using cached fields
                            InvMenu_HoveredItemField?.SetValue(inventoryPage.inventory, item);
                            InvPage_HoverTextField?.SetValue(inventoryPage, item.getDescription());
                            InvPage_HoverTitleField?.SetValue(inventoryPage, item.DisplayName);
                            InvPage_HoveredItemField?.SetValue(inventoryPage, item);
                        }
                    }
                }


            }
            catch (Exception ex)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"InventoryManagement: TriggerHoverTooltip error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Handle A button press for console-style inventory management.
        /// Returns true if the input was handled and should be suppressed.
        /// </summary>
        public static bool HandleAButton(GameMenu gameMenu, IMonitor monitor)
        {
            Monitor = monitor;

            if (!ModEntry.Config.EnableConsoleInventory)
                return false;

            try
            {
                var inventoryPage = gameMenu.pages[GameMenu.inventoryTab] as InventoryPage;
                if (inventoryPage == null)
                    return false;

                var snapped = inventoryPage.currentlySnappedComponent;
                if (snapped == null)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log("InventoryManagement: No snapped component", LogLevel.Debug);
                    return false;
                }

                int slotId = snapped.myID;

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"InventoryManagement: A pressed, slot={slotId}, holding={IsHoldingItem}", LogLevel.Debug);

                // Check if this is a valid inventory slot (0-35 for main inventory)
                bool isInventorySlot = slotId >= 0 && slotId < Game1.player.Items.Count;

                if (IsHoldingItem)
                {
                    // We're holding an item - place it
                    return PlaceItem(inventoryPage, slotId, isInventorySlot);
                }
                else
                {
                    // Not holding - try to pick up item
                    if (!isInventorySlot)
                    {
                        return PickUpFromEquipmentSlot(inventoryPage, slotId);
                    }

                    Item item = Game1.player.Items[slotId];
                    if (item == null)
                    {
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"InventoryManagement: Slot {slotId} is empty, nothing to pick up", LogLevel.Debug);
                        return false;
                    }

                    return PickUpItem(inventoryPage, slotId, item);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"InventoryManagement HandleAButton error: {ex.Message}", LogLevel.Error);
                Monitor.Log(ex.StackTrace, LogLevel.Debug);
                CancelHold();
                return false;
            }
        }

        /// <summary>
        /// Pick up a single item from a stack (Y button behavior).
        /// </summary>
        private static bool PickupSingleItem(InventoryPage inventoryPage, int slotId, Item item)
        {
            try
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"InventoryManagement: Y pickup single from slot {slotId}, item={item.Name}, stack={item.Stack}", LogLevel.Debug);

                Item cursorItem = Game1.player.CursorSlotItem;

                // If already holding something
                if (cursorItem != null)
                {
                    // Can only add if same item type and stackable
                    if (cursorItem.canStackWith(item))
                    {
                        // Check if cursor item can hold more
                        if (cursorItem.Stack < cursorItem.maximumStackSize())
                        {
                            cursorItem.Stack++;

                            // Reduce source stack
                            item.Stack--;
                            if (item.Stack <= 0)
                            {
                                Game1.player.Items[slotId] = null;
                            }

                            Game1.playSound("dwop");
                            Monitor.Log($"InventoryManagement: Added 1 to cursor stack (now {cursorItem.Stack})", LogLevel.Debug);
                            return true;
                        }
                        else
                        {
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log($"InventoryManagement: Cursor stack full", LogLevel.Debug);
                            Game1.playSound("cancel");
                            return true;
                        }
                    }
                    else
                    {
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"InventoryManagement: Cannot stack different items", LogLevel.Debug);
                        return false;
                    }
                }
                else
                {
                    // Not holding anything - pick up 1 from stack
                    if (item.Stack > 1)
                    {
                        // Create a copy with stack of 1
                        Item singleItem = item.getOne();
                        singleItem.Stack = 1;

                        // Put on cursor
                        Game1.player.CursorSlotItem = singleItem;

                        // Reduce source stack
                        item.Stack--;

                        IsHoldingItem = true;
                        SourceSlotId = slotId;

                        Game1.playSound("dwop");
                        Monitor.Log($"InventoryManagement: Picked up 1 {singleItem.Name}, source stack now {item.Stack}", LogLevel.Info);
                    }
                    else
                    {
                        // Only 1 item - pick up entire thing (same as A button)
                        Game1.player.CursorSlotItem = item;
                        Game1.player.Items[slotId] = null;

                        IsHoldingItem = true;
                        SourceSlotId = slotId;

                        Game1.playSound("dwop");
                        Monitor.Log($"InventoryManagement: Picked up last {item.Name}", LogLevel.Info);
                    }

                    // Clear selection to remove red box from source
                    ClearInventorySelection(inventoryPage);

                    return true;
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"InventoryManagement PickupSingleItem error: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Pick up an entire item stack (A button).
        /// </summary>
        private static bool PickUpItem(InventoryPage inventoryPage, int slotId, Item item)
        {
            try
            {
                Monitor.Log($"InventoryManagement: Picking up {item.Name} (x{item.Stack}) from slot {slotId}", LogLevel.Debug);

                // Move item from inventory to cursor
                Game1.player.CursorSlotItem = item;
                Game1.player.Items[slotId] = null;

                IsHoldingItem = true;
                SourceSlotId = slotId;

                // Clear selection to remove red box from source slot
                ClearInventorySelection(inventoryPage);

                Game1.playSound("pickUpItem");
                Monitor.Log($"InventoryManagement: Now holding {item.Name}", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                Monitor.Log($"InventoryManagement PickUpItem error: {ex.Message}", LogLevel.Error);
                CancelHold();
                return false;
            }
        }

        /// <summary>
        /// Pick up an item from an equipment slot to cursor (console-style).
        /// </summary>
        private static bool PickUpFromEquipmentSlot(InventoryPage inventoryPage, int slotId)
        {
            Item pickedUp = null;

            switch (slotId)
            {
                case 101: // Hat
                    if (Game1.player.hat.Value != null)
                    {
                        pickedUp = Game1.player.hat.Value;
                        Game1.player.hat.Value = null;
                    }
                    break;
                case 102: // Right Ring
                {
                    Ring ring = Game1.player.rightRing.Value;
                    if (ring != null)
                    {
                        ring.onUnequip(Game1.player);
                        Game1.player.rightRing.Value = null;
                        pickedUp = ring;
                    }
                    break;
                }
                case 103: // Left Ring
                {
                    Ring ring = Game1.player.leftRing.Value;
                    if (ring != null)
                    {
                        ring.onUnequip(Game1.player);
                        Game1.player.leftRing.Value = null;
                        pickedUp = ring;
                    }
                    break;
                }
                case 104: // Boots
                {
                    Boots boots = Game1.player.boots.Value;
                    if (boots != null)
                    {
                        boots.onUnequip(Game1.player);
                        Game1.player.boots.Value = null;
                        pickedUp = boots;
                    }
                    break;
                }
                case 108: // Shirt
                    if (Game1.player.shirtItem.Value != null)
                    {
                        pickedUp = Game1.player.shirtItem.Value;
                        Game1.player.shirtItem.Value = null;
                    }
                    break;
                case 109: // Pants
                    if (Game1.player.pantsItem.Value != null)
                    {
                        pickedUp = Game1.player.pantsItem.Value;
                        Game1.player.pantsItem.Value = null;
                    }
                    break;
                case 106: // Organize/sort button — handle directly because Android's
                          // receiveLeftClick pass-through fires at mouse position, not
                          // snapped component position, so the sort button never gets hit.
                    InventoryPagePatches.SortPlayerInventory();
                    return true;
                case DropZoneId: // Drop zone — nothing to pick up, consume input
                    return true;
                default:
                    // Non-equipment slot (trash 105, tab icons, etc.) — let game handle
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"InventoryManagement: Slot {slotId} is not equipment, passing through", LogLevel.Debug);
                    AllowGameAPress = true;
                    return false;
            }

            if (pickedUp == null)
                return true; // Empty equipment slot — consume input, do nothing

            Game1.player.CursorSlotItem = pickedUp;
            IsHoldingItem = true;
            SourceSlotId = -1; // No inventory source slot

            ClearInventorySelection(inventoryPage);
            Game1.playSound("dwop");
            Monitor.Log($"InventoryManagement: Picked up {pickedUp.Name} from equipment slot {slotId}", LogLevel.Info);
            return true;
        }

        /// <summary>
        /// Place the held item at the target slot.
        /// </summary>
        private static bool PlaceItem(InventoryPage inventoryPage, int targetSlotId, bool isInventorySlot)
        {
            try
            {
                Item heldItem = Game1.player.CursorSlotItem;

                if (heldItem == null)
                {
                    Monitor.Log($"InventoryManagement: No item on cursor to place", LogLevel.Debug);
                    CancelHold();
                    return false;
                }

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"InventoryManagement: Placing {heldItem.Name} at slot {targetSlotId}", LogLevel.Debug);

                // Handle non-inventory slots (equipment slots, trash, etc.) directly.
                // Android's InventoryPage doesn't support equipping from CursorSlotItem —
                // it only handles equipping via its internal touch-drag state machine.
                // Diagnostic builds v3.2.3-v3.2.5 confirmed: receiveLeftClick, leftClickHeld,
                // and releaseLeftClick all fire at correct coordinates with CursorSlotItem set,
                // but NONE consume it. Touch-drag equip shows cursor=(none) — different mechanism.
                // So we handle equipment/trash ourselves, same as we handle inventory swaps.
                if (!isInventorySlot)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"InventoryManagement: Placing {heldItem.Name} at non-inventory slot {targetSlotId}", LogLevel.Debug);
                    return PlaceItemInNonInventorySlot(targetSlotId, heldItem);
                }

                Item targetItem = Game1.player.Items[targetSlotId];

                if (targetItem == null)
                {
                    // Empty slot - place item
                    Game1.player.Items[targetSlotId] = heldItem;
                    Game1.player.CursorSlotItem = null;

                    IsHoldingItem = false;
                    SourceSlotId = -1;

                    Game1.playSound("stoneStep");
                    Monitor.Log($"InventoryManagement: Placed {heldItem.Name} in empty slot {targetSlotId}", LogLevel.Info);
                }
                else if (targetItem.canStackWith(heldItem))
                {
                    // Same item type - try to stack
                    int spaceAvailable = targetItem.maximumStackSize() - targetItem.Stack;
                    int toAdd = Math.Min(spaceAvailable, heldItem.Stack);

                    if (toAdd > 0)
                    {
                        targetItem.Stack += toAdd;
                        heldItem.Stack -= toAdd;

                        if (heldItem.Stack <= 0)
                        {
                            Game1.player.CursorSlotItem = null;
                            IsHoldingItem = false;
                            SourceSlotId = -1;
                        }

                        Game1.playSound("stoneStep");
                        Monitor.Log($"InventoryManagement: Stacked {toAdd}x {heldItem.Name} (target now {targetItem.Stack})", LogLevel.Info);
                    }
                    else
                    {
                        // Stack is full - swap instead
                        Game1.player.Items[targetSlotId] = heldItem;
                        Game1.player.CursorSlotItem = targetItem;
                        // Still holding (the swapped item)
                        Game1.playSound("stoneStep");
                        Monitor.Log($"InventoryManagement: Stack full, swapped with {targetItem.Name}", LogLevel.Info);
                    }
                }
                else
                {
                    // Different items - swap
                    Game1.player.Items[targetSlotId] = heldItem;
                    Game1.player.CursorSlotItem = targetItem;

                    // Still holding the swapped item
                    Game1.playSound("stoneStep");
                    Monitor.Log($"InventoryManagement: Swapped, now holding {targetItem.Name}", LogLevel.Info);
                }

                return true;
            }
            catch (Exception ex)
            {
                Monitor.Log($"InventoryManagement PlaceItem error: {ex.Message}", LogLevel.Error);
                Monitor.Log(ex.StackTrace, LogLevel.Debug);
                CancelHold();
                return false;
            }
        }

        /// <summary>
        /// Handle placing a held item into a non-inventory slot (equipment or trash).
        /// Android's InventoryPage doesn't support equipping from CursorSlotItem —
        /// it only supports touch-drag. So we handle equipment/trash directly.
        /// </summary>
        private static bool PlaceItemInNonInventorySlot(int slotId, Item heldItem)
        {
            switch (slotId)
            {
                case 101: return TryEquipHat(heldItem);
                case 102: return TryEquipRing(heldItem, isLeft: false);
                case 103: return TryEquipRing(heldItem, isLeft: true);
                case 104: return TryEquipBoots(heldItem);
                case 105: return TrashHeldItem(heldItem);
                case 106: // Organize/sort button — sort inventory, keep held item on cursor
                    InventoryPagePatches.SortPlayerInventory();
                    return true;
                case DropZoneId: return DropHeldItem(heldItem);
                case 108: return TryEquipClothing(heldItem, isShirt: true);
                case 109: return TryEquipClothing(heldItem, isShirt: false);
                default:
                    // Unknown non-inventory slot (tab icons, etc.) — let game handle
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"InventoryManagement: Slot {slotId} is unknown non-inventory, passing through", LogLevel.Debug);
                    AllowGameAPress = true;
                    return false;
            }
        }

        private static bool TryEquipHat(Item heldItem)
        {
            if (heldItem is not Hat newHat)
            {
                Game1.playSound("cancel");
                return true;
            }

            Hat oldHat = Game1.player.hat.Value;
            Game1.player.hat.Value = newHat;
            Game1.player.CursorSlotItem = oldHat;

            if (oldHat == null)
            {
                IsHoldingItem = false;
                SourceSlotId = -1;
            }

            Game1.playSound("grassyStep");
            Monitor.Log($"InventoryManagement: Equipped hat {newHat.Name}" + (oldHat != null ? $", holding {oldHat.Name}" : ""), LogLevel.Info);
            return true;
        }

        private static bool TryEquipRing(Item heldItem, bool isLeft)
        {
            if (heldItem is not Ring newRing)
            {
                Game1.playSound("cancel");
                return true;
            }

            var ringField = isLeft ? Game1.player.leftRing : Game1.player.rightRing;
            Ring oldRing = ringField.Value;

            if (oldRing != null)
                oldRing.onUnequip(Game1.player);

            ringField.Value = newRing;
            newRing.onEquip(Game1.player);
            Game1.player.CursorSlotItem = oldRing;

            if (oldRing == null)
            {
                IsHoldingItem = false;
                SourceSlotId = -1;
            }

            Game1.playSound("crit");
            string slotName = isLeft ? "left" : "right";
            Monitor.Log($"InventoryManagement: Equipped {slotName} ring {newRing.Name}" + (oldRing != null ? $", holding {oldRing.Name}" : ""), LogLevel.Info);
            return true;
        }

        private static bool TryEquipBoots(Item heldItem)
        {
            if (heldItem is not Boots newBoots)
            {
                Game1.playSound("cancel");
                return true;
            }

            Boots oldBoots = Game1.player.boots.Value;

            if (oldBoots != null)
                oldBoots.onUnequip(Game1.player);

            Game1.player.boots.Value = newBoots;
            newBoots.onEquip(Game1.player);
            Game1.player.CursorSlotItem = oldBoots;

            if (oldBoots == null)
            {
                IsHoldingItem = false;
                SourceSlotId = -1;
            }

            Game1.playSound("sandyStep");
            Monitor.Log($"InventoryManagement: Equipped boots {newBoots.Name}" + (oldBoots != null ? $", holding {oldBoots.Name}" : ""), LogLevel.Info);
            return true;
        }

        private static bool TryEquipClothing(Item heldItem, bool isShirt)
        {
            if (heldItem is not Clothing newClothing)
            {
                Game1.playSound("cancel");
                return true;
            }

            // Check clothing type matches the slot (0 = Shirt, 1 = Pants)
            bool clothingIsShirt = (int)newClothing.clothesType.Value == 0;
            if (clothingIsShirt != isShirt)
            {
                Game1.playSound("cancel");
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"InventoryManagement: {heldItem.Name} is {(clothingIsShirt ? "shirt" : "pants")}, wrong slot", LogLevel.Debug);
                return true;
            }

            var clothingField = isShirt ? Game1.player.shirtItem : Game1.player.pantsItem;
            Clothing oldClothing = clothingField.Value;

            clothingField.Value = newClothing;
            Game1.player.CursorSlotItem = oldClothing;

            if (oldClothing == null)
            {
                IsHoldingItem = false;
                SourceSlotId = -1;
            }

            Game1.playSound("sandyStep");
            string slotName = isShirt ? "shirt" : "pants";
            Monitor.Log($"InventoryManagement: Equipped {slotName} {newClothing.Name}" + (oldClothing != null ? $", holding {oldClothing.Name}" : ""), LogLevel.Info);
            return true;
        }

        private static bool TrashHeldItem(Item heldItem)
        {
            // Calculate refund based on trash can upgrade level
            try
            {
                int refund = Utility.getTrashReclamationPrice(heldItem, Game1.player);
                if (refund > 0)
                {
                    Game1.player.Money += refund;
                    Monitor.Log($"InventoryManagement: Trash refund: {refund}g", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"InventoryManagement: Trash refund calculation failed: {ex.Message}", LogLevel.Debug);
            }

            Monitor.Log($"InventoryManagement: Trashed {heldItem.Name}", LogLevel.Info);
            Game1.playSound("trashcan");
            Game1.player.CursorSlotItem = null;
            IsHoldingItem = false;
            SourceSlotId = -1;
            return true;
        }

        /// <summary>
        /// Drop the held item on the ground as debris at the player's feet.
        /// </summary>
        private static bool DropHeldItem(Item heldItem)
        {
            Game1.createItemDebris(heldItem, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
            Game1.playSound("throwDownITem");
            Monitor.Log($"InventoryManagement: Dropped {heldItem.Name} on ground", LogLevel.Info);
            Game1.player.CursorSlotItem = null;
            IsHoldingItem = false;
            SourceSlotId = -1;
            return true;
        }

        /// <summary>
        /// Clear the inventory selection highlight (red box).
        /// </summary>
        private static void ClearInventorySelection(InventoryPage inventoryPage)
        {
            try
            {
                if (inventoryPage.inventory != null)
                {
                    InvMenu_HoveredItemField?.SetValue(inventoryPage.inventory, null);
                }

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log("InventoryManagement: Cleared selection highlight", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"InventoryManagement: ClearSelection error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Cancel any active hold state (e.g., when leaving menu).
        /// </summary>
        public static void CancelHold()
        {
            if (IsHoldingItem)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log("InventoryManagement: Cancelling hold state", LogLevel.Debug);

                // If we still have a cursor item, put it back somewhere safe
                if (Game1.player.CursorSlotItem != null)
                {
                    Item item = Game1.player.CursorSlotItem;

                    // Try to put back in original slot first
                    if (SourceSlotId >= 0 && SourceSlotId < Game1.player.Items.Count &&
                        Game1.player.Items[SourceSlotId] == null)
                    {
                        Game1.player.Items[SourceSlotId] = item;
                        Monitor?.Log($"InventoryManagement: Returned {item.Name} to source slot {SourceSlotId}", LogLevel.Debug);
                    }
                    // Try to add back to inventory
                    else if (!Game1.player.addItemToInventoryBool(item))
                    {
                        // Inventory full - drop item
                        Game1.createItemDebris(item, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
                        Monitor?.Log($"InventoryManagement: Dropped {item.Name} (inventory full)", LogLevel.Warn);
                    }

                    Game1.player.CursorSlotItem = null;
                }

                IsHoldingItem = false;
                SourceSlotId = -1;
            }

            // Also reset Y button state
            IsYButtonHeld = false;
            YButtonHoldTicks = 0;
        }

        /// <summary>
        /// Check if we're currently holding an item.
        /// </summary>
        public static bool IsCurrentlyHolding()
        {
            return IsHoldingItem && Game1.player.CursorSlotItem != null;
        }

        /// <summary>
        /// Called when leaving the inventory menu - clean up state.
        /// </summary>
        public static void OnMenuClosed()
        {
            CancelHold();
        }

        /// <summary>
        /// Called by FishingRodPatches when it clears CursorSlotItem (e.g., attaching bait to rod).
        /// This keeps our IsHoldingItem state in sync.
        /// </summary>
        public static void OnCursorItemCleared()
        {
            if (IsHoldingItem)
            {
                Monitor?.Log("InventoryManagement: Cursor cleared by external code, syncing state", LogLevel.Debug);
                IsHoldingItem = false;
                SourceSlotId = -1;
            }
        }

        /// <summary>
        /// Called by FishingRodPatches when it puts an item on the cursor (e.g., detaching bait from rod).
        /// This keeps our IsHoldingItem state in sync.
        /// </summary>
        public static void SetHoldingItem(bool holding)
        {
            Monitor?.Log($"InventoryManagement: SetHoldingItem({holding}) called by external code", LogLevel.Debug);
            IsHoldingItem = holding;
            if (!holding)
            {
                SourceSlotId = -1;
            }
        }

        /// <summary>
        /// Prefix on InventoryMenu.draw — suppress the red selection box (tile 56) by temporarily
        /// clearing currentlySelectedItem. The draw method uses this only for the slot background
        /// tile choice (56=red vs 10=normal). We save the value and restore in postfix.
        /// currentlySelectedItem is Android-only, so we use reflection.
        /// </summary>
        private static void InventoryMenu_Draw_Prefix(InventoryMenu __instance)
        {
            if (InvMenu_CurrentlySelectedItemField == null)
            {
                _savedSelectedItem = -1;
                return;
            }

            _savedSelectedItem = (int)InvMenu_CurrentlySelectedItemField.GetValue(__instance);
            if (_savedSelectedItem >= 0)
                InvMenu_CurrentlySelectedItemField.SetValue(__instance, -1);
        }

        /// <summary>
        /// Postfix on InventoryMenu.draw — restore currentlySelectedItem and draw finger cursor
        /// at the selected slot position (replacing the red box visual).
        /// </summary>
        private static void InventoryMenu_Draw_Postfix(InventoryMenu __instance, SpriteBatch b)
        {
            if (InvMenu_CurrentlySelectedItemField != null)
                InvMenu_CurrentlySelectedItemField.SetValue(__instance, _savedSelectedItem);

            if (_savedSelectedItem < 0 || _savedSelectedItem >= __instance.inventory.Count)
                return;

            var slot = __instance.inventory[_savedSelectedItem];
            if (slot == null)
                return;

            b.Draw(Game1.mouseCursors,
                new Vector2(slot.bounds.X, slot.bounds.Y),
                Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 44, 16, 16),
                Color.White, 0f, Vector2.Zero,
                4f + Game1.dialogueButtonScale / 150f,
                SpriteEffects.None, 1f);
        }
    }
}
