# Android Consolizer

A SMAPI mod that makes Android Stardew Valley's controller support work like the Nintendo Switch version. 90+ fixes across shops, inventory, chests, toolbar, Robin's building menu, Community Center bundles, furniture placement, and every game menu tab.

If you play on a handheld (Odin, Ayaneo, Retroid, etc.) or dock your phone/tablet to a TV, this mod makes the game actually playable without a touchscreen.

## Current Version: 3.7.0 — Bug Fix Release 2

## What's New in v3.7.0

A focused follow-up to v3.6. Both fixes individually toggleable via GMCM.

**Picked-up items land in the active toolbar row** — Furniture, forage, drops, gifts, and shop purchases now go into the toolbar row you're currently viewing, instead of always dropping into row 0 where you can't see them. Picked-up furniture (your held tool until you place it) moves the selection to follow; ordinary pickups never disturb your selected tool. New toggle, on by default.

**Triggers no longer skip toolbar slots** — On Gamesir and Logitech G Cloud controllers, a single trigger pull occasionally jumped two toolbar slots instead of one. Hall-effect triggers briefly read zero mid-pull and some digital triggers glitch for a single frame; trigger handling is now a two-threshold state machine with a release-confirmation window that rides through those dropouts. Verified on both analog and digital triggers.

**Plus:** reduced diagnostic log noise — leftover debug lines from the v3.5 investigations are now quiet during normal play.

## Controller Layout Support

- **Switch/Odin**: A=right, B=bottom, X=top, Y=left
- **Xbox**: A=bottom, B=right, X=left, Y=top
- **PlayStation**: Cross=A, Circle=B, Square=X, Triangle=Y

Two control styles:

- **Switch style**: Right button confirms, bottom cancels
- **Xbox/PS style**: Bottom button confirms, right cancels

## Features

### Toolbar (Console-Style)
- 12-slot toolbar rows instead of Android's chaotic scrolling
- LB/RB switches between rows (up to 3 with full backpack)
- LT/RT moves left/right within the current row
- Visual toolbar matches console layout
- Picked-up items land in the row you're currently viewing

### Shops
- A button purchases on buy tab, sells entire stack on sell tab
- Y button sells one item (hold for rapid sell)
- LB/RB adjusts purchase quantity (hold to repeat)
- Right stick jumps 5 items at a time (hold to repeat)
- Y button icon shows tab-switch hint (adapts to controller layout)
- Sell price tooltip with gold coin icon next to selected item
- Non-sellable items (0g) greyed out on sell tab
- Visible cursor on both buy and sell tabs
- Works with every shop: Pierre, Robin, Marnie, Blacksmith upgrades, Desert Trader, recipes, Joja, dressers, aquariums, and more

### Chests (Console-Style)
- A transfers full stack between chest and inventory (instant, no selection step)
- Y transfers one item (hold for rapid transfer)
- X sorts chest contents
- RB snaps to Fill Stacks button
- All sidebar buttons reachable: Sort, Fill Stacks, Color Toggle, Sort Inventory, Trash, Close
- Full 7x3 color picker swatch navigation, B closes picker only (not the chest)
- Swap system when inventory is full — A picks up for displacement, place anywhere, B cancels
- X button deletion bug completely blocked (your iridium tools are safe)

### Inventory (Console-Style)
- A picks up entire stack to cursor, A again places or swaps
- Y picks up a single item from a stack (hold for continuous)
- X sorts inventory
- Held items render visually at cursor slot
- Tooltips on hover, equipment slot tooltips (hat, rings, boots, etc.)

### Shipping Bin
- A ships entire stack from selected slot
- Y ships one item
- "Last shipped" display updates properly

### Furniture Placement
- Single ghost rectangle shows exactly where the furniture will land
- Translucent furniture sprite rendered over the placement target — matches console
- Bed placement no longer bounces back to inventory
- Y no longer rapid-toggles — one press = one interaction

