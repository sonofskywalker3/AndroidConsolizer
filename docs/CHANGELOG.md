# Changelog

All notable changes to Android Consolizer will be documented in this file.

## [3.7.0] - 2026-05-14

Bug fix release — two controller-handling fixes plus log-noise cleanup. All fixes individually toggleable via GMCM; technical references in `DONE.md`. Internally this rolls up the v3.6.1–v3.6.9 patch series.

### Fixed
- **Picked-up items land in the active toolbar row** (#63) — picking up furniture, forage, drops, gifts, or shop purchases now places the item into the toolbar row you're currently viewing, instead of always defaulting to row 0. Vanilla Android's `removeQueuedFurniture` and `Farmer.addItemToInventory` both hard-scan slots 0–11. Furniture (your held tool until placed) also moves the selection to follow; non-tool pickups never disturb your selected tool. New `EnablePickupToActiveRow` GMCM toggle (default on).
- **Triggers no longer skip two toolbar slots** (#54) — on Gamesir and G Cloud controllers a single trigger pull occasionally moved two slots instead of one. Hall-effect triggers briefly drop to zero mid-pull and digital triggers glitch for a single tick; the old single-bool edge detector re-fired on the bounce. Replaced with a two-threshold state machine plus a 4-tick release-confirmation streak — verified on G Cloud (analog) and Gamesir X2 (digital).

### Changed
- **Diagnostic log noise reduced** (#64) — leftover `[Bed]` and `[StartHold]` debugging lines from the 3.5 bed-bounce and Quest Log investigations downgraded from Info to Debug and gated behind the Verbose Logging toggle.

### Investigated (no code change)
- **#65 bed-bounce root cause** — confirmed the v3.5.35 `removeQueuedFurniture` gate is the structurally correct fix: bed removal is asynchronous, and `canBeRemoved` is checked once, synchronously, upstream of the async chain. Farm Type Manager ruled out as a cause. See `DONE.md` "#53".
- **#48 X/Y button overlap** — the v3.3/v3.4 "Y both picks up and sorts" double-fire does not reproduce on current code; resolved incidentally by intervening input-pipeline work. See `DONE.md` "#48".

## [3.6.0] - 2026-05-08

Bug fix release responding to Nexus user reports. All fixes individually toggleable via GMCM; technical references in `DONE.md`.

### Fixed
- **Bed bouncing on placement** (#53) — patched `GameLocation.removeQueuedFurniture` to gate the full removal cascade. v3.5.14's `canBeRemoved` patch and v3.5.34's `performRemoveAction` patch were both insufficient — the caller still ran `furniture.Remove(guid)` and dumped the bed back to inventory.
- **Right stick drifting cursor in overworld** (#50) — vanilla Android maps the right stick to mouse position, drifting interactions to the wrong tile. New `SuppressRightStickInOverworld` GMCM toggle (default on).
- **Dresser destroying clothes** (#51) — `ShippingBinPatches` was matching any `reverseGrab=false` `ItemGrabMenu`. Source check tightened to actual `ShippingBin`.
- **Aquarium duplicating fish on take** (#57) — Y-button take-one bypassed the source container's removal hook. Now invokes `behaviorOnItemGrab` via reflection.
- **Quest Log lockout** (#52) — tap Start opens GameMenu (vanilla), hold Start ≥500ms opens Quest Log. Suppressed at `GamePad.GetState` level because `Game1.UpdateControlInput` reads gamepad state directly and bypasses SMAPI input suppression.
- **Storage shop fixes** (#1, #51, #57 follow-ups) — dresser/aquarium sell tab honors `highlightAllItems`, vanilla deposit restrictions restored, hovered item refreshes after `rebuildSaleButtons`, buy tab defaults to row 0 when stock is empty.
- **Chest deposits skipping bundle/quest hooks** (#58) — routed through the menu's `behaviorFunction`.
- **Equipment slot silent-fail** (#59, #9 follow-up) — equip/unequip routed through `Farmer.Equip` with grab-replacement so callbacks fire.
- **Shop stock desync after purchase** (#60) — runs `ActionsOnPurchase` and synchronizes stock counts the way vanilla does.
- **Fishing rod tackle attachment via controller** (#61) — routed through `Tool.attach`.
- **Console-style A/Y deposits routing into Auto-Grabber** — Auto-Grabber chests now blocked from console-style deposit shortcuts (Auto-Grabber expects to own its contents).

### Added
- **Console Furniture Placement** — replaces the misleading multi-tile green-square map (which marked where the furniture's *top-left corner* could land, not where the furniture itself would sit) with a single ghost rectangle plus a translucent furniture sprite over the placement target. Matches console behaviour. New GMCM toggle `EnableConsoleFurniturePlacement` (default on).

### Technical
- 28 atomic commits between v3.5.0 and v3.6.0; full bisectable history retained.
- Mobile-only `Object.DrawRedGreenRectangleForPlacing` resolved via `AccessTools.Method` string lookup so the project still builds against the PC `StardewValley.dll` reference assembly.

## [3.0.0] - 2026-02-07

### Added
- **Console-style chest item transfer** — A button instantly transfers full stack between chest and inventory. Y button transfers one item. Hold Y for rapid single-item transfer. No selection step needed.
- **RB snaps to Fill Stacks** — Right bumper snaps cursor to the Fill Stacks sidebar button in chests
- **Chest sidebar navigation** — Sort Chest, Fill Stacks, Color Toggle, Sort Inventory, Trash, and Close X buttons all reachable via controller snap navigation
- **Color picker swatch navigation** — Full 7x3 grid with correct neighbors. A selects color. B closes picker only (not the chest). Runtime stride detection for correct cursor positioning. Color preserved after probe.
- **Close X button** — Properly closes chest via simulated B press with A suppress-until-release (no reopen)
- **exitThisMenu same-tick guard** — Prevents B from closing both the color picker AND the chest in the same frame
- New config toggles: `EnableChestNavFix`, `EnableChestTransferFix`

### Changed
- Sort Chest button up-neighbor now goes to Close X (more natural sidebar flow)
- Y button on chest grid slots now transfers one item instead of triggering global add-to-stacks (add-to-stacks still available via Fill Stacks sidebar button or RB shortcut)

## [2.1.0] - 2026-02-01

### Changed
- **Complete shipping bin rewrite** - Replaced broken touch-based UI with console-style controls
- No more "pick up and drop" - just select an item and press A to ship
- Uses game's native `behaviorFunction` for proper integration

### Fixed
- **Shipping bin now works properly** - A button ships entire stack, Y button ships one item
- **"Last shipped" display now updates** - Shows the item you just shipped
- **Toolbar selection box** - Now properly sized and drawn behind items

### Technical
- Removed 200+ lines of drop zone/navigation hacking
- Simplified to ~80 lines using the game's own shipping flow

## [2.0.0] - 2026-02-01

### Added
- **Console-style toolbar** - 12-slot rows with LB/RB to switch rows, LT/RT to move within row
- Custom toolbar rendering that matches console layout

### Changed
- Rebranded to Android Consolizer

## [1.0.0] - 2026-01-31

### Added
- Initial stable release
- Shop purchasing (A button)
- Inventory/chest sorting (X button)
- Add to stacks in chests (Y button)
- X button deletion bug blocked

## [0.3.4] - 2026-01-31

### Changed
- **New button scheme**: X = Sort, Y = Add to stacks
- Removed broken buy/sell toggle from active features
- Simplified GMCM config menu
- GMCM now shows current button mappings at top

### Added
- Inventory sorting (X button when in inventory menu)
- Config options: EnableSortFix, EnableAddToStacksFix

### Fixed
- X button in inventory/chest now triggers sort instead of potentially triggering deletion bug

## [0.3.3] - 2026-01-31

### Added
- Detailed logging for all button presses in chest menu
- Support for Back/Start/BigButton as organize alternatives

### Fixed
- Improved button detection in ItemGrabMenu

## [0.3.2] - 2026-01-31

### Fixed
- Shop purchase now respects quantity selected via LT/RT
- Properly calculates total cost based on quantity
- Resets quantity to 1 after purchase

## [0.3.1] - 2026-01-31

### Added
- Extensive debug logging for shop purchases
- Multiple fallback methods for purchase

### Changed
- Purchase now tries direct method call before manual implementation

## [0.3.0] - 2026-01-31

### Added
- Initial beta release
- Shop purchase fix (A button)
- Chest add-to-stacks (X button) - working
- Buy/sell toggle (Y button) - not working on Android
- GMCM configuration support
- Verbose logging option

## [Unreleased]

### Planned
- Toolbar navigation fix (console-style 12-item rows)
- Buy/sell toggle fix (need different approach)
- Fishing rod bait removal fix
- Full button remapping system
