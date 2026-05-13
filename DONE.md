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

### 36. CC Bundle Donation Fix — v3.4.34-v3.4.36
- **Root cause:** Holding A on the donation page triggered touch-sim `leftClickHeld` every frame, which ran `InventoryMenu.leftClickHeld` (dragScale *= 1.075 = item swell) and `JunimoNoteMenu.leftClickHeld` ingredient slot highlighting (sourceRect = green).
- **v3.4.34:** Prefix patch on `leftClickHeld` blocks when A is physically pressed. Fixed green slots and item swell.
- **v3.4.35:** Replaced `receiveLeftClick` with `tryDepositItem()` (via reflection, Android-only) for one-press A donation instead of pick-up-then-deposit.
- **v3.4.36:** `tryDepositItem` unconditionally sets `heldItem` before checking slots. If deposit fails (wrong item), phantom cursor appeared. Fixed by saving/restoring `heldItem` in finally block.
- **File:** `Patches/JunimoNoteMenuPatches.cs`

---

## Milestone 2: Chest & Item Interaction Polish (v3.4.47 – v3.4.83)

Headline release: shop-cursor fixes, CarpenterMenu polish, CC bundle reward menu controller support, equipment tooltips. All items previously tracked as 34a-e / 40a-c / 41a-f / 42 / 43 / 44 / 45.

### #34 CarpenterMenu — Remaining Issues
- **34a (v3.4.48):** Style picker A on arrows now cycles skins instead of exiting.
- **34b (v3.4.49, v3.4.64):** Ghost centers on cursor. v3.4.64: ghost tracks cursor continuously (no two-press), zoom-correct offset, direct `_drawAtX/_drawAtY` setting.
- **34c (v3.4.64):** Dead zone in bottom-right corner — could not reproduce on TCL (zoom=1.875). Likely resolved by zoom correction in v3.4.64.
- **34d (v3.4.47):** `_overridingMousePosition` now cleared in `OnMenuClosed()`. Chest interface works after build menu.
- **34e (v3.4.64):** Building placement now works at all zoom levels. Was using unscaled offset (`tileWidth * 32`) instead of zoom-scaled (`tileWidth * 32 * zoom`). Ghost tracks cursor in real time, single A press to build.
- **File:** `Patches/CarpenterMenuPatches.cs`

### #40 Shop Cursor Fixes
- **40a (v3.4.57):** Sell tab cursor missing at Blacksmith and Joja. Root cause: `Game1.mouseCursorTransparency=0` at these shops (=1 at others). Fix: draw cursor ourselves when transparency < 0.01, at `Game1.getMouseX/Y()` (same position vanilla uses).
- **40b (v3.4.59-v3.4.62):** Buy tab cursor missing. Root cause: `drawMouse()` skipped when `SnappyMenus && !inventoryVisible`. Fix: draw cursor at `forSaleButton` matching `hoveredItem` (`getMouseX/Y` and `currentlySnappedComponent` both unreliable on Android buy tab).
- **40c (v3.4.60):** Left stick hold-to-repeat. Root cause: game's `directionKeyPolling` only fires repeat for `childMenu`/`textEntry`, not `activeClickableMenu`. Fix: track stick direction in `Update_Postfix`, 15-tick delay then 4-tick repeat matching game timing.
- **File:** `Patches/ShopMenuPatches.cs`

