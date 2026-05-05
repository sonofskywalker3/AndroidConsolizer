# AndroidConsolizer - TODO

**Read this file before starting any bug fix or feature.** Each item has implementation notes, root cause analysis, and file references.

For completed features and their technical reference, see `DONE.md`.

---

## Milestone 1: GameMenu Tabs (v3.4) — COMPLETE

All items done. See `DONE.md` and `.planning/STATE.md` quick tasks table.

---

## Milestone 2: Chest & Item Interaction Polish (v3.5) — COMPLETE

### 34. CarpenterMenu — Remaining Issues
- **34a. FIXED (v3.4.48)** — Style picker A on arrows now cycles skins instead of exiting.
- **34b. FIXED (v3.4.49, v3.4.64)** — Ghost centers on cursor. v3.4.64: ghost tracks cursor continuously (no two-press), zoom-correct offset, direct _drawAtX/_drawAtY setting.
- **34c. FIXED (v3.4.64)** — Dead zone in bottom-right corner. Could not reproduce on TCL (zoom=1.875). Likely resolved by zoom correction in v3.4.64.
- **34d. FIXED (v3.4.47)** — `_overridingMousePosition` now cleared in OnMenuClosed(). Chest interface works after build menu.
- **34e. FIXED (v3.4.64)** — Building placement now works at all zoom levels. Was using unscaled offset (tileWidth * 32) instead of zoom-scaled (tileWidth * 32 * zoom). Ghost tracks cursor in real time, single A press to build.
- **File:** `Patches/CarpenterMenuPatches.cs`

### 40. Shop Cursor Fixes
- **40a. FIXED (v3.4.57)** — Sell tab cursor missing at Blacksmith and Joja. Root cause: `Game1.mouseCursorTransparency=0` at these shops (=1 at others). Fix: draw cursor ourselves when transparency < 0.01, at `Game1.getMouseX/Y()` (same position vanilla uses).
- **40b. FIXED (v3.4.59-v3.4.62)** — Buy tab cursor missing. Root cause: `drawMouse()` skipped when `SnappyMenus && !inventoryVisible`. Fix: draw cursor at `forSaleButton` matching `hoveredItem` (getMouseX/Y and currentlySnappedComponent both unreliable on Android buy tab).
- **40c. FIXED (v3.4.60)** — Left stick hold-to-repeat. Root cause: game's `directionKeyPolling` only fires repeat for `childMenu`/`textEntry`, not `activeClickableMenu`. Fix: track stick direction in Update_Postfix, 15-tick delay then 4-tick repeat matching game timing.
- **File:** `Patches/ShopMenuPatches.cs`

### 41. Community Center Bundle — Completed Bundle Issues
- **41a. FIXED (v3.4.67)** — Completed bundle icon now shows correct completion state on overview. Root cause: our `SyncHighlightedBundle` was calling `sprite.reset()` on all non-highlighted bundles, reverting completed bundles from frame 14 (completed icon) back to frame 0 (incomplete). Fix: skip sprite reset and hover animation for completed bundles.
- **41b. FIXED (v3.4.72)** — Bundle reward no longer redeemable multiple times. Root cause: `rewardGrabbed()` doesn't reliably clear `BundleRewards` on Android. Fix: when controller A opens rewards menu, save pending bundle indices. On overview re-entry, force-clear those `BundleRewards` entries and null presentButton before `InitOverviewNavigation` runs.
- **41c. FIXED (v3.4.69)** — Bundle reward gift (presentButton) now navigable with controller. Added presentButton to `allClickableComponents` in `InitOverviewNavigation` with ID 105, wired into spatial neighbor graph.
- **41d. FIXED (v3.4.70)** — Hover animation and tooltip now clear when navigating from a bundle to a non-bundle component (presentButton, area buttons). `SyncHighlightedBundle` was returning early when target wasn't a Bundle — now resets all non-complete bundle sprites and sets `highlightedBundle = -1` before returning.
- **41e. FIXED (v3.4.78)** — Cursor visible in reward menu. ItemGrabMenu cursor handling already applied; the fix for 41f (adding `!reverseGrab` deposit block and `behaviorOnItemGrab` callback) made the reward menu fully functional with controller.
- **41f. FIXED (v3.4.76-v3.4.83)** — Reward menu controller support fully working. Multiple issues fixed across versions:
  - Present disappeared on B-close: `ClearPendingRewards` force-cleared ALL BundleRewards. Fix (v3.4.76): track actual grabs via `rewardGrabbed` postfix, only clear grabbed indices.
  - Deposits into reward menu: our `TransferToChest` bypassed `reverseGrab` check. Fix (v3.4.78): added `!reverseGrab` guard.
  - `rewardGrabbed` callback never fired via controller: our `TransferFromChest` bypassed `behaviorOnItemGrab`. Fix (v3.4.78): invoke callback via reflection after transfers.
  - RT/LT switching to GameMenu from walked-in CC rooms: Fix (v3.4.79): added `fromGameMenu` check.
  - Y (take-one) blocked on reward menus (v3.4.83): rewards are all-or-nothing per stack. Plays cancel sound.
