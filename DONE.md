# AndroidConsolizer - Completed Features

Technical reference for all completed work. Implementation notes, root causes, and lessons learned are preserved here for future reference when working on related systems.

---

## Shop System (v2.7.5-v2.8.22)

### 2. Shop Purchase Flow Fix (CRITICAL) — v2.7.5-v2.7.14
- **Root cause:** Purchase logic calls `actionWhenPurchased(shopId)`, checks/consumes trade items, handles inventory-full refunds.
- Uses `hoveredItem` validated against `forSale` list (forSaleButtons myIDs are all -500 on Android).
- Sell-tab detection via `inventoryVisible` field prevents phantom purchases.
- Buy quantity reset on sell tab prevents trigger bleed.
- **File:** `Patches/ShopMenuPatches.cs`

### 4. Shop Quantity Enhancement — Confirmed working v2.8.9
- Non-bumper mode: LB/RB = +/-10, bumper mode: LB/RB = +/-1
- Hold-to-repeat with 333ms delay then 50ms repeat
- Quantity limits respect stock, money, trade items, and stack size
- **Implementation:** Initial press in `ModEntry.OnButtonsChanged`, auto-repeat in `ShopMenuPatches.Update_Postfix`

### 5b. Shop Inventory Tab — v2.8.9-v2.8.17
- Touch tab button blocked when controller connected (v2.8.12)
- Controller button icon drawn on shop UI — Y/X/Square depending on layout, dims on sell tab (v2.8.13-v2.8.17)
- Tab switching works via controller button for all layouts

### 5c. Buy Quantity Bleeds to Sell Tab — v2.7.14
- **Root cause:** `HandleShopBumperQuantity` ran on any LB/RB press without checking `inventoryVisible`. Vanilla trigger input also modified `quantityToBuy` regardless of tab.
- **Fix:** Guard with `inventoryVisible` check. Reset `quantityToBuy` to 1 in `Update_Postfix` when on sell tab.

### 5d. Console-Style Shop Selling — v2.7.16-v2.7.20
- **A** sells full stack, **Y** sells one, **Hold Y** sells repeatedly (~333ms delay, ~50ms repeat)
- Sell price tooltip: custom-drawn box next to selected inventory slot showing per-unit and stack total
- **Key finding (v2.7.19):** `hoveredItem` is NEVER set on sell tab with controller — `performHoverAction` uses mouse position which doesn't track snap nav on Android. All sell-tab item detection uses `GetSellTabSelectedItem()` reading `currentlySnappedComponent`.
- Sell price: `Object.sellToStorePrice()` for Objects, `salePrice()/2` for non-Objects
- **File:** `Patches/ShopMenuPatches.cs`

### 5e. Shop Buy List Right Stick Scrolling — v2.8.18-v2.8.22
- Vanilla right stick scroll blocked at GamePad.GetState level
- Replaced with right stick jump-5-items navigation using simulated DPad presses
- Hold-to-repeat with 250ms delay, 100ms repeat rate
- Raw right stick Y cached in `GameplayButtonPatches.RawRightStickY`
- **Files:** `Patches/ShopMenuPatches.cs`, `Patches/GameplayButtonPatches.cs`

---

## CarpenterMenu / Building (v2.7.2-v3.1.44)

### 1. Robin's Building Menu Fix — v2.7.4
- **Root cause:** A button press from Robin's dialogue carries over as mouse-down state. `snapToDefaultClickableComponent()` snaps to cancel button (ID 107). When A released: `releaseLeftClick()` → `OnReleaseCancelButton()` → `exitThisMenu()`. Standard input methods (`receiveLeftClick`, `receiveKeyPress`, `receiveGamePadButton`) are NEVER called.
- **Fix:** Prefix patches on `releaseLeftClick`, `leftClickHeld`, `exitThisMenu` with 20-tick grace period.
- **File:** `Patches/CarpenterMenuPatches.cs`
- **Config:** `EnableCarpenterMenuFix`

### 1b. CarpenterMenu Joystick Panning + Cursor — v3.1.14-v3.1.21
- **Panning:** Harmony postfix on `CarpenterMenu.update(GameTime)` reads left stick, calls `Game1.panScreen()`. Pan compensation keeps cursor at same world position.
- **Visible cursor:** Harmony postfix on `CarpenterMenu.draw(SpriteBatch)` renders cursor at tracked joystick position.
- **A button:** Edge-detected in `Update_Postfix`, calls `receiveLeftClick(cursorX, cursorY)` to snap building ghost.
- **Why not direct ghost control:** Seven versions proved the building ghost on Android does NOT follow any mouse API. Ghost only moves via touch/click events. See #16d in TODO.md.
- **File:** `Patches/CarpenterMenuPatches.cs`