### Robin's Building Menu
- Menu no longer instantly closes when opened with controller
- Full joystick control in farm view — left stick moves cursor, pans viewport at edges
- Build mode: A confirms placement at cursor position
- Move mode: A selects building, move cursor, A confirms new location
- Demolish mode: A highlights building (green), A again confirms. Move off to deselect safely.
- Building skin picker navigable with controller

### Community Center Bundles
- Bundle overview: D-pad/thumbstick navigates bundles, A opens donation page
- Cursor remembers your position when returning from donation page
- Donation page: Navigate 6-column inventory grid, A donates items
- Navigate right into ingredient list to see what each slot needs
- Vault bundles: A on purchase button pays for the bundle
- Bundle rewards: Navigate to present, A opens rewards, A takes stacks
- Custom cursor drawn (Android suppresses the default one here)

### Game Menu Tabs
- **Social**: Navigate villager list, right stick fast scroll, A opens gift log, LB/RB switches villagers in gift log
- **Collections**: Grid navigation across items and sub-tabs, finger cursor
- **Crafting**: Finger cursor replaces red highlight
- **Skills**: Grid navigation across skill icons and level bars
- **Animals**: Navigate animal list
- **Powers**: Finger cursor replaces glow highlight
- **Options**: Left stick navigates, A activates, right stick scrolls, dropdowns work
- **LT/RT** switches between all tabs (console parity)

