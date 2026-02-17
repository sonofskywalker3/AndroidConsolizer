# AndroidConsolizer - TODO

**Read this file before starting any bug fix or feature.** Each item has implementation notes, root cause analysis, and file references.

For completed features and their technical reference, see `DONE.md`.

---

## Milestone 1: GameMenu Tabs (v3.4) ← ACTIVE

### 14. Social Tab Cursor Fix — IN TESTING
- Cursor doesn't follow when switching tabs with LB/RB.
- **Confirmed persists** on Logitech G Cloud.
- Symptoms: (1) Switch to social tab, cursor stays visually on inventory tab icon. (2) D-pad scrolls villager list but no visual indicator. (3) B from gift log puts cursor back on inventory tab visually. (4) Right from social tab skips straight to map tab.
- Log analysis: After tab switch, mod correctly stops intercepting (only fires on inventoryTab). All social tab input passes through to vanilla game.
- Possible root cause: `GameMenu.changeTab()` doesn't call `snapToDefaultClickableComponent()` on the new page.
- Navigation implemented (v3.3.18) with scroll support. Awaiting device testing.
- **Part of the Main Menu Overhaul project — see #14c.**
- **File:** `Patches/GameMenuPatches.cs`

### 14a. Gift Log Improvements
- **Visible cursor:** No visible cursor/selection indicator when viewing a villager's gift log (ProfileMenu). Need to investigate what components exist and add cursor support.
- **Bumper-switch return position:** Using LB/RB to switch villagers inside the gift log doesn't update the saved return index. Pressing B returns to the villager you originally pressed A on, not the one you switched to. Fix: patch ProfileMenu's `ChangeCharacter` to update `_savedSocialReturnIndex`, or detect the current villager on ProfileMenu exit.
- **Scroll-to-top on return (cosmetic, non-blocking):** Returning from gift log for villagers 5+ scrolls them to the top of the screen. Villagers 1-4 stay in place since they're already visible at `slotPosition=0`. Cause: restore sets `slotPosition = snapTargetIndex` which puts the villager at the top. Could save original `slotPosition` alongside the slot index for a more natural return position.
- **Log evidence (v3.3.42):** `FixSocialPage` is called twice on gift log exit (ChangeTab_Postfix + OnGameMenuOpened). Fix: don't clear `_savedSocialReturnIndex` in FixSocialPage; clear on next user input instead.
- **File:** `Patches/GameMenuPatches.cs`

### 14b. Collections/Shipping Tab — Vertical Sub-Tabs Unreachable
- No way to switch between vertical sub-tabs (Crops, Fish, Recipes, Achievements, etc.) using controller. Only first sub-tab accessible.
- **On console:** LT/RT swap between vertical sub-tabs, LB/RB swap main tabs.
- **Fix approaches:**
  1. **Triggers for sub-tab switching (console parity):** LT/RT cycle through vertical sub-tabs. Check if triggers already used on this tab.
  2. **Replace red-box selector with visible cursor:** More work but fixes underlying navigation model.
- **File:** `Patches/GameMenuPatches.cs` (new patches needed)

### 20. Settings Menu Controller Navigation
- Options menu uses free cursor, not snap-based.
- Left joystick moves free cursor anywhere, right joystick scrolls options list.
- Can reach GMCM "Mod Options" button by moving cursor below visible area (1px visible at bottom).
- Full fix: inject snap navigation for sliders, checkboxes, dropdowns. Complex.
- **File:** Would need new `Patches/OptionsPagePatches.cs` or extend `GameMenuPatches.cs`

### 14e. CarpenterMenu "Build" Button Unaffordable — TO INVESTIGATE
- Clicking "Build" for a building you can't afford dumps back to shop. Console greys out the button.
- Need to check: vanilla Android behavior? Our bug or theirs?
- Desired: grey out Build button (console parity) or show error message.
- **File:** `Patches/CarpenterMenuPatches.cs`

---

## Milestone 2: Chest & Item Interaction Polish (v3.5)

### 11b. Touch-Interrupt Side Effects — PARTIALLY FIXED
- **Tooltip follows finger after touch cancel (CHEST — NOT FIXED):** Inventory touch guards block `receiveLeftClick`/`leftClickHeld`, but `performHoverAction` still fires in chests. `ItemGrabMenuPatches` needs equivalent touch guards.
- **Touch on chest breaks sidebar navigation — FIXED (v3.2.16):** Self-healing check in `Update_Postfix` detects missing `ID_SORT_CHEST` and re-runs `FixSnapNavigation()`.
- **Cursor resets to slot 0 after touch:** After touch interrupt, next joystick input snaps to slot 0. Game calls `snapToDefaultClickableComponent()` when re-engaging controller after touch.
- **Fix approach:** Save `currentlySnappedComponent.myID` before `CancelHold()`, restore on next controller input. Applies to both inventory and chest menus.
- **File:** `Patches/ItemGrabMenuPatches.cs`, `Patches/InventoryPagePatches.cs`

### 13c. Color Picker Cursor Position Slightly Off
- Visible cursor doesn't align perfectly with swatch grid during navigation. Functionality correct (A selects right color).
- Likely caused by gap between relocated component bounds and actual rendered swatch visuals.
- **Not blocking.** Cosmetic only.
- **File:** `Patches/ItemGrabMenuPatches.cs`

### O2. Remove GamePad.GetState() from Draw Postfix
- `InventoryManagementPatches.InventoryPage_Draw_Postfix` calls `GamePad.GetState()` every frame to check if A is pressed. On Android, this is a JNI call through MonoGame.
- **Fix:** Store A-button state in a static bool from `OnUpdateTicked`, read in draw postfix.
- **File:** `Patches/InventoryManagementPatches.cs`

---

## Milestone 3: Overworld Cursor & Accessibility (v3.6)

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
- Also: "Choose cabin style" dialog (`BuildingSkinMenu`) — no controller support. B closes it.

### 17. Title/Main Menu Cursor Fix
- Cursor reportedly invisible on main menu with controller.
- **UNVERIFIED** — Not reproducible on Odin Pro. May be device/controller-specific.

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

---

## Not Our Bug (Tracked for Reference)

### 26. SMAPI Menu Button Position (G Cloud Title Screen)
- SMAPI details and mod menu button positioned ~1/3 up screen instead of corner. Cannot tap or A-press them.
- G Cloud 1920x1080 — SMAPI scaling/anchor bug at 1080p.
- **Recommendation:** Report to SMAPI, not our responsibility.