- **File:** `Patches/JunimoNoteMenuPatches.cs`

### 45. FIXED (v3.4.74) — Purchase Button on Cash Bundles (Vault)
- Vault bundles have a `purchaseButton` (myID=797) instead of ingredient slots. Game's `doSpecificBundlePageJoystick` uses highlight-based two-step A, but we use cursor-based single-press A.
- Fix: `HandlePurchaseAPress` overrides GetMouseState to purchaseButton center, calls `releaseLeftClick`. Draw_Prefix/Draw_Postfix draw cursor at purchaseButton when on donation page.
- **File:** `Patches/JunimoNoteMenuPatches.cs`

### 42. FIXED (v3.4.63) — Equipment Slot Tooltips Missing
- Android's `InventoryPage.draw()` stripped `drawToolTip` call. Added draw postfix that reads equipment directly from player data based on snapped component ID (hat=101, rings=102-103, boots=104, shirt=108, pants=109, trinkets=120+).
- **File:** `Patches/InventoryPagePatches.cs`

### 43. FIXED (v3.4.58) — Sell Tooltip for Non-Object Items
- Sell price tooltip now works for all item types (weapons, rings, boots). Replaced `is not Object` early return with unified price logic: `sellToStorePrice()` for Object, `salePrice() / 2` for everything else.

### 44. FIXED (v3.4.58) — Zero-Price Items Greyed Out on Sell Tab
- Items with sell price 0 (Mixed Seeds, Mixed Flower Seeds) now greyed out on sell tab via `highlightMethod` override. Vanilla's `receiveLeftClick` sell path checks `highlightItemToSell` before selling, so greying out blocks both touch-sim and gamepad sell paths.

---

## Nexus Feedback Release 1

### 48. Xbox/PS Layout — Y Button Overlap in Chests and Inventory
- **Reporter:** Nexus user, tested on v3.3 and v3.4
- **Bug:** On Xbox/PS layout, pressing Y (top physical button) both picks up a single item from a stack AND sorts the inventory/chest. The two actions fire simultaneously, so every single-item pickup also rearranges the container.
- **Root cause hypothesis:** The button remapper maps the top physical button based on layout position. On Switch, top = X (sort); on Xbox, top = Y (single-item). But if the remapping causes the raw button to hit one code path and the remapped button to hit another, both actions fire on the same press. Or the sort handler fires on the raw button while the transfer handler fires on the remapped button.
- **Investigation needed:**
  1. Trace what `ButtonRemapper.RemapButton()` returns for physical Y on Xbox layout
  2. Check if the vanilla game's `receiveGamePadButton` also processes the raw button after our prefix returns false
  3. Check if `GetState_Postfix` X/Y swap interferes — the swap happens at hardware level and the `receiveGamePadButton` prefix sees the already-swapped button
  4. Test: does disabling `EnableButtonRemapping` fix the overlap?