### Other Fixes
- Fishing rod bait/tackle: A picks up bait/tackle, Y on rod attaches or detaches
- Slingshot ammo: Same pattern — A picks up ammo, Y on slingshot attaches/detaches
- Cutscene skip: Press Start twice during skippable cutscenes
- Start button: Tap = Game Menu, Hold ~½ sec = Quest Log/Journal
- Right stick suppressed in overworld (no more cursor drift)
- GMCM controller navigation via our [controller-enabled GMCM fork](https://github.com/sonofskywalker3/GenericModConfigMenu)

### Bumper Mode
For controllers where triggers aren't detected (e.g. Xbox via Bluetooth):
- D-Pad Up/Down switches toolbar rows, LB/RB moves within row
- LB/RB adjusts shop purchase quantity

## Button Mappings (Default Layout + Style)

| Context | Button | Action |
|---------|--------|--------|
| **Gameplay** | LB | Switch to previous toolbar row |
| **Gameplay** | RB | Switch to next toolbar row |
| **Gameplay** | LT | Move left in toolbar row |
| **Gameplay** | RT | Move right in toolbar row |
| **Shop (buy)** | A | Purchase selected quantity |
| **Shop (buy)** | LB/RB | Adjust purchase quantity (hold to repeat) |
| **Shop (buy)** | Right stick | Jump 5 items up/down (hold to repeat) |
| **Shop** | Y | Switch between buy/sell tabs (icon shown on button) |
| **Shop (sell)** | A | Sell entire stack |
| **Shop (sell)** | Y | Sell one item (hold for rapid sell) |
| **Inventory** | X | Sort inventory |
| **Chest** | A | Transfer full stack (chest↔inventory) |
| **Chest** | Y | Transfer one item (hold for rapid transfer) |
| **Chest** | X | Sort chest contents |
| **Chest** | RB | Snap to Fill Stacks button |
| **Inventory** | A | Pick up / place / swap item |
| **Inventory** | Y | Pick up single from stack (hold for continuous) |
| **Shipping Bin** | A | Ship entire stack |
| **Shipping Bin** | Y | Ship one item |
| **Inventory** | A (on bait/tackle) | Pick up bait/tackle to cursor |
| **Inventory** | Y (on fishing rod) | Attach held bait/tackle or detach to cursor |
| **Building (farm view)** | Left stick | Move cursor / pan viewport at edges |
| **Building (build)** | A | Confirm building placement at cursor |
| **Building (move)** | A | Select building / confirm new placement |
| **Building (demolish)** | A | Select building (highlights green) / confirm demolition |
| **CC overview** | D-Pad/stick | Navigate between bundles |
| **CC overview** | A | Open bundle donation page |
| **CC donation** | D-Pad/stick | Navigate inventory / ingredient list |
| **CC donation** | A | Donate item from inventory |
| **CC vault** | A | Purchase vault bundle |
| **CC rewards** | A | Take full reward stack |
| **Game Menu** | LT/RT | Switch between tabs |
| **Social tab** | D-Pad/stick | Navigate villager list |
| **Social tab** | Right stick | Fast scroll villager list |
| **Social tab** | A | Open gift log |
| **Gift log** | LB/RB | Switch between villagers |
| **Gift log** | B | Return to social tab |
| **Options** | Left stick | Navigate options |
| **Options** | Right stick | Scroll options list |
| **Options** | A | Activate option / toggle / open dropdown |

## Requirements

- Stardew Valley 1.6.15+ (Android)
- [SMAPI 4.0+ for Android](https://github.com/NRTnarathip/SMAPI-Android-1.6) by NRTnarathip
- [Generic Mod Config Menu](https://github.com/sonofskywalker3/GenericModConfigMenu/releases) (optional, recommended — our fork adds controller navigation)

## Tested Controllers

| Controller | Device | Status | Notes |
|------------|--------|--------|-------|
| **AYN Odin Pro** (built-in) | AYN Odin Pro | ✅ Fully Working | All buttons and triggers work |
| **Ayaneo Pocket Air Mini** (built-in) | Ayaneo Pocket Air Mini | ✅ Fully Working | All buttons and triggers work |
| **EasySMX S10** | TCL NXTPaper 11 Plus | ✅ Fully Working | All buttons and triggers work |
| **Gamesir X2** | — | ✅ Fully Working | Digital triggers; v3.7 trigger fix verified here |
| **Xbox Wireless (Bluetooth)** | AYN Odin Pro | ⚠️ Partial | Triggers (LT/RT) not detected — enable "Use Bumpers Instead of Triggers". All other buttons work. |
| **Logitech G Cloud** (built-in) | Logitech G Cloud | ⚠️ Working | Trigger double-fire greatly reduced in v3.7 |

If you test on other hardware, let me know!

### Known Issues: Xbox Controller on Android

Xbox Wireless Controllers (both Xbox One and Xbox Series X|S) connected via Bluetooth have a known issue on Android where the analog triggers (LT/RT) are not detected by Stardew Valley. Both controller models behave identically — this is due to Xbox controllers reporting triggers on different axes (`AXIS_GAS`/`AXIS_BRAKE`) than what the game's framework expects (`AXIS_LTRIGGER`/`AXIS_RTRIGGER`).

**Workaround:** Enable "Use Bumpers Instead of Triggers" in the mod settings. This remaps:
- Toolbar: D-Pad Up/Down for rows, LB/RB for moving within row
- Shops: LB/RB for adjusting purchase quantity

All other Xbox controller buttons (A/B/X/Y, bumpers, thumbsticks, D-Pad) work correctly.

If you test with other controllers, please report your results!

## Installation

1. Download the latest release ZIP
2. Extract to your Mods folder (or install via SMAPI Launcher)
3. Launch the game via SMAPI

## Configuration

Edit `config.json` or use Generic Mod Config Menu in-game:

```json
{
  "EnableButtonRemapping": true,
  "ControllerLayout": "Switch",
  "ControlStyle": "Switch",
  "EnableConsoleChests": true,
  "EnableConsoleShops": true,
  "EnableConsoleToolbar": true,
  "EnablePickupToActiveRow": true,
  "EnableConsoleInventory": true,
  "EnableConsoleShipping": true,
  "SuppressRightStickInOverworld": true,
  "EnableJournalButton": true,
  "EnableCutsceneSkip": true,
  "EnableCarpenterMenuFix": true,
  "EnableFurnitureDebounce": true,
  "EnableConsoleFurniturePlacement": true,
  "EnableGameMenuNavigation": true,
  "FreeCursorOnSettings": false,
  "UseBumpersInsteadOfTriggers": false,
  "VerboseLogging": false
}
```

| Option | Description |
|--------|-------------|
| `EnableButtonRemapping` | Remap buttons based on controller layout and control style. When disabled, all buttons pass through unmodified |
| `ControllerLayout` | Physical button layout: `Switch`, `Xbox`, or `PlayStation` |
| `ControlStyle` | Control scheme: `Switch` (right=confirm) or `Xbox` (bottom=confirm) |
| `EnableConsoleChests` | Sort (X), fill stacks (Y), sidebar navigation, color picker, and A/Y item transfer in chests |
| `EnableConsoleShops` | A button purchases, LT/RT quantity selector, sell tab with A/Y, right stick scroll |
| `EnableConsoleToolbar` | 12-slot fixed toolbar with LB/RB row switching and LT/RT slot movement |
| `EnablePickupToActiveRow` | Steer picked-up items (furniture, forage, drops, gifts, purchases) into the toolbar row you're currently viewing instead of row 0 |
| `EnableConsoleInventory` | A picks up/places items, Y picks up one from stack, fishing rod bait/tackle via Y |
| `EnableConsoleShipping` | A ships full stack, Y ships one item from the shipping bin |
| `SuppressRightStickInOverworld` | Stop the right thumbstick from drifting the mouse cursor during overworld gameplay |
| `EnableJournalButton` | Tap Start opens the game menu; hold Start ~500ms opens the Quest Log/Journal |
| `EnableCutsceneSkip` | Press Start twice during a skippable cutscene to skip it |
| `EnableCarpenterMenuFix` | Prevent Robin's building menu from instantly closing + joystick farm view controls |
| `EnableFurnitureDebounce` | Prevent furniture from rapid-toggling between picked up and placed |
| `EnableConsoleFurniturePlacement` | Replace Android's multi-tile green-square placement map with a single ghost rectangle showing exactly where the furniture will land |
| `EnableGameMenuNavigation` | Controller navigation for all game menu tabs (Social, Collections, Crafting, Skills, Animals, Powers, Options) |
| `FreeCursorOnSettings` | Use free cursor (vanilla) instead of snap navigation on the Options page and GMCM config page |
| `UseBumpersInsteadOfTriggers` | Use LB/RB instead of LT/RT (for Xbox Bluetooth controllers) |
| `VerboseLogging` | Enable detailed debug logging |

## Building from Source

### Prerequisites
- .NET 6 SDK
- Stardew Valley installed (for reference assemblies)

### Build
```bash
cd AndroidConsolizer
dotnet build --configuration Release
```

Output: `bin/Release/net6.0/AndroidConsolizer X.X.X.zip`

## Troubleshooting

### Mod not loading
- Ensure SMAPI Android is properly installed
- Check the SMAPI log at `/storage/emulated/0/StardewValley/ErrorLogs/`

### Features not working
- Enable `VerboseLogging` in config to see detailed logs
- Check SMAPI log for error messages

### Toolbar not showing 12 slots
- Make sure `EnableConsoleToolbar` is `true` in config
- The feature only works during gameplay (not in menus)

### Triggers not working (Xbox controller)
- This is a known Android limitation with Xbox Bluetooth controllers
- Enable "Use Bumpers Instead of Triggers" in the mod settings as a workaround

## Compatibility

- **Stardew Valley Expanded**: Compatible
- **Content Patcher**: Compatible
- **Generic Mod Config Menu**: Compatible (optional)
- **Star Control**: NOT compatible with Android (don't use together)

## Known Issues

- Geode breaking menu works but has no visual feedback with controller
- Trash can lid animation doesn't play on Android with controller hover
- Museum donation menu requires touch for item placement (controller can't place items on the grid)

## TODO / Roadmap

- Right joystick free cursor mode for menus and gameplay
- Zoom control slider in options menu
- Museum donation menu controller support
- Geode breaking menu visual feedback
- Tool charging while moving (console parity)
- Slingshot aim-and-fire with controller
- Expanded controller testing (8BitDo, DualSense, etc.)

## Why "Consolizer"?

Android Stardew Valley has broken controller support that makes it nearly unplayable when docked to a TV (no touchscreen). This mod "consolizes" the experience - making it play like the Nintendo Switch version when using a controller.

## Also by this author

[**Nap Time**](https://www.nexusmods.com/stardewvalley/mods/42616) — Nap in bed to regain energy without ending the day. Configurable recovery speed and wake-up time cap. Works on PC and Android.

## Credits

- Created by sonofskywalker3
- Uses [SMAPI](https://smapi.io/) modding framework
- Android SMAPI port by [NRTnarathip](https://github.com/NRTnarathip/SMAPI-Android-1.6)

## License

MIT License - Feel free to modify and redistribute.

## Changelog

### 3.7.0 — Bug Fix Release 2
- **Picked-up items land in the active toolbar row** — picking up furniture, forage, drops, gifts, or shop purchases now places the item into the toolbar row you're currently viewing, instead of always defaulting to row 0. Furniture (your held tool until placed) also moves the selection to follow; non-tool pickups never disturb your selected tool. New `EnablePickupToActiveRow` GMCM toggle (default on).
- **Triggers no longer skip two toolbar slots** — on Gamesir and G Cloud controllers a single trigger pull occasionally moved two slots instead of one. Hall-effect triggers briefly drop to zero mid-pull and digital triggers glitch for a single tick; replaced the single-bool edge detector with a two-threshold state machine plus a 4-tick release-confirmation streak. Verified on G Cloud (analog) and Gamesir X2 (digital).
- **Diagnostic log noise reduced** — leftover `[Bed]` and `[StartHold]` debug lines downgraded from Info to Debug and gated behind the Verbose Logging toggle.
- Internally rolls up the v3.6.1–v3.6.9 patch series.

### 3.6.0 — Bug Fix Release
- **Bed bouncing on placement fixed** — patched `GameLocation.removeQueuedFurniture` to gate the full removal cascade. Place a bed and it stays placed.
- **Right stick no longer drifts the cursor in the overworld** — vanilla Android maps the right stick to mouse motion. New `SuppressRightStickInOverworld` GMCM toggle (default on).
- **Dresser no longer destroys clothes** — `ShippingBinPatches` source check tightened to the actual `ShippingBin`.
- **Aquarium no longer duplicates fish on take** — Y-button take-one now invokes the source container's removal hook.
- **Quest Log via Hold Start** — tap Start opens the GameMenu (vanilla), hold Start ≥500ms opens the Quest Log/Journal.
- **Storage shop polish** — dresser/aquarium sell-tab highlighting, deposit restrictions, hover refresh, and default buy-tab selection all match console/PC behaviour.
- **Console Furniture Placement** — replaces Android's misleading multi-tile green-square placement map with a single ghost rectangle plus a translucent furniture sprite over the placement target. New `EnableConsoleFurniturePlacement` GMCM toggle (default on).
- Other fixes: chest deposits fire bundle/quest hooks, equipment equip/unequip routed through `Farmer.Equip`, shop stock and post-purchase actions match vanilla, fishing rod tackle attaches correctly via controller, Auto-Grabber chests blocked from console-style A/Y deposits.

### 3.5.0 — The Chest & Menu Polish Release
- **CarpenterMenu Polish** — Building ghost now tracks cursor continuously in real time at all zoom levels. Building skin picker fully navigable with controller (A cycles skins). Fixed ghost/cursor speed mismatch, zoom-incorrect offsets, and GetMouseState override persisting after menu close.
- **Shop Cursor Fixes** — Visible cursor now appears on both buy and sell tabs at all shops (Blacksmith, Joja, and others where Android hid it). Left stick hold-to-repeat navigation added (15-tick delay, 4-tick repeat matching game timing).
- **Sell Tab Improvements** — Sell price tooltip works for all item types (weapons, rings, boots — not just objects). Items with 0g sell value greyed out on sell tab.
- **Equipment Slot Tooltips** — Hovering over equipment slots (hat, rings, boots, shirt, pants, trinkets) now shows item stats. Android's stripped `drawToolTip` call restored via postfix.
- **Community Center Bundle Fixes**
  - Completed bundle icons now show correct completion state on overview (no false incomplete icons)
  - Bundle reward (present) navigable with controller and properly clears after collection
  - B-close on reward menu preserves unclaimed rewards for next visit
  - Controller deposits into reward menus blocked (rewards are take-only)
  - Y blocked on reward menus (rewards are all-or-nothing per stack)
  - Vault bundles: A on purchase button pays for the bundle (cursor-based, single press)
  - LT/RT tab switching blocked when CC is opened from the junimo tile (only works from GameMenu)
  - Hover animation and tooltip properly clear when navigating between bundles and non-bundle components
- **Chest Tooltip Positioning** — Tooltips reposition below/above the slot with proper cursor clearance, adapting to screen size
- **Finger Cursor** — Red selection box replaced with finger cursor in all InventoryMenu contexts (chests, crafting, collections)
- **Dialogue Width Fix** — Dialogue boxes no longer get squished on small screens when custom toolbar is active
- **Watering Can Gauge** — Water level gauge renders correctly in inventory, chest, and toolbar contexts
- **Boot Freeze Fix** — Intermittent white screen freeze on SMAPI Android boot detected and auto-recovered
- **Button Remapping Toggle** — New `EnableButtonRemapping` config option to disable A/B and X/Y swaps independently of other features
- **Codebase cleanup** — Removed 166 lines of diagnostic patches and logging. All remaining debug logs gated behind VerboseLogging.

### 3.4.0 — The Game Menu Release
- **Social Tab Navigation** — Full controller support for the Social page
  - D-pad/thumbstick navigates villager list with scroll support
  - Right stick for fast scrolling with hold-to-accelerate
  - Scrollbar tracks your position as you navigate
  - A opens the gift log (ProfileMenu) for the selected villager
  - LB/RB switches between villagers inside the gift log
  - B returns to social tab, restoring scroll position
  - Cell content aligned properly (portrait, name, relationship status no longer overlap)
  - Gift log has a visible cursor
- **Collections Tab Navigation** — D-pad/thumbstick grid navigation across items and sub-tabs (Crops, Fish, Recipes, Achievements, etc.). Finger cursor replaces red highlight box.
- **Crafting Tab** — Finger cursor replaces red highlight box
- **Skills Page Navigation** — D-pad/thumbstick grid navigation across skill icons and level bars
- **Animals Tab Navigation** — D-pad/thumbstick navigates animal list
- **Powers Tab** — Finger cursor replaces glow highlight
- **Options Tab Navigation** — Left stick navigates options, A activates/toggles, right stick scrolls the options list. Dropdown menus open and close properly.
- **LT/RT Tab Switching** — Cycle between all game menu tabs with triggers (console parity). Works with CC tab too.
- **GMCM Controller Navigation** — Full d-pad/thumbstick snap navigation in [our GMCM fork](https://github.com/sonofskywalker3/GenericModConfigMenu). B navigates back through mod config → mod list → close with scroll position and selection preserved.
- **Chest Swap System** — When inventory or chest is full, A picks up an item for displacement swap. Navigate to any slot and press A to place it; the displaced item returns to the source. B cancels. Works both directions (chest→inventory, inventory→chest).
- **CarpenterMenu Build Fix** — Clicking "Build" for a building you can't afford no longer closes the menu
- **Chest Touch-Sim Fix** — Items no longer get re-selected after placing them via controller
- **Analog Trigger Fix** — Triggers properly debounced at hardware level, no more multi-slot jumps
- **Cutscene Skip Fix** — Press Start twice to skip cutscenes (uses native skip icon)
- **Chest Tool Transfer** — Y strips attachments from fishing rods/slingshots when transferring, A transfers loaded tools as-is
- **Codebase cleanup** — Removed dead diagnostic code, fixed GetState cache bug

### 3.3.0 — The Community Center Release
- **Community Center Bundle Navigation** — Full controller support for the JunimoNoteMenu
  - **Bundle overview**: D-Pad/thumbstick navigates between bundles, A opens the donation page
  - Cursor remembers your position when returning from the donation page (no reset to first bundle)
  - Correct bundle highlights as you navigate (hover animation tracks the cursor)
  - Custom cursor drawn on both overview and donation pages (Android suppresses the default cursor)
  - LB/RB switches between Community Center rooms (vanilla behavior, unchanged)
  - **Donation page**: Navigate 6-column inventory grid with D-Pad/thumbstick, A donates items
  - Right from last inventory column enters the ingredient list — see what each bundle slot needs
  - Navigate between ingredient rows, tooltips show item names
  - Left from ingredient list returns to inventory
- **Equipment Slot Fixes** — A button now works on all equipment slots (hat, rings, boots, shirt, pants)
  - Pick up equipment to cursor, place/swap equipment with A
  - Sort button (slot 106) and trash can handled correctly
- **Slingshot Ammo Management** — Same treatment as fishing rod bait/tackle
  - A on ammo picks it up to cursor, Y on slingshot attaches/detaches ammo
- **Touch Interrupt Fixes**
  - Touching the screen while holding an item with controller now safely returns it to its source slot
  - Drop zone between Sort and Trash buttons — A while holding an item drops it as debris at player's feet
  - Touch on chest no longer breaks sidebar navigation (self-healing component check)

### 3.2.0 — The Robin Release
- **Robin's Build Menu — Full Controller Support** - Build, Move, and Demolish all work with the joystick
  - Building ghost follows your cursor in real time across the farm
  - **Build mode**: Move cursor to desired location, press A to set the building position, press A again to place
  - **Move mode**: Press A to select a building, move cursor to new location, press A to confirm
  - **Demolish mode**: Press A on a building to highlight it green, press A again to demolish. Move cursor away to safely deselect without demolishing
  - Left stick pans the viewport when cursor reaches screen edges
  - Visible cursor rendered in farm view so you always know where you're pointing
- **Furniture Placement Fix** - Y button no longer rapid-toggles furniture between picked up and placed
  - One press = one interaction, works for all furniture types including beds
  - New `EnableFurnitureDebounce` config toggle (enabled by default)

### 3.1.0
- **Cleanup Update** - No behavior changes, internal housekeeping only
  - Consolidated 12 granular GMCM toggles into 5 feature groups: Console Chests, Console Shops, Console Toolbar, Console Inventory, Console Shipping
  - Removed dead code across ButtonRemapper, InventoryManagementPatches, ItemGrabMenuPatches, GameplayButtonPatches, and ModEntry
  - Cached all per-call reflection lookups (cutscene skip, inventory hover, chest sidebar buttons) for better performance
  - Cleaned up stale version references in log messages
  - **Note:** Existing `config.json` files will reset to defaults due to renamed config properties. Re-configure via GMCM if needed.

### 3.0.0
- **Console-Style Chest Item Transfer** - Chests now work like Nintendo Switch
  - **A button** instantly transfers full stack between chest and inventory (no selection step)
  - **Y button** transfers one item (hold Y for rapid single-item transfer)
  - **RB** snaps cursor to Fill Stacks button
  - Works bidirectionally for all chest types (regular chests, fishing treasure, fridge, etc.)
- **Chest Sidebar Navigation** - All sidebar buttons reachable via controller
  - Sort Chest, Fill Stacks, Color Toggle, Sort Inventory, Trash, Close X
  - Close X properly closes chest without reopening (A suppress-until-release)
- **Color Picker Swatch Navigation** - Full 7x3 grid navigation
  - Cursor snaps to first swatch on open, A selects color
  - B closes picker only (not the chest) via exitThisMenu same-tick guard
  - Visual stride detection probes picker at runtime for correct cursor positioning
  - Color preserved after probe (click at saved position to restore)

### 2.9.0
- **Shop Controls Overhaul** - Major improvements to shop controller experience
  - **LB/RB quantity adjustment** with hold-to-repeat (+/-1 in bumper mode, +/-10 otherwise)
  - **Right stick fast navigation** on buy tab — jump 5 items at a time with hold-to-repeat
  - **Controller button icon** on the tab-switch button — shows Y/X/square depending on layout, dims correctly on sell tab
  - **Touch tab button blocked** when controller connected — prevents accidental touchscreen taps toggling tabs
  - **Grayed-out item sell fix** — shops no longer let you sell items they don't accept
  - **Cutscene skip** — press Start twice to skip cutscenes
  - Right stick vanilla scroll desync fully fixed (vanilla scroll blocked at GamePad.GetState level)
  - Sell tooltip for unsellable items no longer shows

### 2.8.0
- **Release cleanup** - No behavior changes
  - Debug/Trace logging silenced by default (VerboseLogging now defaults to false)
  - All debug logs gated behind VerboseLogging toggle — enable in GMCM when needed
  - Removed unused legacy config properties
  - Y-sell feedback promoted to INFO for consistency with A-sell

### 2.7.x (2.7.1 — 2.7.21)
- **Carpenter Menu Fix** (v2.7.2-v2.7.4) - Robin's building menu no longer instantly closes
- **Fishing Mini-Game Fix** (v2.7.1) - X/Y button swap now applies during bobber bar
- **Shop Purchase Overhaul** (v2.7.5-v2.7.14) - Complete rewrite of purchase logic
  - Trade item shops (Desert Trader) now work correctly
  - Tool upgrades, recipes, and special purchases handled via actionWhenPurchased
  - Inventory-full refunds return both money and trade items
  - Fixed phantom purchases when switching between buy/sell tabs
  - Buy quantity no longer bleeds to sell tab
- **Console-Style Shop Selling** (v2.7.16-v2.7.21) - Full sell-tab controller support
  - A sells entire stack, Y sells one, hold Y for rapid sell
  - Sell price tooltip with gold coin icon positioned next to selected item
  - Snap-navigation-based item detection (hoveredItem doesn't work on Android sell tab)
- **Performance** (v2.7.15) - Cached reflection fields in InventoryManagementPatches

### 2.7.0
- **Console-Style Inventory Management** - A button now works like Nintendo Switch
  - A picks up entire stack to cursor, A again places or swaps
  - Y picks up a single item from a stack, hold Y for continuous pickup
  - Held items render visually at cursor slot position
  - Controller hover tooltips appear when navigating inventory
  - Fishing rod tooltip shown when holding bait or tackle (to see rod info before attaching)
- **Improved Fishing Rod Bait/Tackle** - Detaching bait or tackle now puts it on the cursor instead of first empty slot, consistent with console behavior

### 2.6.0
- **Fishing Rod Bait/Tackle Fix** - Controller support for attaching and detaching bait/tackle from fishing rods
  - Press A on bait or tackle to select it
  - Press Y on a fishing rod to attach the selected bait/tackle
  - Press Y on a fishing rod with nothing selected to detach (bait first, then tackle)
  - Supports stacking same bait type and swapping different bait/tackle

### 2.5.1
- **Fixed shop stock bug** - Limited stock items now properly decrement when purchasing partial quantities

### 2.5.0
- **Fixed X button inventory deletion bug** - Critical fix for Xbox layout + Switch style combination
- Renamed "Use D-Pad for Toolbar" to "Use Bumpers Instead of Triggers"
- Added LB/RB shop quantity adjustment when bumper mode is enabled
- Added "Start Opens Journal" feature
- Fully tested with Odin built-in controls and external Xbox Wireless controllers via Bluetooth

### 2.4.0
- **Xbox controller support** - Tested with Xbox Wireless Controller
- **Bumper mode** - Use LB/RB instead of triggers for toolbar and shops
- **Controller layout settings** - Support for Switch, Xbox, and PlayStation button layouts
- **Control style settings** - Choose between Switch-style or Xbox-style confirm/cancel

### 2.1.0
- **Console-style shipping bin** - Complete rewrite using game's native shipping flow
- A button ships entire stack, Y button ships one item
- "Last shipped" display now works properly
- Fixed toolbar selection box sizing

### 2.0.0
- **Rebranded to Android Consolizer**
- **Console-style toolbar** - 12-slot rows with LB/RB to switch rows, LT/RT to move within row
- Custom toolbar rendering that matches console layout

### 1.0.0
- Initial stable release
- Shop purchasing (A button)
- Inventory/chest sorting (X button)
- Add to stacks in chests (Y button)
- X button deletion bug blocked