### 1c. CarpenterMenu All Modes — v3.1.22-v3.1.44
- Build mode: two-press A (position ghost → confirm build). Continuous GetMouseState override.
- Move mode: initial selection via `receiveGamePadButton`, then two-press for placement.
- Demolish mode: first A selects (green highlight), second A on same building confirms. Off-building A deselects.
- **Android touch simulation:** `Game1.updateActiveMenu` fires `receiveLeftClick` after every A press. With GetMouseState override active, it reads our cursor position.
- All modes use continuous GetMouseState override — ghost tracks cursor in real time.
- **Key lesson:** NEVER use direct field/type access for CarpenterMenu mode fields. PC DLL has `CarpentryAction` enum; Android uses `bool moving`/`bool demolishing`. Always use reflection.

---

## Chest & Item Management (v2.9.8-v2.9.34, v3.2.9-v3.3.13)

### 7. Console-Style Chest Item Transfer — v2.9.31
- **A** transfers full stack between chest and inventory. **Y** transfers single item.
- No selection step needed (unlike vanilla Android red-outline behavior).
- A/Y intercept in `ReceiveGamePadButton_Prefix` after side-button handler.
- Four transfer methods: full-stack and single-item in both directions.
- **File:** `Patches/ItemGrabMenuPatches.cs`
- **Config:** `EnableChestTransferFix`

### 7b. RB Snaps to Fill Stacks — v2.9.34
- RB snaps cursor to Fill Stacks button in `ReceiveGamePadButton_Prefix`. Skips when color picker open.
- **File:** `Patches/ItemGrabMenuPatches.cs`

### 10. Trash/Sort Reachable in Item Grab Menus — v2.9.8
- Sidebar buttons (Sort Chest, Fill Stacks, Color Toggle, Sort Inventory, Trash, Close X) all reachable via snap nav.
- Close X: A simulates B press + suppress-A-until-release at GetState level (v2.9.28).
- See `CHESTNAV_SPEC.md` for full navigation wiring spec.

### 13. Chest Sidebar Navigation — v2.9.8-v2.9.30
- All sidebar buttons navigable and functional.
- Color toggle opens/closes DiscreteColorPicker via direct `.visible` toggle + sound (v2.9.12).
- `receiveLeftClick` does NOT work for color toggle on Android — must toggle `.visible` directly.

### 13b. Color Picker Swatch Navigation — v2.9.14-v2.9.30
- All requirements implemented: inventory blocked while picker open, cursor snaps to first swatch, swatches wired as 7x3 grid, B closes picker only, A selects color.
- **Visual stride detection (v2.9.20-v2.9.25):** Probes picker's nearest-color hit-test at runtime. Relocates swatch bounds to visual grid positions.
- **B-closes-whole-chest fix (v2.9.29):** `exitThisMenu` prefix with same-tick guard.
- **Close X reopen fix (v2.9.28):** Suppress-A-until-release at GetState level.
- **Color preservation (v2.9.30):** Click at saved color's grid position after probe. `menu.context` is NOT a Chest on Android.
- **Files:** `Patches/ItemGrabMenuPatches.cs`, `Patches/GameplayButtonPatches.cs`

### 11. Touch Interrupt Returns Held Item — v3.2.9-v3.2.10
- In `InventoryPagePatches`, both `ReceiveLeftClick_Prefix` and `LeftClickHeld_Prefix` detect touch-during-hold and call `CancelHold()` to return item to source slot.
- **File:** `Patches/InventoryPagePatches.cs`

### 11 (Drop Zone). Drop Item Zone — v3.2.11-v3.2.13
- Invisible snap zone component (ID 110) between Sort and Trash. A while holding drops item as debris at player's feet.
- Nav wired: sort ↔ drop zone ↔ trash vertically, inventory grid ↔ drop zone horizontally.
- **File:** `Patches/InventoryManagementPatches.cs`

### 11c. Slingshot Ammo Add/Remove — v3.2.17
- Same treatment as fishing rod bait/tackle. Pick up ammo with A, Y on slingshot attaches. Y without holding detaches.
- **File:** `Patches/SlingshotPatches.cs`

### 11d. Chest Y-Transfer Strips Attachments — v3.3.12-v3.3.13
- **Root cause:** `TransferOneToChest` calls `item.getOne()` which creates bare copy without attachments.
- **Fix:** Y on loaded tool detaches attachments one at a time into chest, then transfers bare tool. Same for A button.
- Fishing rod: 1st Y = bait, 2nd Y = tackle, 3rd Y = bare rod. Slingshot: 1st Y = ammo, 2nd Y = bare slingshot.
- **File:** `Patches/ItemGrabMenuPatches.cs`