- **Files:** `ButtonRemapper.cs`, `Patches/GameplayButtonPatches.cs`, `Patches/ItemGrabMenuPatches.cs`, `Patches/InventoryPagePatches.cs`

### 49. Respond to v2.0.0 User — Furniture Debounce Availability
- **Reporter:** Nexus user on v2.0.0, using Switch Controls mod separately
- **Request:** Wants furniture debounce without button remapping. Staying on v2.0.0 to avoid conflicts with their existing Switch Controls mod.
- **Status:** Already solved in current version. `EnableFurnitureDebounce` has been a separate toggle since v3.2.0, and `EnableButtonRemapping` was added in v3.5.0 to disable A/B and X/Y swaps independently.
- **Action:** Reply on Nexus explaining they can upgrade to v3.5.0 and set `EnableButtonRemapping: false` to get just the fixes without interfering with their Switch Controls mod. All features are individually toggleable via GMCM or config.json.

---

## Nexus Feedback Release 2

Pulled from Nexus comments + bug reports on 2026-05-05.

### 50. FIXED (v3.5.12) — Right Stick Targets Distant Objects in Overworld
- **Reporter:** Nexus user (DM), tested against multiple controller mods — confirms issue is specific to AndroidConsolizer.
- **Bug:** Moving the right joystick during gameplay drives an invisible cursor that targets objects far away (a dozen+ tiles). Standing next to a tomato, a nudge of the right stick can latch onto a pepper across the field. Then pressing the interact button harvests the wrong crop, or the sickle refuses to harvest the crop the player is actually facing.
- **Why this matters:** Silently breaks core gameplay (harvesting, tool use). User cannot tell where the cursor is pointing because nothing draws it.
- **Root cause hypothesis:**
  1. We do not yet have an intentional right-stick cursor (#12 is still TODO), so something is unintentionally feeding right-stick motion into mouse position.
  2. Likely candidates: leftover `Game1.setMousePosition` / `GetMouseState` override from CarpenterMenu/JunimoNoteMenu work leaking into the overworld code path.
  3. Could also be `Mouse.SetPosition`-style call in `GameplayButtonPatches` or `ButtonRemapper` reading right-stick axis.
  4. Vanilla Android may also map right stick to cursor; if so we may be amplifying it (e.g. doubling the delta) rather than originating it.
- **Investigation steps:**
  1. Grep the codebase for `setMousePosition`, `Mouse.SetPosition`, `_overridingMousePosition`, `RightThumbstick` to find any code path that runs in overworld context (not gated to a menu).
  2. Build a diagnostic patch: log `Game1.getMousePosition()` every tick when no menu is open, alongside the right-stick X/Y values, and see whether stick motion correlates with cursor jumps.
  3. Test with `EnableRightStickCursor`-style toggles disabled — if the bug persists with all our right-stick code paths gated off, it is vanilla Android behavior and we need to dampen it.
- **Files to check first:** `Patches/GameplayButtonPatches.cs`, `Patches/CarpenterMenuPatches.cs` (cleanup of `_overridingMousePosition` in `OnMenuClosed`), `ModEntry.cs` (UpdateTicked handlers), `ButtonRemapper.cs`.

### 51. FIXED (v3.5.11) — Dresser Sold Clothes Instead of Storing
- **Reporters:** TWO Nexus users — initial report 3 Mar 2026, confirmed by second user 11 Mar 2026 ("This happens to me as well… I accidentally sold a few clothes, but got them back with the android version of the item spawner mod").
- **Bug:** Interacting with a dresser triggers shipping-bin behavior — clothes get *sold* instead of stored. Silent data loss.
- **Root cause hypothesis:** `ShippingBinPatches` is matching too broadly. Dresser is likely an `IslandFurniture` / `StorageFurniture` subclass that shares some interface or check with shipping bin. Our patch may be hitting `ItemGrabMenu` for any reverseGrab=false menu that has a parent matching some condition we used for shipping bin.
- **Investigation steps:**
  1. Decompile `Furniture.checkForAction` / `StorageFurniture.checkForAction` on Android — confirm what menu the dresser opens and whether it shares a base class with shipping bin.
  2. Audit `ShippingBinPatches.cs` prefixes — what context check do they use? Tighten so the patch only fires when source is actually a `ShippingBin`.
- **Files:** `Patches/ShippingBinPatches.cs`, possibly `Patches/ItemGrabMenuPatches.cs`.

### 52. FIXED (v3.5.15) — Quest Log Lockout (Back/Select Fallback)
- **Reporter:** Nexus user, 15 Mar 2026.
- **Bug:** If the user removes the "open quests" binding from the Start button (via remap/config), there is no other way to open the quest log with a controller.
- **Fix:** Add a fallback chord (e.g. Select+Start, or a GMCM-configurable button) that always opens the quest log regardless of the Start binding. Or simply prevent the user from unbinding it without a replacement.
- **Files:** `ModEntry.cs` (button event handling), GMCM config registration.

### 53. FIXED (v3.5.14) — Bed Furniture Debounce
- **Reporters:** 17 Feb 2026 + 15 Mar 2026 ("the fix for furniture placement seems to be fixed for the carpet but not for the bed").
- **Bug:** Pressing X (place/pickup) on a bed places-and-immediately-picks-up in a loop. Carpet works fine. Disabling debounce in mod settings makes the bed un-placeable entirely.
- **Why this is its own item:** `BedFurniture` is documented in MEMORY.md as bypassing `canBeRemoved` entirely — only `performRemoveAction` / `placementAction` fire. The current debounce flow is presumably gating on `canBeRemoved`, which is why beds slip through.
- **Fix approach:** Extend the suppress-until-release flag to fire from `BedFurniture.performRemoveAction` and `placementAction` prefixes specifically (in addition to the existing `Furniture` prefixes). Verify it does not break placement of regular beds in the bedroom (only the in-world pickup/replace cycle).
- **Files:** `Patches/CarpenterMenuPatches.cs` if furniture handling lives there, or wherever the debounce-until-release flag is defined (search for `SuppressFurnitureUntilRelease` or similar).

### 54. Trigger Column-Skip Still Occurring on Gamesir X2 (v3.3.11 fix incomplete)
- **Reporter:** Nexus user (Gamesir X2 controller), 6 Mar 2026.
- **Bug:** Despite the v3.3.11 hall-effect trigger fix, this user still sees "the triggers are occasionally skipping 2 slots."
- **Status:** Partial fix exists. The original report was on G Cloud (analog hall-effect triggers) — Gamesir X2 also has hall-effect triggers, so the same code path applies but is not fully reliable.
- **Investigation steps:** Re-check the trigger debounce window. Look for whether the v3.3.11 fix is gated on a value (e.g. analog threshold) that does not match Gamesir X2's resting/peak readings. May need a SMAPI log from this user to see actual trigger values.

### 55. VERIFIED (v3.5.15) — Release Zip Is Clean
- v3.5.15 zip contains 3 entries: AndroidConsolizer.dll, AndroidControllerFix.dll (legacy), manifest.json. No logs, no images, no junk. ModBuildConfig defaults are filtering correctly. Issue was self-resolved between v3.5.4 and present.

### 56. Random Freeze During Gameplay — Amazon Luna Controller
- **Reporter:** Nexus user, 16 Feb 2026, confirmed during gameplay 26 Feb 2026 ("4:10pm during gameplay").
- **SMAPI log:** https://smapi.io/log/987136a58b4e4936a3c8a77e418369f5
- **Action:** Pull the log, look for the last entries before freeze, look for our patches in the stack or recurring exceptions.

### 57. FIXED (v3.5.13) — Aquarium Duplicate Fish (Bug #1058739)
- **Reporter:** easton777 on Nexus, 25 Mar 2026, against v3.5.10.
- **Bug:** Pressing X (take-one) on the first fish placed in an aquarium gives the player the fish but leaves the original in the aquarium — duplication. Same behavior for decorative objects placed outside (example given: seasonal plant pot).
- **Root cause hypothesis:** Our X-button take-one path is calling something like `TransferFromChest` / item-pickup that does not invoke the source container's removal hook. This mirrors the v3.4.78 bundle-reward fix where `behaviorOnItemGrab` had to be invoked via reflection. Aquariums and outdoor decoration containers likely have an analogous "remove from world" callback that is being skipped.
- **Files:** `Patches/InventoryManagementPatches.cs` (X-button take-one flow), `Patches/ItemGrabMenuPatches.cs`.

### 27 (cross-ref). Toolbar Resize Request — Bug #1050718
- Nexus user throyiii filed Bug #1050718 ("Can't resize the toolbar") against v3.5.10 on 5 Mar 2026.
- This maps directly onto existing TODO #27 (Toolbar Size Slider in Options Menu). No new item needed; bumping priority since there is now a public bug report.

---

## Milestone 3: Overworld Cursor & Accessibility (v3.6) ← ACTIVE

### 12. Right Joystick Cursor Mode + Zoom Control
- **Bundled feature (LARGE)**
- **Cursor mode:** Right joystick moves free cursor in menus + gameplay.
  - Essential for precise furniture placement, museum donations.
  - On Switch: right stick moves cursor, disappears after inactivity. Press for left click.
  - Implementation: Read right stick axis from `GamePad.GetState()`, call `Game1.setMousePosition()` per tick.
  - Complexity in behavior: dead zones, acceleration curves, auto-hide, interaction with snap navigation.
- **Zoom control:** Add to in-game Options page (not GMCM).
  - CONFIRMED: Zoom slider does NOT exist on Android — mobile port stripped it (pinch-to-zoom only).
  - On console: `whichOption = 18`, `OptionsSlider`, range 75%-200%, controls `Game1.options.desiredBaseZoomLevel`.
  - Need to inject custom `OptionsSlider` subclass into `OptionsPage.options` list.
  - Must subclass `OptionsSlider` with own value management since game's zoom handling may not be wired on Android.
  - GMCM's "Mod Options" button partially cut off — our slider injection may need to fix scroll bounds.

### 18. Museum Donation Menu
- Controller-only placement inaccessible. **Confirmed on G Cloud.** Touch required to select/place items.
- Likely requires Right Joystick Cursor Mode (#12) since museum uses free-placement grid.
- Alternative: snap-based item selection overlay, but freeform grid doesn't map well to snap nav.

### 19. Geode Breaking Menu
- Partially works but no visual feedback.
- Geode highlighted in inventory but does NOT visually move to anvil. Pushing up invisibly moves to anvil area, A cracks it. Functional but unintuitive.
- **Fix approach:** Apply inventory-style patches on `GeodeMenu`. A-button to select geode, visual feedback of geode moving to anvil, A on anvil to crack.

---

## Milestone 4: Dialogue & Interaction Polish (v3.7)

### 39. Monster Eradication Tracking Page
- No cursor visible on the monster eradication goals page (Adventurer's Guild tracking).
- Can't switch pages with controller.
- **Investigation needed:** Check which menu class this is, how pages are structured, what navigation exists.

### 22b. Dialogue Option Box — Counter-Intuitive Initial Selection
- Nothing visually selected when dialogue choice box appears. Pressing down selects TOP option; pressing up selects BOTTOM option.
- **Root cause (hypothesis):** Game initializes with invisible slot above/below visible options (index -1 or null). Navigation wraps around.
- **Fix approaches:**
  1. Start with top option pre-selected (convenient but risks accidental selection). Check if console pre-selects.
  2. Invisible start position BETWEEN options — up=top, down=bottom. Safest.
  3. Disable wrap-around on first input only.
- **Investigation needed:** Find dialogue choice class, check selection index init, check console behavior.

### 16. Trash Can Lid Animation (Cosmetic)
- Lid doesn't animate on controller hover. Extensive investigation — no solution found.
- **What was tried:**
  1. `performHoverAction()` with trash can coords — game's own `performHoverAction` fights it
  2. `Game1.setMousePosition()` — breaks cursor display
  3. Reflection: set `trashCanLidRotation` — field IS set (confirmed PI/2) but visual doesn't change
  4. Reflection in PREFIX on `InventoryPage.draw()` — same result
  5. Draw lid sprite ourselves in POSTFIX — no visual change
- **Hypothesis:** Android port renders through different code path or mobile-specific overlay covers lid area.
- **Next steps:** Dump all draw calls, try layer depth 1.0f, check if `trashCan.draw(b)` draws both base AND lid on Android.

### 16d. CarpenterMenu Direct Ghost Control (Lowest Priority)
- Ghost only moves via touch/click, not joystick. Seven versions tried (v3.1.14-v3.1.20). Current A-button-tap approach works.
- **Hypothesis:** Android stores ghost position from last touch event in internal field, bypassing all mouse APIs.
- **To investigate someday:** Decompile `CarpenterMenu.draw()` on Android, find ghost position field.

### 17. Title/Main Menu Cursor Fix
- Cursor should be visible on the Load button by default when the main menu loads, instead of being invisible until the player moves the stick or presses a button.
- **Investigation needed:** Check `TitleMenu` class, initial `currentlySnappedComponent`, cursor visibility logic.

### 35. Load Game Screen Cursor/Navigation
- Cursor should start on the top save slot when the Load Game menu opens, instead of in the free space below the saves.
- Navigation has issues (details TBD — needs investigation).
- **Investigation needed:** Check `LoadGameMenu` class, initial snap position, navigation wiring.

---

## Milestone 5: Combat & Tools (v3.8)

### 25. Tool Charging Broken While Moving
- Holding tool button while moving rapid-fires single uses instead of charging. Player stops moving and tool keeps firing.
- **Expected (console):** Holding tool button while moving begins charging. Player hops one square at a time.
- **NOT mod-caused.** Occurs regardless of layout. Android port difference.
- **Root cause hypothesis:** Android's tool-use code checks for movement and prevents charge state entry.
- **Fix approach (v1):** Allow movement to continue while charging. Don't need console hop-to-grid-center for v1.
- **Investigation needed:** Decompile tool-use state machine. Look for `Farmer.isMoving()` check.
- **Testing plan:** For each upgradeable tool at each upgrade level, test charged use stationary + while walking vs. Switch behavior.

### 25b. Slingshot Combat — NEEDS INVESTIGATION
- Having slingshot equipped stops movement. Slingshot doesn't behave like console.
- **Expected (console):** Move freely, hold tool button to aim (stick controls crosshairs), release to fire.
- **Key question:** Vanilla Android bug or mod-caused?
  - Our X/Y swap in `GetState_Postfix` might interfere with slingshot's pull-back mechanic (continuous held state, not just press).
  - Fix: disable X/Y swap during slingshot use (`Game1.player.CurrentTool is Slingshot`)?
- **Investigation needed:** Test with mod disabled. Decompile `Slingshot.beginUsing()`, `tickUpdate()`, `endUsing()`.
- Possibly related to #25 (both involve stick + tool-use state).

---

## Milestone 6: Advanced Features (v4.0)

### 23. Lock Inventory Slots
- Prevent specific slots from being moved/sorted. User feature request.
- Need: way to mark slots (long-press or modifier), sorting/transfer skips locked slots.
- GMCM toggle.

### 24. Save Inventory Layout Profiles
- Save/restore inventory arrangements. Pairs with #23 (locked slots define layout, profiles save/restore).

### 27. Toolbar Size Slider (Options Menu)
- Console-style 12-slot toolbar has overlap/sizing issues on small screens.
- Hijack vanilla "Toolbar Size" slider or inject our own.
- **Investigation needed:** Does vanilla slider exist on Android? What field does it control? How does our toolbar determine slot size?
- **File:** `Patches/ToolbarPatches.cs`, possibly new `Patches/OptionsPagePatches.cs`

### 15. Disable Touchscreen Option
- GMCM toggle to disable all touch/mouse input when using controller.
- **Deprioritized:** Touch provides useful fallback. Off by default.
- Risk: some vanilla Android controller code may internally simulate mouse clicks.

### 13c. Color Picker Cursor Position Slightly Off
- Visible cursor doesn't align perfectly with swatch grid during navigation. Functionality correct (A selects right color).
- Likely caused by gap between relocated component bounds and actual rendered swatch visuals.
- **Not blocking.** Cosmetic only.
- **File:** `Patches/ItemGrabMenuPatches.cs`

### 46. Grey Out Non-Donatable Items on Bundle Page
- When a bundle donation page is open, items in inventory that cannot be donated to that bundle should be greyed out.
- Same pattern as #44 (zero-price items greyed out on sell tab) — override `highlightMethod` on the inventory to only highlight valid donation items.
- **Investigation needed:** How does the game determine valid donations? Check `Bundle.canAcceptThisItem()` or equivalent. Need to match against the bundle's remaining required ingredients.
- **File:** `Patches/JunimoNoteMenuPatches.cs`

### 47. Missed Rewards Chest Not Appearing
- After completing a CC room with unclaimed bundle rewards, the "missed rewards" chest should appear at tile (22, 10) in the Community Center. On Android, the chest never appears — even when reward stacks are left completely untouched.
- **Vanilla system:** `CommunityCenter.checkForMissedRewards()` iterates `bundleRewards`, checks `bundleRewards[key] == true && areasComplete[area] == true`, populates `missedRewardsChest` items. Called from `doRestoreAreaCutscene` (line 875), `resetSharedState` (line 562), and `performAction` on "MissedRewards" tile (line 357). Chest tile modification via `showMissedRewardsChestEvent`.
- **Confirmed broken:** User left 2 of 4 reward stacks completely untouched, room completed, no chest appeared. `BundleRewards` still showed pending indices `[23, 25]` in logs after room completion.
- **Investigation needed:** Does `showMissedRewardsChestEvent` fire on Android? Does the tile modification at (22, 10) work? Is `checkForMissedRewards` ever called? May be an Android-specific issue with the event system or tile layer.
- **Possible fix approaches:** If the event never fires, we could hook `markAreaAsComplete` or `doRestoreAreaCutscene` to force-check for missed rewards. If the chest exists but is invisible, may need tile/sprite fix.
- **File:** Likely new `Patches/CommunityCenterPatches.cs`

### 38. GMCM Two-Tier Config (Simple Page + Granular File)
- GMCM currently has a flat list of toggles. With 20+ features, this is heading toward a 68-item checklist nobody wants to scroll through.
- **Goal:** "Easy mode" for most users (streamlined GMCM page with grouped presets/categories) and "picky mode" for power users (full per-feature granularity in `config.json`).
- **Approaches to investigate:**
  1. **GMCM categories/sections:** Group related toggles under collapsible headers (Menu Fixes, Button Remapping, Inventory, Combat). Fewer top-level items visible.
  2. **Preset profiles:** "Console Parity" (everything on), "Minimal" (just menu fixes), "Custom" (unlocks all toggles). Preset selector at top, individual toggles only show in Custom mode.
  3. **GMCM simple + config.json granular:** GMCM shows only category-level toggles (e.g. "Enable Menu Fixes"). Per-feature overrides live in `config.json` only — power users edit the file directly.
  4. **Two GMCM pages:** "Quick Setup" page with presets/categories, "Advanced" page with every individual toggle. Check if GMCM API supports multiple pages per mod.
- **Investigation needed:** What does the GMCM API support? Section headers? Multiple pages? Conditional visibility (show/hide based on another toggle)?
- **File:** `ModEntry.cs` (GMCM registration), `ModConfig.cs`

---

## Not Our Bug (Tracked for Reference)

### 26. SMAPI Menu Button Position (G Cloud Title Screen)
- SMAPI details and mod menu button positioned ~1/3 up screen instead of corner. Cannot tap or A-press them.
- G Cloud 1920x1080 — SMAPI scaling/anchor bug at 1080p.
- **Recommendation:** Report to SMAPI, not our responsibility.
