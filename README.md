# Android Consolizer

[**⬇ Download on Nexus Mods**](https://www.nexusmods.com/stardewvalley/mods/41869)

Makes Android Stardew Valley's controller support work like the Nintendo Switch version. 90+ fixes across shops, inventory, chests, toolbar, Robin's building menu, Community Center bundles, furniture placement, every game menu tab — and the right-stick free cursor.

If you play on a handheld (Odin, Ayaneo, Retroid, etc.) or dock your phone/tablet to a TV, this mod makes the game actually playable without a touchscreen.

> This README mirrors the [Nexus Mods description](https://www.nexusmods.com/stardewvalley/mods/41869); the two are kept identical (the "Building from source" / "Troubleshooting" sections below are GitHub-only). Full version history lives in [docs/CHANGELOG.md](docs/CHANGELOG.md).

> **🎉 Feature complete — v4.0.0.** With the right stick now working like the Switch, Android Consolizer covers the full console control scheme, so I'm considering the mod **feature complete**. If I missed something, let me know — I'll keep fixing any reported bugs, and I'll consider feature requests that bring it closer to full console parity.

## What's New in v4.0.0 — The Right Stick Update

**The right stick is now the console free cursor** — the last piece of the Switch control scheme that Android was missing. Every group is individually toggleable via GMCM.

- **Right-stick cursor** — the right stick moves an on-screen cursor that aims your tools, interaction, and placement at the tile under it, exactly like the Switch. Swing a tool and it hits the cursor's tile (diagonals included); move the cursor onto a chest or villager and the action targets it. The cursor auto-hides after ~4 seconds of no input, reverting tools to the tile you're facing.
- **Contextual cursor** — the cursor shows a hand over things you can grab or use (chests, the mailbox) and a magnifying glass over things you can inspect, like console.
- **Place with the cursor** — furniture, machines, and seeds place under the right-stick cursor; A places, B cancels.
- **Tool-hit box follows through** — the optional "always show tool hit location" box tracks the cursor while it's up and snaps back to your facing tile when it fades.
- **Settings that stick** — the in-game Zoom level and the tool-hit-location options now survive a full restart (Android wasn't saving them), and options you change with the controller are saved the same as touch.
- **Snappier trigger toolbar** — changing toolbar slots quickly with LT/RT no longer drops presses.
- **Fertilizer shows a single target box** — like seeds, instead of the screen-wide green map.

## Previous releases

Full version history is in [docs/CHANGELOG.md](docs/CHANGELOG.md). Recent highlights: **v3.9.0 — Console Parity: Big Systems** (charge tools while moving, console slingshot aiming, drop blocker, restored Options + working zoom, dialogue choice boxes); **v3.8.0 — Console Parity: Quick Wins** (resizable toolbar, museum donations, geode breaking, Load Game/title cursors, multi-page mail, and more).

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
- Optional "always show tool hit location" box — the tile your hoe/can/pickaxe/axe will hit (toggled on the Options page); it follows the right-stick cursor and snaps back to your facing tile when the cursor fades

### Right Stick (Console Cursor)
- The right stick moves an on-screen cursor in the overworld, exactly like the Switch — it aims tool swings, tile interaction, and item placement at the tile under it (diagonals included)
- Contextual sprite: a hand over interactables (chests, the mailbox) and a magnifying glass over things you can inspect
- Auto-hides ~4 seconds after the last stick input, reverting tools to the tile you're facing
- Furniture, machine, and seed placement ghosts follow the cursor; A places, B cancels
- Toggleable (Right Stick Cursor); when off, the right stick is zeroed in the overworld (no cursor drift)

### Other Fixes
- Fishing rod bait/tackle: A picks up bait/tackle, Y on the rod attaches or detaches
- Slingshot ammo: same pattern — A picks up ammo, Y on the slingshot attaches/detaches
- Cutscene skip: press Start twice during skippable cutscenes
- Start button: tap = Game Menu, hold ~½ sec = Quest Log/Journal
- Dialogue choice boxes open on the first option with a red outline + finger cursor (console look, no yellow highlight)
- Settings persistence: the in-game Zoom level and the tool-hit-location options survive a full restart, and options changed with the controller are saved the same as touch
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

Every feature is individually toggleable via [Generic Mod Config Menu](https://github.com/sonofskywalker3/GenericModConfigMenu/releases) or `config.json`: Controller Layout (Switch / Xbox / PlayStation), Control Style, Console Chests, Console Shops, Console Toolbar, Console Inventory, Console Shipping, Console Menus, Console Furniture Placement, Console Museum Donation + Geode menus, Move While Charging, Console Slingshot Aim, Console Drop Blocker, Console Dialogue Cursor, Pickup To Active Row, Hold To Craft, Right Stick Cursor, Carpenter Menu Fix, Furniture Debounce, Hold Start for Quest Log, Bumper Mode, and more. (Zoom level and the tool-hit-location checkboxes live on the in-game Options page and now persist across restarts.)

## Also by this author

- [**Cart Catalog**](https://www.nexusmods.com/stardewvalley/mods/47146) — Order from the Traveling Cart's daily stock; items arrive in a package on your porch the next morning.
- [**Nap Time**](https://www.nexusmods.com/stardewvalley/mods/42616) — Nap in bed to recover energy without ending the day. Configurable rate and wake-up cap. PC + Android.
- [**The Longest Year**](https://www.nexusmods.com/stardewvalley/mods/47192) — A roguelite time-loop: restore the Community Center within a year, or the Junimos rewind the seasons and you begin again, a little stronger. (Beta)

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