### #41 Community Center Bundle — Completed Bundle Issues
- **41a (v3.4.67):** Completed bundle icon now shows correct completion state on overview. Root cause: our `SyncHighlightedBundle` was calling `sprite.reset()` on all non-highlighted bundles, reverting completed bundles from frame 14 (completed icon) back to frame 0 (incomplete). Fix: skip sprite reset and hover animation for completed bundles.
- **41b (v3.4.72):** Bundle reward no longer redeemable multiple times. Root cause: `rewardGrabbed()` doesn't reliably clear `BundleRewards` on Android. Fix: when controller A opens rewards menu, save pending bundle indices. On overview re-entry, force-clear those `BundleRewards` entries and null `presentButton` before `InitOverviewNavigation` runs.
- **41c (v3.4.69):** Bundle reward gift (`presentButton`) now navigable with controller. Added `presentButton` to `allClickableComponents` in `InitOverviewNavigation` with ID 105, wired into spatial neighbor graph.
- **41d (v3.4.70):** Hover animation and tooltip now clear when navigating from a bundle to a non-bundle component (`presentButton`, area buttons). `SyncHighlightedBundle` was returning early when target wasn't a Bundle — now resets all non-complete bundle sprites and sets `highlightedBundle = -1` before returning.
- **41e (v3.4.78):** Cursor visible in reward menu. ItemGrabMenu cursor handling already applied; the fix for 41f (adding `!reverseGrab` deposit block and `behaviorOnItemGrab` callback) made the reward menu fully functional with controller.
- **41f (v3.4.76-v3.4.83):** Reward menu controller support fully working. Multiple issues fixed across versions:
  - Present disappeared on B-close: `ClearPendingRewards` force-cleared ALL `BundleRewards`. Fix (v3.4.76): track actual grabs via `rewardGrabbed` postfix, only clear grabbed indices.
  - Deposits into reward menu: our `TransferToChest` bypassed `reverseGrab` check. Fix (v3.4.78): added `!reverseGrab` guard.
  - `rewardGrabbed` callback never fired via controller: our `TransferFromChest` bypassed `behaviorOnItemGrab`. Fix (v3.4.78): invoke callback via reflection after transfers.
  - RT/LT switching to GameMenu from walked-in CC rooms: Fix (v3.4.79): added `fromGameMenu` check.
  - Y (take-one) blocked on reward menus (v3.4.83): rewards are all-or-nothing per stack. Plays cancel sound.
- **File:** `Patches/JunimoNoteMenuPatches.cs`

### #42 Equipment Slot Tooltips Missing — v3.4.63
- Android's `InventoryPage.draw()` stripped `drawToolTip` call. Added draw postfix that reads equipment directly from player data based on snapped component ID (hat=101, rings=102-103, boots=104, shirt=108, pants=109, trinkets=120+).
- **File:** `Patches/InventoryPagePatches.cs`

### #43 Sell Tooltip for Non-Object Items — v3.4.58
- Sell price tooltip now works for all item types (weapons, rings, boots). Replaced `is not Object` early return with unified price logic: `sellToStorePrice()` for `Object`, `salePrice() / 2` for everything else.

### #44 Zero-Price Items Greyed Out on Sell Tab — v3.4.58
- Items with sell price 0 (Mixed Seeds, Mixed Flower Seeds) now greyed out on sell tab via `highlightMethod` override. Vanilla's `receiveLeftClick` sell path checks `highlightItemToSell` before selling, so greying out blocks both touch-sim and gamepad sell paths.

### #45 Purchase Button on Cash Bundles (Vault) — v3.4.74
- Vault bundles have a `purchaseButton` (`myID=797`) instead of ingredient slots. Game's `doSpecificBundlePageJoystick` uses highlight-based two-step A, but we use cursor-based single-press A.
- Fix: `HandlePurchaseAPress` overrides `GetMouseState` to `purchaseButton` center, calls `releaseLeftClick`. `Draw_Prefix`/`Draw_Postfix` draw cursor at `purchaseButton` when on donation page.
- **File:** `Patches/JunimoNoteMenuPatches.cs`

---

## Nexus Feedback Release 2 (v3.5.11 – v3.6.0)

### #50 Right Stick Cursor Drift in Overworld — v3.5.12
- **Root cause:** Vanilla Android `Game1.UpdateControlInput` moves the mouse cursor by `(rightStick * thumbstickToMouseModifier)` every tick the stick is non-zero. Deltas accumulate, so a small nudge drifts the cursor many tiles away from the player; interact/sickle then target the wrong tile.
- **Fix:** `GameplayButtonPatches.GetState_Postfix` zeros `__result.ThumbSticks.Right` when `Game1.activeClickableMenu == null && Config.SuppressRightStickInOverworld`. Right stick still works in menus that need it (shop scroll, social tab, etc.).
- **Files:** `Patches/GameplayButtonPatches.cs`, `ModConfig.cs`
- **Config:** `SuppressRightStickInOverworld` (default true)

### #51 Dresser Destroying Clothes — v3.5.11
- **Root cause:** `ShippingBinPatches` matched any `ItemGrabMenu` with `reverseGrab=false`. Dressers and aquariums also opened `ItemGrabMenu` with `reverseGrab=false`, so deposits routed through shipping-bin sell handlers — silently sold clothing instead of storing it.
- **Fix:** Tightened the source check to require the menu's source object to actually be a `ShippingBin`. Storage-shop subclasses (dresser, fish tank) bypass shipping-bin handlers.
- **File:** `Patches/ShippingBinPatches.cs`

