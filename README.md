# Android Consolizer

[**⬇ Download on Nexus Mods**](https://www.nexusmods.com/stardewvalley/mods/41869)

Makes Android Stardew Valley's controller support work like the Nintendo Switch version. 90+ fixes across shops, inventory, chests, toolbar, Robin's building menu, Community Center bundles, furniture placement, and every game menu tab.

If you play on a handheld (Odin, Ayaneo, Retroid, etc.) or dock your phone/tablet to a TV, this mod makes the game actually playable without a touchscreen.

> This README mirrors the [Nexus Mods description](https://www.nexusmods.com/stardewvalley/mods/41869); the two are kept identical (the "Building from source" / "Troubleshooting" sections below are GitHub-only). Full version history lives in [docs/CHANGELOG.md](docs/CHANGELOG.md).

## What's New in v3.9.0 — Console Parity: Big Systems

**The big real-time systems now play like the Switch.** This release moves past menus into how the game *feels* in your hands — charging tools while you walk, aiming the slingshot the console way — plus a sweep over the dialogs and Options page that still felt touch-first. Every group is individually toggleable via GMCM.

- **Charge tools while moving** — hold an upgraded Hoe or Watering Can while walking and it charges its area effect like console, instead of rapid-firing single uses and locking you in place. (Basic tools, Pickaxe and Axe are unaffected.)
- **Console slingshot aiming** — move freely with a slingshot equipped; hold the tool button to draw and aim with the left stick, release to fire once. Aim is the Switch's direct aim, not Android's mirrored, straight-down default.
- **Drop blocker** — you can no longer instantly re-grab an item the moment you drop it; the game's own short pickup delay now engages, just like console. Monster loot and harvested crops are unaffected.
- **Seeds show a single target box** — planting seeds now shows one console-style red/green box on the target tile instead of lighting up every tilled tile on screen.
- **Shop list stays put after a purchase** — buying an item no longer jumps the buy list to the top (or off-screen); your place in the list holds.
- **Restored Options + working zoom** — the in-game Options page now carries the console "Always show tool hit location" checkboxes and a **Zoom Level** slider that actually moves the view; touch-only joypad options are hidden when a controller is connected, and the page scrolls far enough to reach the Mod Options (GMCM) button.
- **Dialogue choice boxes match console** — question prompts (the "Go to sleep?" Yes/No, NPC yes/no, festival choices) now open with the first option selected and show a red outline + finger cursor instead of the yellow highlight.

## Recent fixes — v3.8.4–v3.8.5

**Seeds plant with no ghost** — the single-tile placement preview no longer catches crop seeds, which plant straight into tilled soil. Furniture and machine/sprinkler ghosts are unchanged.

**Switch Pro controllers switch toolbar items** — digital-only ZL/ZR triggers are now read, so item-switching keeps working after a toolbar row change. Thanks to kabusann2008.

**Buying recipes teaches them** — purchasing a cooking or crafting recipe with a gamepad now actually learns the recipe, exactly like the vanilla buy flow. Thanks to Rizkyrahmadhani12.

**Note for Switch Pro users:** when a Pro controller is paired to a non-Nintendo Android device, the system usually reports its buttons in Xbox positions. If your face buttons feel swapped, set **Controller Layout = Xbox** and keep **Control Style = Switch**. A proper auto-detecting fix is planned.

## Also in v3.8 — Console Parity: Quick Wins

A big batch of console-parity fixes — the menus that still felt touch-only on a controller now behave like the Switch version. Each group is individually toggleable via GMCM.

- **Resizable toolbar** — the Options "Toolbar Slot Size" slider finally works (icons and stack/quality numbers scale with it), capped so the full row clears the energy bar. "Toolbar Padding" lifts the toolbar off the screen edge.
- **Museum donations & rearranging with a controller** — visible cursor, D-pad selection, A to donate, and full grid navigation when rearranging.
- **Geode breaking feels like console** — single A-press to crack, the geode auto-selected with its tooltip, and spatial navigation between geodes.
- **Load Game & title screen cursors** — proper cursor + snap navigation (slot selection, scrolling, delete-confirmation dialog).
- **Adventurer's Guild & multi-page mail** — the monster eradication list and multi-page letters are fully navigable.
- **Plus:** Community Center bundle donation greys out ineligible items, the Dwarvish Translation Guide reward is learned (not dumped in your bag), single-tile placement ghost for craftables, hold-A to craft, and dialogue answer boxes start on the first option.

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
- Resizable via the Options "Toolbar Slot Size" slider (capped to fit the screen); "Toolbar Padding" lifts it off the edge

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
- Single-tile placement ghost for craftables too (sprinklers, machines)
- Seeds and fertilizer show a single red/green target box, not the touch-style screen-wide green map
- Bed placement no longer bounces back to inventory
- Y no longer rapid-toggles — one press = one interaction

### Robin's Building Menu
- Menu no longer instantly closes when opened with controller
- Full joystick control in farm view — left stick moves cursor, pans viewport at edges
- Build mode: A confirms placement at cursor position
- Move mode: A selects building, move cursor, A confirms new location
- Demolish mode: A highlights building (green), A again confirms; move off to deselect safely
- Building skin picker navigable with controller

### Community Center Bundles
- Bundle overview: D-pad/thumbstick navigates bundles, A opens donation page
- Cursor remembers your position when returning from the donation page
- Donation page: navigate the 6-column inventory grid, A donates items; ineligible items greyed out
- Navigate right into the ingredient list to see what each slot needs
- Vault bundles: A on the purchase button pays for the bundle
- Bundle rewards: navigate to the present, A opens rewards, A takes stacks
- Custom cursor drawn (Android suppresses the default one here)

### Museum & Geodes
- Museum: visible cursor, D-pad selection, A donates artifacts; rearrange the layout with full grid navigation
- Geode menu: single A-press cracks, selected geode auto-highlighted with tooltip, spatial navigation between geodes

### Title & Menus
- Load Game and title screens: visible cursor + snap navigation (slot select, scroll, delete-confirmation dialog)
- Adventurer's Guild monster eradication list and multi-page mail fully navigable

### Game Menu Tabs
- **Social**: navigate the villager list, right stick fast scroll, A opens the gift log, LB/RB switches villagers in the gift log
- **Collections**: grid navigation across items and sub-tabs, finger cursor
- **Crafting**: finger cursor replaces the red highlight; hold A to craft continuously
- **Skills**: grid navigation across skill icons and level bars
- **Animals**: navigate the animal list
- **Powers**: finger cursor replaces the glow highlight
- **Options**: left stick navigates, A activates, right stick scrolls, dropdowns work; plus the restored "tool hit location" checkboxes and a working **Zoom Level** slider, with touch-only joypad options hidden under a controller
- **LT/RT** switches between all tabs (console parity)

### Gameplay (Console-Style)
- Charge an upgraded Hoe or Watering Can while walking — hold the tool button and move to charge its area effect, release to fire, instead of rapid-firing single uses and stopping in place
- Slingshot: move freely with it equipped, hold the tool button to draw and aim with the left stick, release to fire once (Switch-style direct aim)
- Drop blocker — you can't instantly re-grab an item you just dropped; the game's own short pickup delay engages, like console (monster loot and harvested crops unaffected)
- Optional "always show tool hit location" box — the tile your hoe/can/pickaxe/axe will hit, toggled on the Options page

### Other Fixes
- Fishing rod bait/tackle: A picks up bait/tackle, Y on the rod attaches or detaches
- Slingshot ammo: same pattern — A picks up ammo, Y on the slingshot attaches/detaches
- Cutscene skip: press Start twice during skippable cutscenes
- Start button: tap = Game Menu, hold ~½ sec = Quest Log/Journal
- Dialogue choice boxes open on the first option with a red outline + finger cursor (console look, no yellow highlight)
- Right stick suppressed in the overworld (no more cursor drift)
- GMCM controller navigation via our [controller-enabled GMCM fork](https://github.com/sonofskywalker3/GenericModConfigMenu)

### Bumper Mode
For controllers where triggers aren't detected (e.g. Xbox via Bluetooth):
- D-Pad Up/Down switches toolbar rows, LB/RB moves within a row
- LB/RB adjusts shop purchase quantity

## Tested Controllers

- **AYN Odin Pro** (built-in) — Fully working
- **Ayaneo Pocket Air Mini** (built-in) — Fully working
- **EasySMX S10** on tablet — Fully working
- **Gamesir X2** — Fully working (digital triggers)
- **Nintendo Switch Pro** — Working (digital triggers supported as of v3.8.4; may need Controller Layout = Xbox on some Android devices — see the v3.8.4 note above)
- **Xbox Bluetooth** — Working (triggers need bumper mode)
- **Logitech G Cloud** — Fully working (primary test device)

If you test on other hardware, let me know!

## Requirements

- Stardew Valley 1.6.15+ (Android)
- [SMAPI 4.0+ for Android](https://github.com/NRTnarathip/SMAPI-Android-1.6) by NRTnarathip
- (Optional, recommended) [Generic Mod Config Menu](https://github.com/sonofskywalker3/GenericModConfigMenu/releases) — our fork adds controller navigation

## Configuration

Every feature is individually toggleable via [Generic Mod Config Menu](https://github.com/sonofskywalker3/GenericModConfigMenu/releases) or `config.json`: Controller Layout (Switch / Xbox / PlayStation), Control Style, Console Chests, Console Shops, Console Toolbar, Console Inventory, Console Shipping, Console Menus, Console Furniture Placement, Console Museum Donation + Geode menus, Move While Charging, Console Slingshot Aim, Console Drop Blocker, Console Dialogue Cursor, Pickup To Active Row, Hold To Craft, Suppress Right Stick in Overworld, Carpenter Menu Fix, Furniture Debounce, Hold Start for Quest Log, Bumper Mode, and more. (Zoom level and the tool-hit-location checkboxes live on the in-game Options page.)

## Also by this author

- [**Cart Catalog**](https://www.nexusmods.com/stardewvalley/mods/47146) — Order from the Traveling Cart's daily stock; items arrive in a package on your porch the next morning.
- [**Nap Time**](https://www.nexusmods.com/stardewvalley/mods/42616) — Nap in bed to recover energy without ending the day. Configurable rate and wake-up cap. PC + Android.

## Source

Open source (MIT) — [github.com/sonofskywalker3/AndroidConsolizer](https://github.com/sonofskywalker3/AndroidConsolizer)

---

<!-- GitHub-only appendix (not part of the Nexus description) -->

## Building from source

**Prerequisites:** .NET 6 SDK, and Stardew Valley installed (for reference assemblies).

```bash
cd AndroidConsolizer
dotnet build --configuration Release
```

Output: `bin/Release/net6.0/AndroidConsolizer X.X.X.zip`

## Troubleshooting

- **Mod not loading** — ensure SMAPI Android is installed; check the SMAPI log at `/storage/emulated/0/StardewValley/ErrorLogs/`.
- **Features not working** — enable `VerboseLogging` in config and check the SMAPI log for errors.
- **Toolbar not showing 12 slots** — make sure `EnableConsoleToolbar` is `true`; the feature only applies during gameplay (not in menus).
- **Triggers not working (Xbox controller)** — a known Android limitation with Xbox Bluetooth controllers; enable "Use Bumpers Instead of Triggers" as a workaround.

## Compatibility

- **Stardew Valley Expanded** — compatible
- **Content Patcher** — compatible
- **Generic Mod Config Menu** — compatible (optional)
- **Star Control** — NOT compatible on Android (don't use together)

## Changelog

Full version history: [docs/CHANGELOG.md](docs/CHANGELOG.md).

## Credits

- Created by sonofskywalker3
- Uses the [SMAPI](https://smapi.io/) modding framework
- Android SMAPI port by [NRTnarathip](https://github.com/NRTnarathip/SMAPI-Android-1.6)

License: MIT — feel free to modify and redistribute.
