# Changelog

All notable changes to Android Consolizer, newest first. The README's "What's New" mirrors the latest entries; this file is the full history.

## 4.0.0 — The Right Stick Update (feature complete)
Bundles everything since v3.9.0 (internal iteration builds v3.9.1–v3.9.23). The right stick becomes the console free cursor — the last missing piece of the Switch control scheme — so the mod is now considered feature complete (bug fixes + parity-closing requests still welcome). All in `Patches/RightStickCursorPatches.cs` plus small wiring, every group toggleable via GMCM.
- **Right-stick overworld cursor (#12, #62)** — the engine's native right-stick cursor path (`Game1.UpdateControlInput`) was being starved by AC zeroing the right stick. Now un-starved + self-drawn (Android renders no overworld cursor sprite): the cursor aims tool swings (incl. diagonals via a `Character.GetToolLocation(bool)` prefix), tile interaction (a `Game1.pressActionButton` prefix sets `lastCursorMotionWasMouse` at the gate read-site), and furniture/machine/seed placement at the tile under it; auto-hides after `timerUntilMouseFade` (~4s), reverting to the facing tile; no center-snap (`setMousePositionRaw` postfix). Toggle `EnableRightStickCursor` (replaces `SuppressRightStickInOverworld`).
- **Contextual cursor sprite (#79)** — `ResolveContextualCursor` replicates the engine's cursor pick (which `drawMouseCursor` computes then resets before our `RenderedHud` draw) from the persisted tile-hint flags: grab over actionable tiles/objects (chests, mailbox), look over inspectables; plus an un-petted-animal grab. (NPC speech-bubble/gift dropped — per-frame `checkForCharacterInteractionAtTile` cost, and the engine only sets it for NPCs with pending dialogue.)
- **#76 tool-hit box reverts to facing tile** — the "always show tool hit location" box targets `GetToolLocation(getMousePosition())` and stuck at the stale cursor after a fade; a `Character.GetToolLocation(Vector2,bool)` prefix forces the facing tile when `!wasMouseVisibleThisFrame`.
- **#77 settings persistence** — Android's `StartupPreferences` doesn't save the zoom (`[XmlIgnore]`) and AC's controller toggles bypassed vanilla's per-change `SaveStartupPreferences`. Zoom is now mirrored in AC config + re-applied on `SaveLoaded`; all other serializable options persist via `SaveStartupPreferences` on options-page close (whenever the page was used).
- **#54 faster trigger re-pull** — `TriggerReleaseConfirmTicks` 4→2 so rapid LT/RT slot changes register (was dropping fast re-pulls; log-verified 107/107).
- **#73 fertilizer single target box** — fertilizer (Category -19) joins seeds in the box-only placement branch instead of drawing the craftable ghost.
- **Perf/hygiene** — eliminated two verbose-logging floods (the right-stick diagnostic and the per-frame "Triggers raw" line) that were stalling Android input; gated the last ungated inventory debug logs behind VerboseLogging.

## 3.9.0 — Console Parity: Big Systems
Bundles everything since v3.8.5 (internal iteration builds v3.8.6–v3.8.40). The big real-time gameplay systems plus a dialog/Options pass. Every group toggleable via GMCM.
- **Charge tools while moving (#25)** — holding an upgraded Hoe/Watering Can while walking rapid-fired single uses and locked movement instead of charging. Two root causes (the held-button auto-repeat re-zeroing the charge at `Game1.cs:13640`, and the Android tap-to-move teardown `_mobileUpdateControlInput` stalling the charge on movement). `Patches/ToolUsePatches.cs` Stage 1 (one tool-use begin per hold) + Stage 2 (re-assert charge-hold after the tap-to-move teardown so the engine's own ramp runs). Upgraded Hoe + Watering Can; `EnableMoveWhileCharging`.
- **Console slingshot aiming (#25b)** — Android's tap-to-move layer injected the slingshot's use-tool events from stick/tap (stick alone fired it, holding auto-fired, movement blocked), and `Options.useLegacySlingshotFiring` defaulted true (mirrored/down-default aim). `Patches/SlingshotAimPatches.cs` postfixes `_mobileUpdateControlInput` to drive the use-tool flags from the physical button only (hold→aim with left stick, release→fire once) and forces non-legacy direct aim. `EnableSlingshotAim`.
- **Console drop blocker (#74)** — Android never set `Debris.DroppedByPlayerID`, so the vanilla ~1.2s self-pickup exclusion was dormant and you could instantly re-grab your own drops. `TagAsPlayerDrop` now sets it on deliberate player drops, re-engaging the engine's own exclusion. Loot/harvest untouched. `EnableConsoleDropBlocker`.
- **Seed/fertilizer single target box (#73)** — the seed branch in `FurniturePlacementPatches` now draws the single console red/green box on the target tile (no ghost) instead of the touch-style screen-wide green map.
- **Shop buy-list scroll holds after a purchase (#75)** — the Android buy list scrolls via the MobileScrollbox `scrollArea` pixel offset (not the dead `currentItemIndex` PC vestige). `RebuildSaleButtonsAndRestoreSnap` now captures/restores the offset across a rebuild, and `ClampScrollTop` pins the DPad-up over-scroll at 0.
- **Restored Options page + working zoom (#77, supersedes #76)** — new `Patches/OptionsPageInjectionPatches.cs` injects the native tool-hit checkboxes (whichOption 11/12) and a Zoom Level slider (18, range 50–200) that drives the mobile PinchZoom pipeline (`PinchZoom.Instance.SetZoomLevel` + `Window_ClientSizeChanged`, since `desiredBaseZoomLevel` alone is ignored on mobile); hides the 5 touch-only joypad options under a controller; and recomputes the scroll floor each frame so the GMCM button is reachable. Zoom-slider thumb position corrected on first open (re-trigger the value setter after widening min/max).
- **Console dialogue choice boxes (#78)** — a `DialogueBox.draw` postfix replaces vanilla Android's yellow highlight + faded text on the selected question response with the console look: plain box, full-strength text, a maroon outline, and the menu finger cursor at the bottom-right; the box opens with the first option selected. `EnableConsoleDialogueCursor`. (Key Android gotcha: the gamepad gate `gamepadControls && !lastCursorMotionWasMouse` bails on a controller because Android reports the controller confirm as a synthesized touch — gate on the config toggle instead.)

## 3.8.5 — Seed Planting Ghost Fix
- **Seeds no longer show a placement ghost while planting.** The single-tile placement preview added in v3.8 for machines and sprinklers was also catching crop seeds, so a translucent seed-packet sprite appeared in front of your character as you planted. Seeds plant straight into tilled soil and never needed a preview — they now plant cleanly with no ghost. Furniture and machine/sprinkler placement ghosts are unchanged.

## 3.8.4 — Switch Pro Controller Trigger Fix
- **Digital-only triggers now drive toolbar item-switching (Nexus bug #1087126)** — Nintendo Switch Pro ZL/ZR report as pure digital buttons on many Android devices (analog axis pinned at 0). The mod's trigger pipeline was gated entirely on the analog value, so its own slot-navigation never ran; the game's native `pressSwitchToolButton` handled the trigger instead, and a bumper-armed slot lock then froze item-switching after a row change until a D-pad press. The effective trigger value now folds in the digital flag, so the mod handles digital triggers itself (item-switching survives row swaps) and the native handler is suppressed. Also hardens analog triggers against hall-effect mid-pull dropouts. Device-verified (S26 + Switch Pro). Thanks to kabusann2008.
- Internally rolls up the v3.8.2–v3.8.3 Switch Pro investigation diagnostic builds (now gated behind Verbose Logging).

## 3.8.1 — Recipe Purchase Fix
- **Recipes are learned when bought with a controller (Nexus bug #1087126)** — with `EnableConsoleShops` on, buying a cooking/crafting recipe via gamepad charged the player but never taught the recipe. Our purchase path calls `Object.actionWhenPurchased` directly, which only returns `isRecipe.Value` and does not learn the recipe — vanilla `ShopMenu.purchaseItem` learns it via a separate `Item.LearnRecipe()` call afterward. The mod now mirrors that step (learn + `newRecipe` sound) in the handled branch. Verified on G Cloud (Dehydrator + Birch Syrup recipes learned cleanly). Pre-existing bug, not a 3.8.0 regression.

## 3.8.0 — Console Parity: Quick Wins
- **Resizable toolbar (#27)** — AC's custom toolbar draw now reads the vanilla `Options.toolbarSlotSize` slider (id 148) instead of a hardcoded 64px, scaling the 12 slots, icons, and stack/quality/gauge overlays. Size is capped so the centered row clears the bottom-right energy/health HUD. The "Toolbar Padding" slider (id 134), previously dead under the centered toolbar, is repurposed as a docked-edge gap. Fixes the public "Can't resize the toolbar" report.
- **Museum donation + rearrange via controller (#18)** — visible cursor (Android suppressed `drawMouse` because it reads the control type as touch), D-pad selection, A to donate, and grid navigation while rearranging (`reOrganizing` set so `receiveKeyPress` walks the museum grid). `EnableMuseumDonationController` toggle.
- **Geode menu console parity (#19)** — single-press A cracks (A→X redirect down the vanilla path), the selected geode auto-selects with its tooltip shown, and geode-only spatial navigation replaces the linear scan. `EnableConsoleGeodeMenu` toggle. (Landmine recorded: Harmony-patching `GeodeMenu.releaseLeftClick` hard-crashes the Android runtime — never patch it.)
- **Load Game & title screen cursors (#35, #17)** — cursor draw + snap navigation on the Load Game menu (entry snap, scrolling, delete-confirmation dialog) and the title screen, matching console's snap-as-indicator behaviour.
- **Adventurer's Guild kill list + multi-page mail (#39)** — monster eradication goals and multi-page `LetterViewerMenu` letters are navigable on a controller.
- **Bundle donation grey-out (#46)** — ineligible items grey out on the Community Center donation page, mirroring the sell-tab pattern.
- **Dwarvish Translation Guide reward (#71)** — the museum reward book is consumed into the skill (`canUnderstandDwarves`) instead of being dumped in the bag.
- **Single-tile craftable placement (#68)** + **hold-A to craft (#69)** + **dialogue answer boxes default to the first option (#22b)**.
- **Trigger column-skip refinement (#54b)** — re-arms slot enforcement on LB/RB row switch so the first trigger press after a row change lands on the right slot.
- **Investigated, no change:** the Community Center "missed rewards" container (#47) works correctly on Android — it's the small vanilla giftbox sprite that's easy to overlook, not a bug.
- Internally rolls up the v3.7.1–v3.7.75 patch series.

## 3.7.0 — Bug Fix Release 2
- **Picked-up items land in the active toolbar row** — picking up furniture, forage, drops, gifts, or shop purchases now places the item into the toolbar row you're currently viewing, instead of always defaulting to row 0. Furniture (your held tool until placed) also moves the selection to follow; non-tool pickups never disturb your selected tool. New `EnablePickupToActiveRow` GMCM toggle (default on).
- **Triggers no longer skip two toolbar slots** — on Gamesir and G Cloud controllers a single trigger pull occasionally moved two slots instead of one. Hall-effect triggers briefly drop to zero mid-pull and digital triggers glitch for a single tick; replaced the single-bool edge detector with a two-threshold state machine plus a 4-tick release-confirmation streak. Verified on G Cloud (analog) and Gamesir X2 (digital).
- **Diagnostic log noise reduced** — leftover `[Bed]` and `[StartHold]` debug lines downgraded from Info to Debug and gated behind the Verbose Logging toggle.
- Internally rolls up the v3.6.1–v3.6.9 patch series.

## 3.6.0 — Bug Fix Release
- **Bed bouncing on placement fixed** — patched `GameLocation.removeQueuedFurniture` to gate the full removal cascade. Place a bed and it stays placed.
- **Right stick no longer drifts the cursor in the overworld** — vanilla Android maps the right stick to mouse motion. New `SuppressRightStickInOverworld` GMCM toggle (default on).
- **Dresser no longer destroys clothes** — `ShippingBinPatches` source check tightened to the actual `ShippingBin`.
- **Aquarium no longer duplicates fish on take** — Y-button take-one now invokes the source container's removal hook.
- **Quest Log via Hold Start** — tap Start opens the GameMenu (vanilla), hold Start ≥500ms opens the Quest Log/Journal.
- **Storage shop polish** — dresser/aquarium sell-tab highlighting, deposit restrictions, hover refresh, and default buy-tab selection all match console/PC behaviour.
- **Console Furniture Placement** — replaces Android's misleading multi-tile green-square placement map with a single ghost rectangle plus a translucent furniture sprite over the placement target. New `EnableConsoleFurniturePlacement` GMCM toggle (default on).
- Other fixes: chest deposits fire bundle/quest hooks, equipment equip/unequip routed through `Farmer.Equip`, shop stock and post-purchase actions match vanilla, fishing rod tackle attaches correctly via controller, Auto-Grabber chests blocked from console-style A/Y deposits.

## 3.5.0 — The Chest & Menu Polish Release
- **CarpenterMenu Polish** — Building ghost now tracks cursor continuously in real time at all zoom levels. Building skin picker fully navigable with controller (A cycles skins). Fixed ghost/cursor speed mismatch, zoom-incorrect offsets, and GetMouseState override persisting after menu close.
- **Shop Cursor Fixes** — Visible cursor now appears on both buy and sell tabs at all shops (Blacksmith, Joja, and others where Android hid it). Left stick hold-to-repeat navigation added (15-tick delay, 4-tick repeat matching game timing).
- **Sell Tab Improvements** — Sell price tooltip works for all item types (weapons, rings, boots — not just objects). Items with 0g sell value greyed out on sell tab.
- **Equipment Slot Tooltips** — Hovering over equipment slots (hat, rings, boots, shirt, pants, trinkets) now shows item stats. Android's stripped `drawToolTip` call restored via postfix.
- **Community Center Bundle Fixes** — completed bundle icons show correct state; reward present navigable and clears after collection; B-close preserves unclaimed rewards; controller deposits into reward menus blocked (take-only); Y blocked on reward menus (all-or-nothing); vault bundles pay via A on the purchase button; LT/RT tab switching blocked when CC is opened from the junimo tile; hover animation/tooltip clear correctly between components.
- **Chest Tooltip Positioning** — repositions below/above the slot with proper cursor clearance, adapting to screen size.
- **Finger Cursor** — red selection box replaced with finger cursor in all InventoryMenu contexts (chests, crafting, collections).
- **Dialogue Width Fix** — dialogue boxes no longer get squished on small screens when the custom toolbar is active.
- **Watering Can Gauge** — water level gauge renders correctly in inventory, chest, and toolbar contexts.
- **Boot Freeze Fix** — intermittent white screen freeze on SMAPI Android boot detected and auto-recovered.
- **Button Remapping Toggle** — new `EnableButtonRemapping` option to disable A/B and X/Y swaps independently of other features.
- **Codebase cleanup** — removed 166 lines of diagnostic patches and logging; all remaining debug logs gated behind VerboseLogging.

## 3.4.0 — The Game Menu Release
- **Social Tab Navigation** — D-pad/thumbstick navigates the villager list with scroll; right stick fast-scrolls with hold-to-accelerate; scrollbar tracks position; A opens the gift log (ProfileMenu); LB/RB switches villagers in the gift log; B returns restoring scroll; cells aligned; gift log has a visible cursor.
- **Collections Tab Navigation** — grid navigation across items and sub-tabs; finger cursor replaces the red highlight.
- **Crafting Tab** — finger cursor replaces the red highlight.
- **Skills Page** — grid navigation across skill icons and level bars.
- **Animals Tab** — navigates the animal list.
- **Powers Tab** — finger cursor replaces the glow highlight.
- **Options Tab** — left stick navigates, A activates/toggles, right stick scrolls; dropdowns open/close.
- **LT/RT Tab Switching** — cycle all game-menu tabs with triggers (console parity), CC tab included.
- **GMCM Controller Navigation** — full snap navigation in [our GMCM fork](https://github.com/sonofskywalker3/GenericModConfigMenu); B navigates back through config → list → close, preserving scroll/selection.
- **Chest Swap System** — when inventory/chest is full, A picks up an item for displacement; place at any slot with A and the displaced item returns to source; B cancels; works both directions.
- **CarpenterMenu Build Fix** — clicking "Build" for a building you can't afford no longer closes the menu.
- **Chest Touch-Sim Fix** — items no longer get re-selected after placing them via controller.
- **Analog Trigger Fix** — triggers debounced at the hardware level, no more multi-slot jumps.
- **Cutscene Skip Fix** — press Start twice to skip cutscenes (uses native skip icon).
- **Chest Tool Transfer** — Y strips attachments from fishing rods/slingshots when transferring; A transfers loaded tools as-is.
- **Codebase cleanup** — removed dead diagnostic code, fixed a GetState cache bug.

## 3.3.0 — The Community Center Release
- **Community Center Bundle Navigation** — bundle overview navigates with D-pad/thumbstick, A opens the donation page; cursor remembers position on return; correct bundle highlights; custom cursor on both pages; LB/RB switches CC rooms; donation page navigates the 6-column inventory grid with A to donate; right from the last column enters the ingredient list (tooltips show needs); left returns to inventory.
- **Equipment Slot Fixes** — A works on all equipment slots (hat, rings, boots, shirt, pants); pick up/place/swap with A; Sort button and trash handled.
- **Slingshot Ammo Management** — A on ammo picks it up, Y on slingshot attaches/detaches.
- **Touch Interrupt Fixes** — touching the screen while holding an item safely returns it to source; drop zone between Sort and Trash drops the held item as debris; touch on a chest no longer breaks sidebar navigation.

## 3.2.0 — The Robin Release
- **Robin's Build Menu — Full Controller Support** — Build, Move, and Demolish all work with the joystick; the ghost follows the cursor in real time; Build (A sets position, A places), Move (A selects, A confirms), Demolish (A highlights green, A demolishes; move away to deselect); left stick pans at screen edges; visible cursor in farm view.
- **Furniture Placement Fix** — Y no longer rapid-toggles furniture; one press = one interaction, all furniture types including beds; new `EnableFurnitureDebounce` toggle (default on).

## 3.1.0
- **Cleanup Update** — no behavior changes; consolidated 12 granular GMCM toggles into 5 feature groups; removed dead code; cached per-call reflection lookups; cleaned stale version references. (Existing `config.json` resets to defaults due to renamed properties.)

## 3.0.0
- **Console-Style Chest Item Transfer** — A instantly transfers a full stack between chest and inventory; Y transfers one (hold for rapid); RB snaps to Fill Stacks; works for all chest types.
- **Chest Sidebar Navigation** — all sidebar buttons reachable (Sort, Fill Stacks, Color Toggle, Sort Inventory, Trash, Close X); Close X closes without reopening.
- **Color Picker Swatch Navigation** — full 7x3 grid; cursor snaps to first swatch; A selects; B closes the picker only; runtime stride detection; color preserved after probe.

## 2.9.0
- **Shop Controls Overhaul** — LB/RB quantity adjustment with hold-to-repeat; right-stick fast navigation (jump 5, hold-to-repeat); controller button icon on the tab-switch button; touch tab button blocked when a controller is connected; grayed-out item sell fix; cutscene skip (Start twice); right-stick vanilla scroll desync fixed; no sell tooltip for unsellable items.

## 2.8.0
- **Release cleanup** — Debug/Trace logging silenced by default (VerboseLogging defaults false); all debug logs gated behind the toggle; removed unused legacy config properties; Y-sell feedback promoted to INFO.

## 2.7.x (2.7.1 — 2.7.21)
- **Carpenter Menu Fix** (2.7.2–2.7.4) — Robin's building menu no longer instantly closes.
- **Fishing Mini-Game Fix** (2.7.1) — X/Y swap applies during the bobber bar.
- **Shop Purchase Overhaul** (2.7.5–2.7.14) — complete rewrite; trade-item shops (Desert Trader) work; tool upgrades/recipes/special purchases via `actionWhenPurchased`; inventory-full refunds return money + trade items; fixed phantom purchases on tab switch; buy quantity no longer bleeds to the sell tab.
- **Console-Style Shop Selling** (2.7.16–2.7.21) — A sells the stack, Y sells one (hold for rapid); sell tooltip with gold icon; snap-navigation item detection (hoveredItem doesn't work on the Android sell tab).
- **Performance** (2.7.15) — cached reflection fields in InventoryManagementPatches.

## 2.7.0
- **Console-Style Inventory Management** — A picks up a stack to cursor, A again places/swaps; Y picks up one (hold for continuous); held items render at the cursor slot; controller hover tooltips; fishing rod tooltip shown while holding bait/tackle.
- **Improved Fishing Rod Bait/Tackle** — detaching puts the item on the cursor instead of the first empty slot.

## 2.6.0
- **Fishing Rod Bait/Tackle Fix** — A on bait/tackle selects it; Y on a rod attaches; Y on a rod with nothing selected detaches (bait first, then tackle); supports stacking and swapping.

## 2.5.1
- **Fixed shop stock bug** — limited-stock items decrement correctly on partial-quantity purchases.

## 2.5.0
- **Fixed X button inventory deletion bug** — critical fix for Xbox layout + Switch style; renamed "Use D-Pad for Toolbar" to "Use Bumpers Instead of Triggers"; added LB/RB shop quantity adjustment in bumper mode; added "Start Opens Journal"; tested with Odin built-in controls and external Xbox Bluetooth controllers.

## 2.4.0
- **Xbox controller support** — tested with Xbox Wireless Controller; **bumper mode** (LB/RB instead of triggers); **controller layout settings** (Switch/Xbox/PlayStation); **control style settings** (Switch-style or Xbox-style confirm/cancel).

## 2.1.0
- **Console-style shipping bin** — complete rewrite using the game's native shipping flow; A ships the stack, Y ships one; "Last shipped" display works; fixed toolbar selection box sizing.

## 2.0.0
- **Rebranded to Android Consolizer**; **console-style toolbar** — 12-slot rows with LB/RB to switch rows, LT/RT to move within a row; custom toolbar rendering matching console layout.

## 1.0.0
- Initial stable release — shop purchasing (A), inventory/chest sorting (X), add-to-stacks in chests (Y), X-button deletion bug blocked.

## 0.3.4 — 2026-01-31
- New button scheme: X = Sort, Y = Add to stacks; removed broken buy/sell toggle; simplified GMCM and showed current button mappings at top. Added inventory sorting (X) and `EnableSortFix`/`EnableAddToStacksFix`. Fixed X in inventory/chest triggering sort instead of the deletion bug.

## 0.3.3 — 2026-01-31
- Detailed logging for chest-menu button presses; Back/Start/BigButton as organize alternatives; improved button detection in ItemGrabMenu.

## 0.3.2 — 2026-01-31
- Shop purchase respects the LT/RT quantity; calculates total cost by quantity; resets quantity to 1 after purchase.

## 0.3.1 — 2026-01-31
- Extensive debug logging for shop purchases; multiple fallback purchase methods; tries a direct method call before the manual implementation.

## 0.3.0 — 2026-01-31
- Initial beta — shop purchase fix (A), chest add-to-stacks (X), buy/sell toggle (Y, not working on Android), GMCM config, verbose logging.