### #57 Aquarium Duplicating Fish — v3.5.13
- **Root cause:** Console-style Y (take-one) path bypassed the source container's removal hook. Aquariums use `behaviorOnItemGrab` to clear their tank state; without that callback firing, the fish stayed in the tank AND was added to inventory.
- **Fix:** Same pattern as the v3.4.78 bundle-reward fix — invoke `behaviorOnItemGrab` via reflection after the transfer. Same logic also covers other decorative-with-held-item containers.
- **File:** `Patches/InventoryManagementPatches.cs`

### #52 Quest Log via Hold-Start — v3.5.16, v3.5.31
- **v3.5.15 attempt:** Added Back/Select fallback. Didn't register on Xbox Bluetooth controllers — locked out exactly the users it was meant to help.
- **v3.5.16:** Tap Start = GameMenu (vanilla); Hold Start ≥30 ticks (~500ms) = Quest Log. State machine in `OnUpdateTicked`.
- **v3.5.31 (final):** Tap detection failed because `Game1.UpdateControlInput` reads `GamePad.GetState()` directly (Game1.cs:14266-14283) and opens GameMenu the instant Start is pressed. SMAPI's `Input.Suppress` doesn't reach this path. Fix: extend `ApplyButtonSuppression` to zero `Buttons.Start` when journal-button handling is active, and switch press/release/hold detection to poll `RawStartPressed` cached from `GetState_Postfix` before suppression.
- **Lesson:** Anywhere `Game1` reads `GamePad.GetState()` directly, SMAPI input suppression is invisible. Suppress at GetState level instead.
- **Files:** `ModEntry.cs`, `Patches/GameplayButtonPatches.cs`
- **Config:** `EnableJournalButton` (default true)