---

## Community Center Bundles (v3.2.26-v3.2.39)

### 9a. Donation Page Inventory Navigation + A-press — v3.2.26-v3.2.29
- **Root cause:** `currentlySnappedComponent` null, `inventory.currentlySnappedComponent` stuck at slot 0, mouse frozen. Ingredient slots all id=-500, neighbors=-1. Game's snap nav completely broken.
- **Fix:** Manage own cursor over inventory slots. Override `GetMouseState` on A-press (Android's `receiveLeftClick` reads `GetMouseState` internally). Draw cursor ourselves. Stale click guard for 3 ticks after page entry.
- **File:** `Patches/JunimoNoteMenuPatches.cs`

### 9b. Bundle Overview Screen — v3.2.30-v3.2.33, v3.2.37-v3.2.39
- **Root cause:** `allClickableComponents` was NULL on overview. `bundles` had components but never registered for snap nav. All neighbor IDs were -7777.
- **Fix:** Populate `allClickableComponents` from `bundles` + back button. Wire neighbor IDs using spatial nearest-in-direction algorithm. Handle all input ourselves.
- Invisible snap point in bottom-left fixed (v3.2.38). Cursor restore after donation page fixed (v3.2.37, v3.2.39).

### 9c. Donation Page — Navigate to Ingredient List — v3.2.35-v3.2.36
- Right from inventory column 5 enters ingredient zone. Left returns to inventory.
- Ingredient rows built dynamically by grouping `ingredientList` by Y coordinate.
- `ingredientList` (IDs 1000+, unique, has hoverText) is correct target — NOT `ingredientSlots` (all id=-500).
- **File:** `Patches/JunimoNoteMenuPatches.cs`

---

## Equipment & Inventory (v2.7.2+)

### 8. Equipment Slot Placement — Fixed
- `PickUpFromEquipmentSlot` handles all slot IDs (101=Hat, 102=Right Ring, 103=Left Ring, 104=Boots, 108=Shirt, 109=Pants).
- Sort button (106) handled directly. `AllowGameAPress` fallback for unknown non-inventory slots.
- **File:** `Patches/InventoryManagementPatches.cs`

### 16. Trash Can + Sort Button — v2.7.2+
- A on trash can trashes held item via `Utility.trashItem()`. A on sort button sorts inventory.
- B while holding snaps to trash can; B again cancels hold and closes menu.
- **File:** `Patches/InventoryManagementPatches.cs`

---

## Gameplay Fixes

### 3. Fishing Mini-Game Button Fix — v2.7.1
- `GameplayButtonPatches.GetState_Postfix` applies X/Y swap when `BobberBar` is active.

### 5. Cutscene Skip with Controller — v3.3.1
- Double-press Start to skip cutscenes. First press simulates touch to show skip icon, second press within 3 seconds confirms via `Event.skipEvent()` reflection.
- Edge detection via `GameplayButtonPatches.StartPressedThisTick`. 180-tick timeout.
- **File:** `ModEntry.cs`
- **Config:** `EnableCutsceneSkip`

### 6. Furniture Placement Fix — v3.1.13
- **Root cause:** Y button rapid-toggled (no debounce). Android code path: `canBeRemoved` → `performRemoveAction` → `placementAction` cycling every ~3 ticks. Beds bypass `canBeRemoved`.
- **Fix:** Suppress-until-release pattern. Block all interactions until X/Y physically released.
- **Key discoveries:** `performToolAction` NOT called. `checkForAction` only via ControllerA. `performRemoveAction` postfix unreliable for virtual methods — use prefix. `canBeRemoved` called multiple times — can't use cooldown.
- **Files:** `Patches/CarpenterMenuPatches.cs`, `ModEntry.cs`
- **Config:** `EnableFurnitureDebounce`

### 22. Analog Trigger Multi-Read (G Cloud) — v3.3.2-v3.3.11
- **Root cause:** Three code paths processed trigger input: `HandleTriggersDirectly()`, `HandleToolbarNavigation()` via SMAPI events, and native trigger code reading `GetState()` directly.
- **Fix:** Removed SMAPI event duplicate. GetState-level trigger suppression (zero analog values AND digital button flags). Dual enforcement: `OnUpdateTicking` + `HandleTriggersDirectly`. Persistent `_triggerSlotTarget`.
- **Key lesson:** `GamePadState` has BOTH analog triggers AND digital trigger button flags. Constructor does NOT auto-derive. Must strip `Buttons.LeftTrigger`/`Buttons.RightTrigger` from Buttons field when zeroing triggers.
- **Files:** `ModEntry.cs`, `Patches/GameplayButtonPatches.cs`

### 14c-animals. Animals Tab — v3.3.17
- Bounds fix + D-pad nav + A-press. A does nothing on this tab (same as console).

### 14c-social. Social Tab Navigation — v3.3.18
- Navigation implemented with scroll support. IN TESTING.

---

## Optimization

### O1. Cache Reflection in InventoryManagementPatches — v2.7.15
- `ClearHoverState()` had 4 uncached `AccessTools.Field()` lookups per call (every tick while A held).
- `TriggerHoverTooltip()` had up to 5 uncached lookups on slot change.
- **Fix:** Cache all fields as statics in `Apply()`.

### O3. Cache Reflection in HandleShopBumperQuantity — Already Done
- `QuantityToBuyField` and `InvVisibleField` cached at init in `ShopMenuPatches.Apply()` since v3.0.7-v3.0.9.
- `FillOutStacks` method cached in v3.1.3.

---

## Not Needed / Working Fine / Intentionally Different

- ~~Last shipped display~~ — Works correctly
- ~~Non-shippable items~~ — Properly greyed out
- ~~Shipping bin stacking~~ — Matches console behavior (no stacking)
- ~~Crafting menu fixes~~ — No issues reported
- ~~Menu navigation fixes~~ — No issues reported
- ~~Menu tab navigation (ZL/ZR)~~ — Confirmed working (cycling through all tabs)
- ~~Console-style shop (hold-A to buy)~~ — Intentionally kept enhanced quantity selector instead
- ~~Dialogue text completion~~ — Not reproducible. Works correctly on tested devices.
- ~~Hold-to-repeat tool actions~~ — Confirmed working

---

## Tested Controllers

| Controller | Status | Notes |
|------------|--------|-------|
| Odin Built-in | Full | All buttons + triggers work |
| Logitech G Cloud Built-in | Full | Analog triggers need Bumper Mode |
| Xbox One Wireless (Bluetooth) | Full | Triggers need Bumper Mode |
| Xbox Series X\|S Wireless (Bluetooth) | Full | Triggers need Bumper Mode |

See `CONTROLLER_MATRIX.md` for full testing details by device.

### 32. CC View Trigger/Bumper Navigation — v3.3.94-v3.3.95
- **Root cause:** Vanilla `doFromGameMenuJoystick()` treats all four buttons (LT/RT/LB/RB) as room-switch inside the CC view. Our `ReceiveGamePadButton_Prefix` in JunimoNoteMenuPatches had `default: return true` which let them all pass through. No exit path back to GameMenu.
- **Fix:** Added LT/RT cases in the overview page branch of `ReceiveGamePadButton_Prefix`. When `UseBumpersInsteadOfTriggers` is OFF: LT exits CC → GameMenu last tab (Options), RT exits CC → GameMenu first tab (Inventory). When ON: pass through to vanilla room switching.
- **v3.3.95 fix:** `GameMenu.numberOfTabs` const didn't match SMAPI mobile facade's actual tab count. Changed to create a `GameMenu()` instance and use `pages.Count - 1` for the last tab index.
- **LB/RB unchanged** — continue switching CC rooms via vanilla `doFromGameMenuJoystick`.
- **Full wrap cycle works:** Inventory →RT→ ... →RT→ Options →RT→ CC →RT→ Inventory, and reverse with LT.
- **File:** `Patches/JunimoNoteMenuPatches.cs`

### Tooltip Positioning — v3.4.30-v3.4.33
- **Root cause:** `drawToolTip` positions at `mouse+32`, bottom-edge clamping pushes tooltip over cursor on different screen sizes (Tab S8, G Cloud, Ayaneo).
- **Fix:** Replaced `drawToolTip` with `drawHoverText` using `overrideX`/`overrideY`. Tooltip placed below slot (cursor clearance + 8px gap) or above if insufficient room, using measured height.
- v3.4.30: positioned right of slot — covered rightmost column.
- v3.4.31: positioned below/above with 300px height estimate — too small for weapons.
- v3.4.32: 450px estimate — too aggressive, tooltip "way too high" for simple items.
- v3.4.33: `MeasureTooltipHeight()` computes actual height from font metrics, buff icons, category line, edibility bars, attachment slots, and item-specific overrides (`getExtraSpaceNeededForTooltipSpecialIcons`). Confirmed working on Tab S8.
- Uses reflection for `GetBuffIcons` (Android-only method).
- **Lesson:** Don't estimate tooltip heights — measure them. The game's tooltip code has many conditional sections (buffs, edibility, attachments, weapon stats) that vary wildly by item type.
- **Files:** `Patches/InventoryManagementPatches.cs`, `Patches/ItemGrabMenuPatches.cs`
