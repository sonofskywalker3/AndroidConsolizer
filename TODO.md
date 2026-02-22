# AndroidConsolizer - TODO

**Read this file before starting any bug fix or feature.** Each item has implementation notes, root cause analysis, and file references.

For completed features and their technical reference, see `DONE.md`.

---

## Milestone 1: GameMenu Tabs (v3.4) — COMPLETE

All items done. See `DONE.md` and `.planning/STATE.md` quick tasks table.

---

## Milestone 2: Chest & Item Interaction Polish (v3.5) ← ACTIVE

### 13c. Color Picker Cursor Position Slightly Off
- Visible cursor doesn't align perfectly with swatch grid during navigation. Functionality correct (A selects right color).
- Likely caused by gap between relocated component bounds and actual rendered swatch visuals.
- **Not blocking.** Cosmetic only.
- **File:** `Patches/ItemGrabMenuPatches.cs`

### 34. Building Style Picker Controller Support
- The "Choose cabin style" dialog (`BuildingSkinMenu`) in the carpenter menu has no controller navigation. B closes it but no way to browse/select building skins without touch.
- **File:** `Patches/CarpenterMenuPatches.cs`

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

### 35. Load Game Screen Cursor/Navigation
- After pressing Load on the main menu, cursor appears near the bottom right instead of on the first save slot.
- Navigation has issues (details TBD — needs investigation).
- **Minor.** Low priority.
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
