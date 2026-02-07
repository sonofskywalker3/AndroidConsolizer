# Shipping Bin Controller Fix - Technical Specification

## Current Version: 2.0.28

## Problem Summary
On Android, the shipping bin menu has controller navigation issues:
1. The only snap point above inventory is the "last shipped" display (myID 12598)
2. Pressing A on "last shipped" while holding an item picks up the last shipped item instead of dropping your held item
3. Need a way to drop items into the shipping bin via controller

## Solution Approach
Create a custom "drop zone" snap point that the controller can navigate to when holding an item.

## Key Technical Discoveries

### Android-Specific Issues
1. **Property access crashes**: Accessing `__instance.heldItem` property directly can return wrong types (e.g., `InventoryMenu` instead of `Item`) or crash
2. **Reflection required**: Must use reflection to access the `heldItem` field in `MenuWithInventory`:
   ```csharp
   var field = typeof(MenuWithInventory).GetField("heldItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
   var heldItem = field.GetValue(menu) as Item;
   ```
3. **Setting heldItem to null crashes**: Setting the field to null directly causes `NullReferenceException` in `ItemGrabMenu.draw()`
4. **Item property access crashes**: Accessing `item.DisplayName` or `item.Name` on held items can crash on Android. Use `item.ItemId` or avoid accessing properties altogether.

### Component IDs
- **12598**: "Last shipped" display component
- **12599**: Custom drop zone component (we create this)
- **0-35ish**: Inventory slots
- **< 100**: Generally inventory area

### Menu Structure
- `ItemGrabMenu` inherits from `MenuWithInventory`
- `heldItem` field is defined in `MenuWithInventory`
- `shippingBin` boolean property indicates if menu is for shipping bin
- `allClickableComponents` list contains all navigable components
- `currentlySnappedComponent` tracks controller cursor position

## Implementation Details

### Patches Applied
1. **`setSourceItem` Postfix** (not reliable): Was supposed to add drop zone when menu opens, but doesn't always fire
2. **`receiveGamePadButton` Prefix**: Main handler for controller input

### Drop Zone Creation
Created on-demand in `EnsureDropZone()`:
- Position: 128 pixels LEFT of the "last shipped" component
- Size: 100x100 pixels
- myID: 12599
- Navigation: RIGHT goes to 12598 (last shipped)
- Updates 12598's leftNeighborID to point to 12599

### Navigation Logic
When holding an item:
- **UP from inventory**: Redirects to drop zone (12599) instead of last shipped (12598)
- **LEFT/RIGHT between 12599 and 12598**: BLOCKED to prevent accidental navigation to last shipped

When NOT holding an item:
- Normal navigation (UP goes to last shipped as usual)

### Shipping Logic
When A pressed on drop zone (12599) with held item:
1. Check `canBeShipped()` - play cancel sound if false
2. Get shipping bin via `Game1.getFarm()?.getShippingBin(Game1.player)`
3. Stack with existing items using `canStackWith()` and `maximumStackSize()`
4. Add remaining as new stack using `heldItem.getOne()` then set `Stack`
5. **Critical**: Set `heldItem.Stack = 0` BEFORE nulling the field (prevents item duplication)
6. Set field to null via reflection
7. Play "Ship" sound
8. Move cursor back to first inventory slot

### Item Duplication Bug Fix
The original held item must have its Stack set to 0 before clearing:
```csharp
heldItem.Stack = 0;  // Prevents duplication if item gets "returned"
field.SetValue(menu, null);
```

## Files Modified
- `Patches/ShippingBinPatches.cs` - Main patch implementation
- `ModConfig.cs` - Contains `EnableShippingBinFix` toggle

## Testing Checklist
- [ ] Pick up item from inventory
- [ ] Press UP - should go to drop zone (not last shipped)
- [ ] Press LEFT/RIGHT - should be blocked
- [ ] Press A - item ships, cursor returns to inventory
- [ ] Ship same item type - should stack (check bin total increases)
- [ ] Item should NOT be duplicated (not in bin AND inventory)
- [ ] Without held item, UP goes to last shipped (normal behavior)
- [ ] Can recover last shipped item when not holding anything

## Known Issues / TODO
- Last shipped display may not update visually after shipping (vanilla limitation?)
- Need to test with items that can't be shipped
- Need to verify stacking works correctly with max stack sizes

## Config Option
```json
"EnableShippingBinFix": true
```

## Debugging Tips
- All shipping bin logs prefixed with "ShippingBin:"
- Key log messages:
  - "Created drop zone at (X, Y)" - drop zone initialization
  - "Redirecting UP to drop zone" - navigation working
  - "Blocking LEFT/RIGHT navigation" - block working
  - "A pressed on drop zone with held item" - ship attempt
  - "Stacked X onto existing (now Y)" - stacking working
  - "Added new stack of X" - new stack created
  - "Shipped Xx item successfully" - complete