### #53 Bed Bouncing Between Placed and Picked Up — v3.5.34, v3.5.35
- Three-version chain. Each fix exposed the next layer.
- **v3.5.14 attempt:** Patched `BedFurniture.canBeRemoved` directly (the `Furniture` base patch missed the override that didn't call base). Did not work — bed pickup doesn't always go through `canBeRemoved`.
- **v3.5.34 attempt:** Gated `Furniture.performRemoveAction` on the suppress flag. Skipped the cleanup work but the caller — `GameLocation.removeQueuedFurniture` (GameLocation.cs:7974) — still ran `furniture.Remove(guid)` on the next line, removed the bed from the world, and added it back to inventory. Bed bounced.
- **v3.5.35 fix:** Patched `GameLocation.removeQueuedFurniture` itself with a prefix that returns false when the queued GUID maps to a `BedFurniture` and the suppress flag is set. The whole cascade — `performRemoveAction` + `furniture.Remove(guid)` + inventory.Add — is skipped atomically.
- **Open question (carried into 4.x):** Why does `removeQueuedFurniture` even fire for the just-placed bed when our `canBeRemoved` returned false? Two plausible causes: (a) Farm Type Manager's `HarmonyPatch_DisableFurniturePickup` postfix on `Furniture.canBeRemoved` overwrites our `__result` back to true after our prefix runs; (b) a stale `BedFurniture.AttemptRemoval` mutex callback from before pickup fires later. Both hypotheses are blocked by patching `removeQueuedFurniture`, so the v3.5.35 gate is correct regardless.
- **Diagnostic chain:** v3.5.33 added always-on `[Bed]` and `[StartHold]` log lines, v3.5.36 added bed coordinates, v3.5.37 added per-tile `canBePlacedHere` results — combined trace from one Odin test session was decisive in moving the gate from `performRemoveAction` to `removeQueuedFurniture`.
- **Files:** `Patches/CarpenterMenuPatches.cs`
- **Config:** `EnableFurnitureDebounce` (default true)

### Storage Shop Polish (#1, follow-ups #51 / #57) — v3.5.22 – v3.5.30
- **Sell tab `highlightAllItems`** (v3.5.23): forced `highlightItem.Equals(item) = true` so all items appear sellable on the sell tab regardless of restrictive `highlightItemToSell` overrides. Without this, some clothing greyed out when nothing should.
- **Vanilla deposit restrictions** (v3.5.26): per user testing on PC — dresser sell tab accepts only clothing/hat/boots/ring; aquarium sell tab accepts nothing (deposits via held-fish + world A only). Use vanilla `highlightItemToSell`, don't override.
- **Re-snap after `rebuildSaleButtons`** (v3.5.25): when dresser/aquarium nav rebuilds the sale buttons mid-frame, `currentlySnappedComponent` becomes stale. Re-snap to the equivalent slot index after rebuild.
- **`hoveredItem` refresh after rebuild** (v3.5.27 – v3.5.29): tried three approaches (snap restore, `setCurrentItem`, `currentlySelectedItem`). Final: refresh via `currentlySelectedItem`, the Android-correct path.
- **Default buy-tab selection to row 0** (v3.5.30): when `forSale` was empty (e.g. after sell-tab restock), `currentItemIndex` could land on a non-existent row. Snap to row 0 to keep the cursor visible.
- **File:** `Patches/ShopMenuPatches.cs`

### Chest / Equipment / Shop / Fishing Plumbing — v3.5.18 – v3.5.21
- **#58 chest deposits via `behaviorFunction`** (v3.5.18): bundle/quest hooks now get the deposit event.
- **#59 equipment via `Farmer.Equip`** (v3.5.19) + **#9 equipment take-off via `Farmer.Equip` + grab-replacement** (v3.5.24): equip slots had three separate code paths; now all go through the game's own equip routine so callbacks (e.g. set-bonus changes) fire.
- **#60 shop stock + `ActionsOnPurchase`** (v3.5.20): runs the post-purchase actions and synchronizes stock counts the way vanilla does.
- **#61 fishing rod tackle via `Tool.attach`** (v3.5.21): attach routine called the same way vanilla does, so tackle effects (trap bobber, magnet) apply.
- **Files:** `Patches/ItemGrabMenuPatches.cs`, `Patches/InventoryManagementPatches.cs`, `Patches/ShopMenuPatches.cs`, `Patches/FishingRodPatches.cs`

### Auto-Grabber Block — v3.5.32
- **Root cause:** Console-style A/Y deposits routed items into the Auto-Grabber's loot grid as if it were a chest. Auto-Grabber expects to OWN its contents (it grabs from connected machines and drops with right-click); player deposits broke that contract.
- **Fix:** Detect Auto-Grabber by chest type at the deposit gate and refuse the transfer.
- **File:** `Patches/InventoryManagementPatches.cs`

### Console Furniture Placement — v3.5.38, v3.5.39
- **Root cause of the UX confusion:** Vanilla Android `Object.drawPlacementBounds` falls through to a multi-tile green/red map when `Game1.options.weaponControl` doesn't match values 2/3/4/8 (which are touch-UI specific). The map shows every tile where the furniture's *top-left can land* — for a 2x3 bed that's a narrow region offset from where the bed visually sits, so users aim at where they expect the bed and see only red. v3.5.37 diagnostic logs from the user's bedroom confirmed this exactly: bed at TileLocation=(9,8), green tiles clustered at columns 1-3 above row 8.
- **Fix (v3.5.38):** Harmony prefix on `Object.DrawRedGreenRectangleForPlacing` (mobile-only — resolved by string with `AccessTools.Method` so the project compiles against the PC `StardewValley.dll`). For `Furniture` instances when `EnableConsoleFurniturePlacement` is true, draws a single colored rectangle at `__instance.TileLocation` sized via `getTilesWide()`/`getTilesHigh()` and returns true. The early-return at `Object.cs:5234` then short-circuits the multi-tile map.
- **Refinement (v3.5.39):** After the colored squares, call `__instance.draw(spriteBatch, tileX, tileY, 0.5f)` to render the actual furniture sprite translucently on top. SpriteBatch is in Deferred mode, so call order = render order — squares first, sprite on top with alpha showing the validity highlight through it. Matches console behaviour and the carpenter building ghost.
- **Engine artifact reused:** The single-rectangle path was already in `Object.DrawRedGreenRectangleForPlacing` for tap/touch; we just bypass the `weaponControl` gate for controller-driven placement.
- **File:** `Patches/FurniturePlacementPatches.cs` (new)
- **Config:** `EnableConsoleFurniturePlacement` (default true)

---

## Bug Fix Release 2 (v3.6.1 – v3.7.0)

### Build Fix: Legacy Test Folder Exclusion — v3.6.1
- **Problem:** The untracked, pre-rename `AndroidControllerFix.Tests/` folder (Feb 2026, holds its own xUnit `.csproj`) was being pulled into the main project's SDK auto-discovery, breaking `dotnet build AndroidConsolizer.csproj` with 206 `Fact`/`FactAttribute` errors. v3.6.0 must have been built with stale obj/ caches; clean rebuild fails.
- **Fix:** Added `<DefaultItemExcludes>$(DefaultItemExcludes);AndroidControllerFix.Tests\**</DefaultItemExcludes>` to `AndroidConsolizer.csproj`. The legacy test project's files no longer get compiled into the main mod assembly.
- **File:** `AndroidConsolizer.csproj`

### #49 Nexus Reply — Furniture Debounce Toggle Availability (no version bump)
- Replied to a v2.0.0 user who was running the Switch Controls mod alongside Android Consolizer and asked about cherry-picking just the furniture-debounce fix. Pointed them at `EnableFurnitureDebounce` (separate toggle since v3.2.0) and `EnableButtonRemapping: false` (added v3.5.0) so they can drop the A/B X/Y swap layer without losing the rest.
- **Comms-only.** No code change.

### #64 Diagnostic Logging Cleanup — v3.6.2
- **Source:** v3.5.33, v3.5.36, v3.5.37 added always-on Info-level `[Bed]`, `[StartHold]`, `[Bed] canBePlacedHere` logs while debugging bed-bouncing (#53) and journal-button hold (#52). Those root causes are now resolved; the logging just clutters every furniture interaction and Start press.
- **Fix:** All `[Bed]` and `[StartHold]` calls in `CarpenterMenuPatches.cs`, `GameplayButtonPatches.cs`, and `ModEntry.cs` downgraded `LogLevel.Info` → `LogLevel.Debug` and gated on `Config.VerboseLogging`. Same pattern as the pre-existing non-bed `[Furniture] BLOCKED ...` lines (line 1524 of `CarpenterMenuPatches.cs` was already this pattern).
- **Kept at Info:** Patch-attach confirmations (`[Bed] BedFurniture.canBeRemoved patch attached`, `... canBePlacedHere diagnostic postfix attached`, `... removeQueuedFurniture patch attached`) — one-time at mod load, useful when triaging "is my patch even attached?"
- **Kept at Warn:** The matching `AccessTools.Method(...) returned null` failure lines for each of the three Bed patches.
- **Files:** `ModEntry.cs`, `Patches/CarpenterMenuPatches.cs`, `Patches/GameplayButtonPatches.cs`

### #63 Pickup Steers Into the Active Toolbar Row — v3.6.3, v3.6.4, v3.6.5
- **Source:** User noted in passing during v3.5.35 testing — "the bed pops into the first available inventory slot in the entire inventory, not on the currently visible row." Reproduced 2026-05-13: on row 2, bed landed at row 0 slot 5 while `CurrentToolIndex` got bounced to the same-column slot in row 2 by the toolbar row lock. Two slots looked the same but weren't.
- **Root causes:**
  - `GameLocation.removeQueuedFurniture` (Android decompile GameLocation.cs:7974) scans only slots 0-11 for a null and sets `CurrentToolIndex = i` (still in row 0). Hard-coded loop bound.
  - `Farmer.addItemToInventory(Item, List<Item>)` (Farmer.cs:4240) scans `0..maxItems` for the first null, so pickups always pile into row 0 regardless of the player's active row. Same root pattern, broader surface (forage, drops, gifts, shop purchases).
- **Fix (split across three patches, gated on `Config.EnablePickupToActiveRow`, default true):**
  - **v3.6.3:** Add `EnablePickupToActiveRow` to `ModConfig` + GMCM toggle. No behavior change — just scaffolding.
  - **v3.6.4:** Furniture postfix on `removeQueuedFurniture`. Extended the existing prefix with `out Furniture __state` to capture the just-placed furniture (the body removes it from the location's furniture dict, so postfix needs the reference up front). If active row has a null → swap furniture there + `CurrentToolIndex` follows. If active row is full → leave furniture wherever the game put it AND update `currentToolbarRow` + `CurrentToolIndex` to its slot, so the player can immediately re-place. Matches vanilla "auto-select on pickup" semantic.
  - **v3.6.5:** Non-furniture postfix on `Farmer.addItemToInventory(Item, List<Item>)`. If item ref findable in `player.Items` (i.e., not stack-merged) and outside active row, swap into a null slot in the active row. **Never touches `CurrentToolIndex` or `currentToolbarRow`** — pickups must never disrupt your selected tool. Active-row-full → vanilla wins.
- **Key UX distinction:** Furniture is your held tool until you place it again, so it needs to be selected. Forage, drops, gifts, shop purchases never disrupt your current tool.
- **Plumbing:** Added `ModEntry.SetCurrentToolbarRow(int)` static helper backed by a singleton `_instance` captured in `Entry()`, so static patch helpers can update the row without reflection.
- **Files:** `ModConfig.cs`, `ModEntry.cs`, `Patches/CarpenterMenuPatches.cs`, `Patches/FarmerPatches.cs`
- **Config:** `EnablePickupToActiveRow` (default true)

### #54 Trigger Column-Skip on Gamesir / G Cloud — v3.6.6
- **Reporters:** Nexus user with Gamesir X2 (6 Mar 2026, "occasionally skipping 2 slots") + Gamesir X5 Lite user (17 Feb 2026, "still jumps 2 slot" after v3.3.11). Author has both Gamesir X2 + G Cloud and reproduced.
- **Diagnostic data (v3.6.5 baseline, G Cloud):** Of 103 trigger edges in a 30-second test, **~37% were spurious**:
  - 15 dropout-bounce (LT held at 1.0 but value briefly dropped to 0.01 for 1-3 ticks mid-pull — each dropout flipped `wasLeftTriggerDown=false`, so when value returned the edge detector fired again. The single physical pull registered as two slot moves.)
  - 23 threshold-flutter (analog value oscillated around the 0.5 threshold during slow pulls)
- **Root cause:** Single-bool edge detector (`isLeftTriggerDown = leftTrigger > 0.5`, fire on `!wasLeftTriggerDown && isLeftTriggerDown`) has no protection against signal bounce or short dropouts. Hall-effect triggers don't have mechanical detents, so the analog signal can briefly drop to zero even while the user is physically holding the trigger.
- **Fix:** Replaced `wasLeftTriggerDown/wasRightTriggerDown` booleans with a two-threshold state machine + per-side release-confirmation streak:
  - **Press threshold 0.5** — rising edge enters pressed state, fires move
  - **Release threshold 0.15** — hysteresis absorbs threshold-flutter (values oscillating between 0.3 and 0.7 stay pressed)
  - **Release confirmation 4 ticks** — must see 4 consecutive ticks below release threshold to enter released state; 1-3 tick dropouts can't flip it
- **Verification:**
  - G Cloud (analog hall-effect): 37% spurious → 18% spurious. Threshold-flutter went 22% → 7% (75% reduction); dropout-bounce went 15% → 12%. Some dropouts last 5+ ticks and still leak through — acceptable since they're rare and unverified-to-perceive.
  - Gamesir X2 + Galaxy S26 Ultra: 65 edges, **0% spurious**. Note: X2 reports pure binary 0.0/1.0 (it's **digital**, not analog — only the X2 Pro is the analog hall-effect model). Hysteresis is dead weight on digital lines but release-confirmation absorbs single-tick electrical glitches.
- **Same root pattern probably explains the X5 Lite reporter's "still jumps 2 slot" complaint after v3.3.11** — that controller is also digital and would have been hit by single-tick dropouts on the digital line.
- **Files:** `ModEntry.cs`

### Diagnostic: CurrentToolIndex Setter Logging — v3.6.7
- **Source:** Investigating a one-off observation during #63 testing where the toolbar cursor appeared to jump 2 slots on the FIRST trigger pull after save load. Existing `CurrentToolIndex_Prefix` only logged when AC modified the value — silent pass-through writes weren't traced. Couldn't be reproduced in subsequent sessions, but the diagnostic stays in place to catch it if it recurs.
- **What it does:** `FarmerPatches.CurrentToolIndex_Prefix` now logs every setter call where `value != current` on the main player (gated on `VerboseLogging`). Includes old/new values, tick, `Game1.activeClickableMenu` type, `Context.IsPlayerFree`, and a 6-frame stack trace excerpt that names the caller.
- **Confirmed behaviors in capture:** `OnSaveLoaded` re-equip cycle (`0 → 1 → 0`), `SaveGame.getLoadEnumerator` setting `-1` momentarily during load, every `HandleTriggersDirectly` press edge.
- **No behavior change.** Just observability.
- **Files:** `Patches/FarmerPatches.cs`
