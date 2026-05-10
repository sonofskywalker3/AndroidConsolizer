# Android Inventory Behavior Notes

**Read this file before working on any inventory-related feature** (TODO items #7, #8, #11, or anything touching InventoryManagementPatches.cs, ItemGrabMenuPatches.cs, or equipment slots).

## How Android Inventory Differs from Console

On **Nintendo Switch/Console**:
- A button picks up item, attaches to cursor visually
- Moving cursor drags the item
- A on another slot swaps items
- `Game1.player.CursorSlotItem` holds the dragged item

On **Android with Controller**:
- A button "selects" item (red highlight box) but does NOT attach to cursor
- `Game1.player.CursorSlotItem` is NOT set by A button
- A on another slot: if empty, moves item; if occupied, ABANDONS held item and selects new one
- Touch/drag (press-and-hold) DOES work properly and shows item on cursor

## Key Technical Findings

1. **`Game1.player.CursorSlotItem`** - Can be set programmatically but item is INVISIBLE on Android
   - The game tracks it internally (subsequent checks see the item)
   - But no visual feedback to user
   - A button doesn't clear/place it properly

2. **Slot tracking** - Multiple sources with different behavior:
   - `inventoryPage.inventory.currentlySnappedComponent` - Often stale/stuck
   - `inventoryPage.currentlySnappedComponent` - Tracks cursor movement properly on Android
   - `gameMenu.currentlySnappedComponent` - Usually null

3. **Touch/long-press mechanism** - DOES attach items properly
   - Uses `receiveLeftClick()`, `leftClickHeld()`, `releaseLeftClick()` methods
   - Potential fix: Simulate these calls when A is pressed
   - Would need to calculate screen coordinates of snapped slot

4. **Equipment slots** - Reachable via snap navigation but non-functional
   - Controller cursor CAN navigate to hat, shirt, pants, boots, ring1, ring2 slots
   - Pressing A on an equipment slot with a held item does nothing — the equip action is not triggered
   - Equipment slots have different component IDs than the 36-slot inventory grid
   - The mod's A-button handler (InventoryManagementPatches) likely only handles inventory grid slot IDs, not equipment slot IDs
   - Touch/drag DOES equip items, but mixing touch and controller input while an item is selected can cause the controller-selected item to be orphaned (not in any slot)

5. **Item loss on menu exit** - Held items drop on the ground instead of returning to inventory
   - If an item is controller-selected (removed from slot, shown with red outline) and the menu is closed, the item drops at the player's feet
   - Confirmed: User lost a ring to the river this way when touch input interrupted a controller selection
   - The menu exit path does NOT check for orphaned items or return them to inventory
   - Fix needed in menu close handlers — see TODO #11

## Workaround Used for Fishing Rod Bait

Since `CursorSlotItem` doesn't display on Android, we track selection ourselves:
1. When A pressed on bait/tackle, store the slot index internally (`SelectedBaitTackleSlot`)
2. When Y pressed on fishing rod, use the stored slot to attach
3. Clear selection after attachment or when non-bait item is selected

This pattern could be extended for general inventory management, but proper console-style
behavior would require simulating the touch/drag mechanism.

## Methods to Investigate for Console-Style Fix

- `InventoryPage.receiveLeftClick(int x, int y, bool playSound)`
- `InventoryPage.leftClickHeld(int x, int y)` - Called during drag
- `InventoryPage.releaseLeftClick(int x, int y)`
- Calculate slot coordinates from `currentlySnappedComponent.bounds`
