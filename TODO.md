# AndroidConsolizer — TODO

**Read this before starting any bug fix or feature.** Each item has implementation notes, root-cause hypotheses, and file references. Completed work lives in [`DONE.md`](./DONE.md) — check there for prior art before starting anything new.

Shipped: **v3.6.0** (Bug Fix Release). Roadmap structure was re-evaluated post-3.6.0 — see [`docs/superpowers/specs/2026-05-08-roadmap-reevaluation-design.md`](./docs/superpowers/specs/2026-05-08-roadmap-reevaluation-design.md) for the reasoning behind the milestones below. The original GSD-era M3 → M4 → M5 → M6 ordering has been replaced with the structure here.

**Release-tooling items** (#66 Nexus mod-page automation, #67 cookie refresh) live in [`docs/RELEASE_TOOLING.md`](./docs/RELEASE_TOOLING.md), not here.

---

## v3.8.0 — Console Parity: Quick Wins

Small to medium parity fixes that can each be solved with localized patches. No multi-patch system arc. Bumps the mod from "most parity items done" to "every menu has correct defaults and visible cursors."

### 22b. Dialogue Option Box — Counter-Intuitive Initial Selection
- Nothing visually selected when dialogue choice box appears. Pressing down selects TOP option; pressing up selects BOTTOM option.
- **Root-cause (hypothesis):** Game initializes with invisible slot above/below visible options (index -1 or null). Navigation wraps around.
- **Fix approaches:**
  1. Start with top option pre-selected (convenient but risks accidental selection). Check if console pre-selects.
  2. Invisible start position BETWEEN options — up=top, down=bottom. Safest.
  3. Disable wrap-around on first input only.
- **Investigation:** Find dialogue choice class, check selection index init, check console behavior.

### 17. Title/Main Menu Cursor Fix
- Cursor should be visible on the Load button by default when the main menu loads, instead of being invisible until the player moves the stick or presses a button.
- **Investigation:** Check `TitleMenu` class, initial `currentlySnappedComponent`, cursor visibility logic.

### 35. Load Game Screen Cursor/Navigation
- Cursor should start on the top save slot when the Load Game menu opens, instead of in the free space below the saves.
- Navigation has issues (details TBD — needs investigation).
- **Investigation:** Check `LoadGameMenu` class, initial snap position, navigation wiring.

### 39. Monster Eradication Tracking Page
- No cursor visible on the monster eradication goals page (Adventurer's Guild tracking). Can't switch pages with controller.
- **Investigation:** Check which menu class this is, how pages are structured, what navigation exists.

### 46. Grey Out Non-Donatable Items on Bundle Page
- When a bundle donation page is open, items in inventory that cannot be donated to that bundle should be greyed out.
- Same pattern as #44 (zero-price items greyed out on sell tab) — override `highlightMethod` on the inventory to only highlight valid donation items.
- **Investigation:** How does the game determine valid donations? Check `Bundle.canAcceptThisItem()` or equivalent. Need to match against the bundle's remaining required ingredients.
- **File:** `Patches/JunimoNoteMenuPatches.cs`.

### 47. Missed Rewards Chest Not Appearing
- After completing a CC room with unclaimed bundle rewards, the "missed rewards" chest should appear at tile (22, 10) in the Community Center. On Android, the chest never appears — even when reward stacks are left completely untouched.
- **Vanilla system:** `CommunityCenter.checkForMissedRewards()` iterates `bundleRewards`, checks `bundleRewards[key] == true && areasComplete[area] == true`, populates `missedRewardsChest` items. Called from `doRestoreAreaCutscene` (line 875), `resetSharedState` (line 562), and `performAction` on "MissedRewards" tile (line 357). Chest tile modification via `showMissedRewardsChestEvent`.
- **Confirmed broken:** User left 2 of 4 reward stacks completely untouched, room completed, no chest appeared. `BundleRewards` still showed pending indices `[23, 25]` in logs after room completion.
- **Investigation:** Does `showMissedRewardsChestEvent` fire on Android? Does the tile modification at (22, 10) work? Is `checkForMissedRewards` ever called? May be an Android-specific issue with the event system or tile layer.
- **Possible fixes:** Hook `markAreaAsComplete` or `doRestoreAreaCutscene` to force-check for missed rewards. If the chest exists but is invisible, may need tile/sprite fix.
- **File:** Likely new `Patches/CommunityCenterPatches.cs`.

### 27. Toolbar Size Slider (Options Menu)
- Console-style 12-slot toolbar has overlap/sizing issues on small screens.
- Hijack vanilla "Toolbar Size" slider or inject our own.
- **Investigation:** Does vanilla slider exist on Android? What field does it control? How does our toolbar determine slot size?
- **Public bug report:** Nexus user throyiii filed Bug #1050718 ("Can't resize the toolbar") against v3.5.10 on 5 Mar 2026 — bumped priority since there is now a public report.
- **Files:** `Patches/ToolbarPatches.cs`, possibly new `Patches/OptionsPagePatches.cs`.

### 19. Geode Breaking Menu — Visual Feedback
- Partially works but no visual feedback.
- Geode highlighted in inventory but does NOT visually move to anvil. Pushing up invisibly moves to anvil area, A cracks it. Functional but unintuitive.
- **Fix approach:** Apply inventory-style patches on `GeodeMenu`. A-button to select geode, visual feedback of geode moving to anvil, A on anvil to crack.
- **Note:** Originally bundled with #12 in the GSD-era M3, but doesn't actually require a free cursor — same pattern as the GameMenu tab work.

---

## v3.9.0 — Console Parity: Big Systems

Three player-facing real-time gameplay systems. Each likely needs multiple patches with device testing. Bundling them into one focused arc keeps testing context warm.

### 18. Museum Donation Menu
- Controller-only placement inaccessible. Confirmed on G Cloud. Touch required to select/place items.
- **Approach:** Snap-based item selection overlay over the museum's free-placement grid. Confirmed possible without #12 (Switch handles museum donations with snap nav, no free cursor required).
- **Implementation challenge:** The museum grid doesn't map cleanly to discrete components. Will need a custom selection model — likely tracking a virtual cursor in tile-space and rendering placement preview at the snapped tile.

### 25. Tool Charging Broken While Moving
- Holding tool button while moving rapid-fires single uses instead of charging. Player stops moving and tool keeps firing.
- **Expected (console):** Holding tool button while moving begins charging. Player hops one square at a time.
- **NOT mod-caused.** Occurs regardless of layout. Android port difference.
- **Root-cause hypothesis:** Android's tool-use code checks for movement and prevents charge-state entry.
- **Fix approach (v1):** Allow movement to continue while charging. Don't need console hop-to-grid-center for v1.
- **Investigation:** Decompile tool-use state machine. Look for `Farmer.isMoving()` check.
- **Testing plan:** For each upgradeable tool at each upgrade level, test charged use stationary + while walking vs. Switch behavior.

### 25b. Slingshot Combat
- Having slingshot equipped stops movement. Slingshot doesn't behave like console.
- **Expected (console):** Move freely, hold tool button to aim (stick controls crosshairs), release to fire.
- **EXPLICIT EXCEPTION to the "right-stick features ship in v4.0" rule.** Slingshot is high-usage parity (people actually fight monsters), and gating it on the cursor release would leave a working ranged weapon hostage to a much bigger feature arc.
- **Key question:** Vanilla Android bug or mod-caused?
  - Our X/Y swap in `GetState_Postfix` might interfere with slingshot's pull-back mechanic (continuous held state, not just press).
  - Fix idea: disable X/Y swap during slingshot use (`Game1.player.CurrentTool is Slingshot`)?
- **Investigation:** Test with mod disabled. Decompile `Slingshot.beginUsing()`, `tickUpdate()`, `endUsing()`.
- Possibly related to #25 (both involve stick + tool-use state).

---

## v4.0.0 — The Right Stick Update

**Placement rule:** *all right-stick features ship in v4.0*, with slingshot aim (#25b) as the deliberate v3.9 exception. Major version bump because the right-stick cursor is the only remaining feature class that doesn't exist on Switch — calling v4.0 "The Right Stick Update" makes the bump narratively legible.

### 12. Right Joystick Cursor Mode + Zoom Control
- **Bundled feature (LARGE)**
- **Cursor mode:** Right joystick moves free cursor in menus + gameplay.
  - Essential for precise furniture placement, free-cursor-driven future menus.
  - On Switch: right stick moves cursor, disappears after inactivity. Press for left click.
  - Implementation: read right stick axis from `GamePad.GetState()`, call `Game1.setMousePosition()` per tick.
  - Complexity: dead zones, acceleration curves, auto-hide, interaction with snap navigation.
- **Zoom control:** Add to in-game Options page (not GMCM).
  - Confirmed: zoom slider does NOT exist on Android — mobile port stripped it (pinch-to-zoom only).
  - Console: `whichOption = 18`, `OptionsSlider`, range 75%-200%, controls `Game1.options.desiredBaseZoomLevel`.
  - Need to inject custom `OptionsSlider` subclass into `OptionsPage.options` list.
  - Must subclass `OptionsSlider` with own value management since Android may not wire game's zoom handling.
  - GMCM's "Mod Options" button partially cut off — slider injection may need to fix scroll bounds.

### 62. Right-Stick to Move Furniture Ghost
- **Source:** Original "console furniture placement" ask had two parts. v3.5.38–v3.5.39 covered part 1 (single ghost rectangle + translucent sprite). Part 2: right stick moves the ghost the way it moves the carpenter building ghost.
- **Belongs in v4.0** because it's a right-stick feature — same placement rule as #12.
- **Behaviour to match:** Picking up furniture should produce a ghost in front of the player. Right stick offsets the ghost relative to the player's facing tile (not the cursor). Walking still re-anchors the ghost to in-front-of-player. A places, B cancels (returns furniture to inventory).
- **Implementation sketch:**
  1. New `_furnitureGhostOffset` Vector2 in `FurniturePlacementPatches.cs`, accumulated each tick from `RawRightStickX/Y` (already cached in `GameplayButtonPatches`).
  2. Patch `Game1.GetPlacementGrabTile` to add `_furnitureGhostOffset` when the player has a Furniture as `ActiveObject`.
  3. Reset offset on placement success / B cancel / item swap. Reuse `OnFurnitureUpdateTicked` cadence in `CarpenterMenuPatches`.
  4. Optional: configurable max-distance clamp so the ghost can't drift off-screen.
- **Reuses:** Same pattern as `CarpenterMenuPatches` building-ghost cursor override. Code there is the gold standard for this pattern.
- **Files:** `Patches/FurniturePlacementPatches.cs`, possibly `Patches/GameplayButtonPatches.cs` for stick polling.
- **Config:** Probably gate behind the existing `EnableConsoleFurniturePlacement` flag — same feature, just the second half.

---

## Post-4.0 — Advanced Features

Genuinely Android-better territory, not parity. No version commitment yet — these get scheduled when the time comes. Each likely needs its own brainstorming session.

### 23. Lock Inventory Slots
- Prevent specific slots from being moved/sorted. User feature request.
- Need: way to mark slots (long-press or modifier), sorting/transfer skips locked slots.
- GMCM toggle.

### 24. Save Inventory Layout Profiles
- Save/restore inventory arrangements. Pairs with #23 (locked slots define layout, profiles save/restore).

### 38. GMCM Two-Tier Config (Simple Page + Granular File)
- GMCM currently has a flat list of toggles. With 20+ features, this is heading toward a 68-item checklist nobody wants to scroll through.
- **Goal:** "Easy mode" for most users (streamlined GMCM page with grouped presets/categories) and "picky mode" for power users (full per-feature granularity in `config.json`).
- **Approaches:**
  1. **GMCM categories/sections:** Group related toggles under collapsible headers (Menu Fixes, Button Remapping, Inventory, Combat). Fewer top-level items visible.
  2. **Preset profiles:** "Console Parity" (everything on), "Minimal" (just menu fixes), "Custom" (unlocks all toggles). Preset selector at top, individual toggles only show in Custom mode.
  3. **GMCM simple + config.json granular:** GMCM shows only category-level toggles. Per-feature overrides live in `config.json` only.
  4. **Two GMCM pages:** "Quick Setup" page with presets/categories, "Advanced" page with every individual toggle. Check if GMCM API supports multiple pages per mod.
- **Investigation:** What does the GMCM API support? Section headers? Multiple pages? Conditional visibility (show/hide based on another toggle)?
- **Files:** `ModEntry.cs` (GMCM registration), `ModConfig.cs`.

---

## Won't Fix / Parked

Items kept here for history; not on any milestone. Revisit only if a user reports them or new evidence appears.

### 16. Trash Can Lid Animation (Cosmetic)
- Lid doesn't animate on controller hover. Five fix approaches tried (hoverAction with coords, setMousePosition, reflection on `trashCanLidRotation`, prefix on `InventoryPage.draw()`, drawing the lid sprite ourselves) — all failed.
- **Hypothesis:** Android port renders through different code path or mobile-specific overlay covers lid area.
- **Why parked:** Cosmetic only. 5 attempts failed. Mod-space may not be able to reach the rendering layer.

### 16d. CarpenterMenu Direct Ghost Control
- Ghost only moves via touch/click, not joystick. Seven versions tried (v3.1.14-v3.1.20). Current A-button-tap approach works.
- **Hypothesis:** Android stores ghost position from last touch event in internal field, bypassing all mouse APIs.
- **Why parked:** Already labeled "Lowest Priority / someday" in the original TODO. Current workaround is functional. Revisit only if someone decompiles `CarpenterMenu.draw()` on Android and finds the ghost position field.

### 15. Disable Touchscreen Option
- GMCM toggle to disable all touch/mouse input when using controller.
- **Why parked:** Already labeled "Deprioritized" — touch provides useful fallback. Some vanilla Android controller code may internally simulate mouse clicks, which makes this risky to implement.

### 13c. Color Picker Cursor Position Slightly Off
- Visible cursor doesn't align perfectly with swatch grid during navigation. Functionality correct (A selects right color).
- Likely caused by gap between relocated component bounds and actual rendered swatch visuals.
- **Why parked:** Cosmetic only, no functional impact. Already labeled "Not blocking."

### 26. SMAPI Menu Button Position (G Cloud Title Screen)
- SMAPI details and mod menu button positioned ~1/3 up screen instead of corner. Cannot tap or A-press them.
- G Cloud 1920x1080 — SMAPI scaling/anchor bug at 1080p.
- **Why parked:** Not our bug. Should be reported to SMAPI Android upstream.
