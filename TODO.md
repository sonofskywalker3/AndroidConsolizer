# AndroidConsolizer — TODO

**Read this before starting any bug fix or feature.** Each item has implementation notes, root-cause hypotheses, and file references. Completed work lives in [`DONE.md`](./DONE.md) — check there for prior art before starting anything new.

Shipped: **v3.6.0** (Bug Fix Release). Roadmap structure was re-evaluated post-3.6.0 — see [`docs/superpowers/specs/2026-05-08-roadmap-reevaluation-design.md`](./docs/superpowers/specs/2026-05-08-roadmap-reevaluation-design.md) for the reasoning behind the milestones below. The original GSD-era M3 → M4 → M5 → M6 ordering has been replaced with the structure here.

**Release-tooling items** (#66 Nexus mod-page automation, #67 cookie refresh) live in [`docs/RELEASE_TOOLING.md`](./docs/RELEASE_TOOLING.md), not here.

---

## v3.8.0 — Console Parity: Quick Wins — ✅ SHIPPED 2026-05-31 (GitHub release v3.8.0 + Nexus)

**All items complete — see `DONE.md`.** ✅ #22b, #17, #35, #39, #46, #19, #18, #27 (toolbar size slider, Nexus #1050718), and #47 (resolved not-a-bug). **The next milestone is [v3.9.0 — Console Parity: Big Systems](#v390--console-parity-big-systems) below (#25 tool charging while moving, #25b slingshot aim).** Start the next change at v3.8.1 (or v3.9.0 work). The items below are kept for their historical implementation notes.

Small to medium parity fixes that can each be solved with localized patches. No multi-patch system arc. Bumps the mod from "most parity items done" to "every menu has correct defaults and visible cursors."

### 47. Missed Rewards Chest Not Appearing — ✅ NOT A BUG (resolved 2026-05-31, device-verified G Cloud)
- **✅ RESOLVED — works as designed.** Diagnostic (v3.7.72/73) + device test proved the CC missed-rewards system works correctly on Android: `checkForMissedRewards` flags it (`cond{b0->area0(complete=True)}`), `doShowMissedRewardsChest` places the (22,10) tile (`index=5 sheet=indoors2`), the reward is grabbable, and it clears after grab. The container is the small **vanilla bag/giftbox sprite** — easy to overlook (the user had never noticed it in any playthrough), which is what the "chest never appears" report actually was. Also note missed rewards only show once the **whole area** is complete, not when a single bundle is pending — the original report's pending `[23,25]` were Vault bundles in a likely-incomplete area. No code change. Joins #48/#65 as not-a-bug. See `DONE.md`. Original investigation notes below.
- After completing a CC room with unclaimed bundle rewards, the "missed rewards" chest should appear at tile (22, 10) in the Community Center. On Android, the chest never appears — even when reward stacks are left completely untouched.
- **Vanilla system:** `CommunityCenter.checkForMissedRewards()` iterates `bundleRewards`, checks `bundleRewards[key] == true && areasComplete[area] == true`, populates `missedRewardsChest` items. Called from `doRestoreAreaCutscene` (line 875), `resetSharedState` (line 562), and `performAction` on "MissedRewards" tile (line 357). Chest tile modification via `showMissedRewardsChestEvent`.
- **Confirmed broken:** User left 2 of 4 reward stacks completely untouched, room completed, no chest appeared. `BundleRewards` still showed pending indices `[23, 25]` in logs after room completion.
- **Investigation:** Does `showMissedRewardsChestEvent` fire on Android? Does the tile modification at (22, 10) work? Is `checkForMissedRewards` ever called? May be an Android-specific issue with the event system or tile layer.
- **Possible fixes:** Hook `markAreaAsComplete` or `doRestoreAreaCutscene` to force-check for missed rewards. If the chest exists but is invisible, may need tile/sprite fix.
- **File:** Likely new `Patches/CommunityCenterPatches.cs`.

### 27. Toolbar Size Slider (Options Menu) — ✅ DONE (v3.7.68→v3.7.71, device-verified G Cloud 2026-05-30/31)
- **✅ DONE.** Root cause: `ToolbarPatches.Toolbar_Draw_Prefix` fully replaced `Toolbar.draw` with a hardcoded 64px slot and ignored `Options.toolbarSlotSize`, so the working vanilla slider (id 148) had no effect. Fix wires the slider into AC's draw: `ResolveSlotSize()` reads `toolbarSlotSize` (reflected) and scales the 12 slots + icons + overlays, **capped so the centered row clears the right-side energy/health HUD** (v3.7.69, `HudSafeMarginX`). The "Toolbar Padding" slider (id 134) was also dead under AC's centered toolbar — repurposed as a docked-edge gap (v3.7.70), softened to half-travel per user feedback (v3.7.71). Public Nexus bug #1050718 fixed. See `DONE.md`. Original notes below.
- Console-style 12-slot toolbar has overlap/sizing issues on small screens.
- Hijack vanilla "Toolbar Size" slider or inject our own.
- **Investigation:** Does vanilla slider exist on Android? What field does it control? How does our toolbar determine slot size?
- **Public bug report:** Nexus user throyiii filed Bug #1050718 ("Can't resize the toolbar") against v3.5.10 on 5 Mar 2026 — bumped priority since there is now a public report.
- **Files:** `Patches/ToolbarPatches.cs`, possibly new `Patches/OptionsPagePatches.cs`.

### 19. Geode Breaking Menu — Console Parity (IN PROGRESS, v3.7.21 → v3.7.32 + more)

**Original ask:** A-press to crack a geode the way Switch does it; no visible movement on Android today.

**What ships in v3.7.32 and works:**
- ✅ Single-press A places + cracks (A→X redirect, vanilla X path), Switch parity. See `Patches/GeodeMenuPatches.cs` `ReceiveGamePadButton_Prefix`.
- ✅ Android touch-sim `receiveLeftClick` after A is suppressed (same-tick guard `_redirectTick`) — otherwise it picks up the partially-consumed geode stack.
- ✅ Tooltip auto-shows for the selected geode (`_showTooltip` forced true on menu open via reflection).
- ✅ Spatial geode-only nav replaces vanilla's linear `_selectedItemIndex ±1` scan. 12-col grid math. UP/DOWN fall back to scanning adjacent rows for nearest-column geode when same-column has nothing.
- ✅ `IClickableMenu.applyMovementKey` suppressed for GeodeMenu only — was racing our snap and dragging cursor to spatial neighbours.
- ✅ Mouse cursor sprite forced visible (`mouseCursorTransparency = 1f`) and snapped to bottom-right of selected slot via vanilla `snapCursorToCurrentSnappedComponent`.
- ✅ GMCM toggle `EnableConsoleGeodeMenu` (default true). Off restores vanilla one-press X + A-toggle-tooltip.

**Remaining (next agent picks up here):**

**19a. Red box selection highlight at top-left of selected slot.**
v3.7.32 attempted to hide the duplicate top-left finger drawn by `Patches/InventoryManagementPatches.cs::InventoryMenu_Draw_Prefix/Postfix` for the GeodeMenu case. It works for the finger draw but VANILLA tile 56 (red box) now shows instead. **The early-return bug is in `InventoryMenu_Draw_Prefix`** — when `Game1.activeClickableMenu is GeodeMenu`, it sets `_savedSelectedItem = -1` and returns without actually clearing the InventoryMenu's `currentlySelectedItem` field. So vanilla sees the selection set and draws tile 56. Fix: in the prefix's GeodeMenu branch, do the same save-and-clear the non-GeodeMenu path does (save the real value, set the field to -1) — just skip drawing the replacement finger in the postfix.

**19b. No buzzer + no shake when cracking a geode with inventory full.**
User reports a quiet "bloop" instead of a clear rejection cue (probably `Game1.playSound("smallSelect")` from `GamePadShowInfoPanel`, which we call after every nav). Vanilla `OnPlaceGeodeOnAnvil` for the inventory-full branch (`Patches/decompile reference: GeodeMenu.cs:683-688`) sets `descriptionText = fullText`, `wiggleWordsTimer = 500`, `alertTimer = 1500` — and that's it. No sound, no shake at the geode. User wants a buzzer + a visual shake matching other "invalid action" feedback.

Suggested fix: pre-check in the A→X redirect before forwarding to vanilla X. If `Game1.player.freeSpotsInInventory() < 1 && selectedGeode.Stack > 1` (which is the same condition vanilla checks), play `Game1.playSound("cancel")` and trigger a shake on the highlighted slot (look at how other inventory menus shake — `_iconShakeTimer` dictionary on `InventoryMenu` is referenced in the draw loop, decompile line 836). Then return false to skip vanilla so the silent vanilla branch doesn't run.

**19c. Menu opens with nothing selected.**
Vanilla GeodeMenu opens with `_selectedItemIndex = -1` — the cursor sits at slot 0 but no slot is highlighted, and A doesn't crack anything until the user presses a direction first. Shipped v3.7.35: `OnGeodeMenuOpened` now finds the first geode via `IsGeodeAt` and snaps selection + cursor to it (mirrors `DoSpatialNav`'s pattern). Awaiting user confirmation on G Cloud.

**Heavy diagnostic logging in `Patches/GeodeMenuPatches.cs`** — ✅ **DONE in v3.7.42:** all geode diagnostics demoted from Info to Trace, no longer spams shipped logs. No behavioural change.

**Status note (updated 2026-05-29):** 19a (red box) done v3.7.33, 19b (buzzer+shake) done v3.7.34, 19c (auto-select on open) done v3.7.35. v3.7.36–v3.7.41 = a tooltip-placement arc (flicker, edge-flip, infoBox pinning), awaiting device confirmation it's settled. The old "Inventory Full text never renders" polish item was **dropped** — its only viable fix was suppressing the touch-sim `releaseLeftClick`, and **Harmony-patching `GeodeMenu.releaseLeftClick` hard-crashes the Android runtime** (native SIGSEGV / broken trampoline — see v3.7.50→v3.7.57 saga and `DONE.md`). The buzzer+shake from 19b already covers the rejection-feedback gap. **#19 is effectively done** pending the tooltip-arc device confirmation.

> ⚠️ **Android landmine (v3.7.57):** attaching ANY Harmony patch to `GeodeMenu.releaseLeftClick` on the Android mono runtime produces a broken trampoline → native crash the moment the method is invoked. The method runs fine in vanilla; it's the *patching* that's fatal. Avoid patching this override; if release-handling is ever needed, intercept upstream (`receiveLeftClick` / a state guard) instead.

**Spec:** `docs/superpowers/specs/2026-05-18-geode-menu-design.md` (note: spec assumed two-press A — turned out wrong after Switch hardware testing, see commit `v3.7.24` message). The implementation diverged from spec; update or write a new spec post-completion.

**Test setup helper:** `tools/seed-crackables-into-chest.ps1` injects 5x each of Geode/Frozen/Magma/Artifact Trove/Golden Coconut/Mystery Box/Golden Mystery Box into the Cheatside save's Farm chest at tile (89, 50). Backup auto-written. Useful for next agent.

### 68. Craftable Placement — Single Ghost Tile Instead of Multi-Tile Map
- **✅ DONE — device-verified on G Cloud 2026-05-29 (v3.7.46).** User confirmed placing the auto-grabber showed a single perfect ghost tile (no multi-tile map). See `DONE.md`. Original implementation notes below.
- **FIX IMPLEMENTED — v3.7.46.** Extended `Patches/FurniturePlacementPatches.cs` with a parallel branch for placeable non-furniture `Object`/`BigCraftable` (1×1 footprint): single validity square at `TileLocation` + translucent `Object.draw(sb,x,y,0.5f)` ghost, then suppresses the multi-tile map. Furniture branch left byte-identical. Bombs (286/287/288) + crab pots (685) excluded. New toggle `EnableConsoleCraftablePlacement` (default true, GMCM). **Verify next device session:** hold a machine (e.g. furnace/keg) and a sprinkler → confirm one ghost tile at the landing spot instead of the full green map; confirm a BigCraftable's 2-tall sprite ghost anchors correctly; regression-check furniture placement still looks right; check a bomb still throws normally (no ghost).
- Holding any placeable craftable (furnace, mayo machine, preserves jar, recycling machine, kegs, sprinklers, etc.) highlights EVERY valid placement tile on the screen as a green-tile map. Visually cluttered and useless for controller play — the player only cares about where the item will land *right now*.
- **Fix model:** same as `DONE.md` "Console Furniture Placement — v3.5.38, v3.5.39". That patch hooked `Object.DrawRedGreenRectangleForPlacing` (mobile-only, resolved by string with `AccessTools.Method`) and, for `Furniture` instances, drew a single colored rectangle at `__instance.TileLocation` sized via `getTilesWide()`/`getTilesHigh()`, then drew the furniture sprite translucently on top. Same engine artifact already handles single-tile rendering for tap/touch placement — we just bypass the `weaponControl` gate.
- **What's different here:** the v3.5.38 patch was gated on `__instance is Furniture`. Craftables are plain `StardewValley.Object` (or BigCraftable subclasses), not Furniture. The same engine method serves both; we just need to extend the gate (or add a parallel `is Object && !Furniture && isPlaceable` branch) and pick a sensible single-tile size — for BigCraftables that's typically 1×2 (machine + 1 floor tile under).
- **Investigation:** Confirm `DrawRedGreenRectangleForPlacing` is the path for non-Furniture craftables too (the multi-tile map suggests yes). If yes, extend `FurniturePlacementPatches.DrawRedGreenRectangleForPlacing_Prefix` rather than adding a new patch. Decide whether to gate behind the existing `EnableConsoleFurniturePlacement` toggle or add a sibling `EnableConsoleCraftablePlacement` — leaning sibling so users can opt out per-category.
- **Files:** `Patches/FurniturePlacementPatches.cs` (extend), `ModConfig.cs` (toggle), `ModEntry.cs` (GMCM entry).
- **Belongs in v3.8.0** — same console-parity-quick-win shape as the geode work. Small, localized patch; no new system arc.

### 54b. Trigger Column-Skip — ✅ DONE (v3.7.49, device-verified G Cloud 2026-05-29)
- **✅ DONE — v3.7.49.** Real fix: re-arm `_triggerSlotTarget` on LB/RB row switch so enforcement holds the new slot through the first trigger press (v3.7.48's start-of-tick snapshot failed — vanilla corrupts on a different tick). Log-verified: every first-post-switch press now +1. See `DONE.md` "#54b Trigger Double-Skip". (`[ToolIdx]` diagnostic in `FarmerPatches.cs` stays — VerboseLogging-gated.)
- **(superseded) FIX IMPLEMENTED — v3.7.48, awaiting device verification.** Reproduced reliably 2026-05-29 (user: "almost every time I changed backpack rows the first trigger press jumped 2 spots"). **Confirmed root cause** (log `SMAPI-v3.7.43-20260528-160022.txt` successor, tick ~33189): vanilla `pressSwitchToolButton` still fires on a trigger press (+1, then `FarmerPatches` setter-intercept remaps into the row); AC's `HandleTriggersDirectly` also moves +1. Normally `_triggerSlotTarget` (immune base, enforced pre-Update by `OnUpdateTicking`) + the post-move Slot correction absorb vanilla's move → net +1. **But a row switch (LB/RB or DPad L/R) clears `_triggerSlotTarget = -1` (`OnButtonsChanged`)**, so the FIRST trigger press afterward had no immune base and `HandleTriggersDirectly` read the already-vanilla-moved `player.CurrentToolIndex` as its base → vanilla's +1 stacked on ours → **+2**. **Fix:** snapshot `CurrentToolIndex` at tick start (pre-Update) into `_preUpdateToolIndex` in `OnUpdateTicking`; `HandleTriggersDirectly` falls back to that clean value (not the post-Update index) when `_triggerSlotTarget < 0`. Scoped to the first-press-after-switch case only. **Verify next device session:** switch rows (LB/RB), press a trigger once → moves exactly ONE slot; repeat across rows + both triggers; confirm in-row cycling still moves one at a time. After verified, demote the noisy `[ToolIdx]` Info diagnostic in `FarmerPatches.cs`.
- **(historical) Status:** parent #54 shipped fix in v3.6.6 (see `DONE.md` "#54 Trigger Column-Skip"). One recurrence observed during v3.7.26 G Cloud test session 2026-05-18 — does NOT invalidate v3.6.6, just isn't 100%.
- **Captured evidence** (`test-output/log-archive/SMAPI-v3.7.26-20260518-123505.txt`, lines ~1774-1786, around tick 15814-15830):
  - Tick 15814: `set_CurrentToolIndex_Patch1` 0→1 called by **vanilla** `Game1.pressSwitchToolButton()` from `Game1.UpdateControlInput(GameTime)`.
  - Tick 15815 (1 tick / ~16ms later): `set_CurrentToolIndex_Patch1` 1→2 called by **AC's** `ModEntry.HandleTriggersDirectly` from `OnUpdateTicked`.
  - Trigger raw values across the press: RT 0.96 → 0.92 → 0.78 → 0.78 → 0.51 → 0.12 → 0.01 (clean ramp-down, no mid-pull dropouts).
  - Single user press → both vanilla and AC fired. v3.6.6's release-confirmation streak prevented the AC-vs-AC bounce that was the original #54 root cause, but did nothing about AC racing vanilla.
- **Root cause hypothesis:** `Game1.UpdateControlInput` reads `GamePad.GetState()` directly and calls `pressSwitchToolButton()` whenever the trigger crosses the press threshold. AC's `HandleTriggersDirectly` runs from SMAPI's `UpdateTicked` event and also detects press edges. Both fire on the same edge → double advance. The #54 fix addressed AC's own bounce/dropout sensitivity but didn't suppress the vanilla path that AC was supposed to replace.
- **Fix direction:** either (a) suppress vanilla's `pressSwitchToolButton` while AC's toolbar logic owns trigger handling (e.g. zero the trigger value in `GetState_Postfix` for tool-switch purposes after AC has consumed the edge — same pattern as the analog-trigger zeroing in `GameplayButtonPatches` for the v3.3.x trigger work), or (b) detect that vanilla just advanced the tool index and skip AC's advance for that edge (e.g. record `_lastVanillaToolSwitchTick` from the existing `CurrentToolIndex_Prefix` diagnostic and have `HandleTriggersDirectly` ignore presses within ~5 ticks of that timestamp).
- **Priority:** low — single occurrence per session is rare and the user can correct with one Y/X press in the other direction. Address when the next user complaint lands OR when v3.8 / v3.9 work brings trigger handling under examination anyway.
- **Files to touch:** `ModEntry.cs` (`HandleTriggersDirectly`), possibly `Patches/GameplayButtonPatches.cs` (GetState-level suppression), `Patches/FarmerPatches.cs` (existing `[ToolIdx]` diagnostic stays — it's how we caught this).

### 69. Crafting Quantity — Hold A to Craft Continuously (Console Parity)
- **✅ DONE — device-verified on G Cloud 2026-05-29 (v3.7.47).** User confirmed hold-A crafts continuously and stops appropriately. See `DONE.md`. Original implementation notes below.
- **FIX IMPLEMENTED — v3.7.47.** User clarified the ask: the quantity slider exists but is touch-only; they want hold-A rapid-craft like console / hold-to-buy / hold-Y. New `Patches/CraftingPagePatches.cs` polls A in a `CraftingPage.update` postfix and re-fires `receiveGamePadButton(A)` at delay 15 / rate 8 ticks while held; self-limited by `showCraftButton` (re-evaluated after each craft, so it stops at ingredient/space exhaustion). Covers crafting tab + standalone cooking (GameMenu.update forwards to the page). New GMCM toggle `EnableHoldToCraft` (default true). **Verify next device session:** hold A on the crafting menu → crafts repeatedly; confirm it auto-stops when ingredients/inventory run out; test on both the GameMenu crafting tab and a cooking station; confirm a single quick A-press still crafts just one (no unwanted repeat).
- **Note:** the per-press quantity slider still adjusts only by touch (X/Y adjust it in vanilla but AC's swap / discoverability makes it awkward) — hold-A is the console-parity answer the user wanted; slider-via-controller is a separate, lower-priority polish item if ever requested.
- **Reference — actual vanilla Android crafting flow** (`CraftingPage.cs`): A=craft (`receiveGamePadButton` 476 → `CraftSelectedRecipe` 612 → `clickCraftingRecipe` 821, crafts `quantityToCraft` batch); X/LT=qty−1, Y/RT=qty+1 (502-523) gated on `showQuantitySlider`; slider appears only after first craft.
- **How vanilla Android crafting actually works** (`CraftingPage.cs`):
  - `receiveGamePadButton` (line 465): **A** → `CraftSelectedRecipe()`; **X / LeftTrigger** → quantity −1; **Y / RightTrigger** → quantity +1 (lines 502-523), gated on `showQuantitySlider`.
  - `CraftSelectedRecipe()` (612): crafts the batch, then if `quantityWeCanMake > 1` sets `showQuantitySlider = true`, `quantityToCraft = 1` (line 618-626). So the **slider only appears AFTER the first craft**.
  - `clickCraftingRecipe()` (821): actually crafts `quantityToCraft` at once (`crafted.Stack = quantityToCraft`, consumes ingredients `num` times) — multi-craft works.
- **So the real bug is one (or more) of:** (a) AC's low-level X/Y menu swap INVERTS the quantity buttons (physical X→+1, Y→−1) — confusing, not broken; (b) the slider is undiscoverable (only shows after first craft, no prompt that X/Y change it); (c) something in AC consumes X/Y before they reach `CraftingPage.receiveGamePadButton`; or (d) `showQuantitySlider` never goes true on the controller path. **Cannot be determined from code alone — needs a device diagnostic.**
- **NEXT STEP (device session):** small diagnostic on `CraftingPage` — log when `showQuantitySlider`/`showCraftButton` flip, and log every `receiveGamePadButton` arrival (which button reaches it, post-AC-swap). Reproduce: open crafting, craft one, try X/Y. Then fix appropriately — most likely either un-swap (or correctly map) X/Y for the crafting page so +/- match the on-screen slider, and/or add a clearer prompt. Hold-to-repeat could be a *later* enhancement on top, not the core fix.
- **Files:** likely new `Patches/CraftingPagePatches.cs`; possibly `Patches/GameplayButtonPatches.cs` (X/Y swap scope) or `Patches/GameMenuPatches.cs`.
- **Status:** moved out of the "code-blind, build-verify" bucket — **device-diagnosis required** before any fix. Not started.

### 71. Consume-on-Grab Rewards Dumped Into Bag (Museum/Reward ItemGrabMenu via Controller)
- **Public report (Nexus comment, 2026-05-28):** *"When I used the controller to collect the Dwarf Language Translation Manual, it gave me a book, not a skill."*
- **✅ DONE — device-verified on G Cloud 2026-05-29 (v3.7.45).** Log confirmed `[#71] Consumed reward on grab: Dwarvish Translation Guide ((O)326) — canUnderstandDwarves set`; user confirmed the Dwarf is now understandable and the guide didn't enter the bag. See `DONE.md`. Original implementation notes below.
- **FIX IMPLEMENTED — v3.7.45.** `TryConsumeRewardOnGrab` added to `Patches/ItemGrabMenuPatches.cs`, called at the top of both chest→player grab paths (`TransferFromChest`, `TransferOneFromChest`). For `(O)326` it sets `canUnderstandDwarves` (reflected setter); for `parentSheetIndex 102` it calls `foundArtifact("(O)102",1)`; both invoke `behaviorOnItemGrab` (marks collected), play "fireball", remove from the reward menu, and don't add to the bag. Keyed on item identity so normal chest grabs are untouched. Diagnostic `BookRewardDiagnosticPatches` removed in v3.7.44 (it was on the wrong path). **Verify next device session:** donate 4 Dwarf Scrolls → grab guide from Rewards popup with controller → confirm the guide does NOT enter the bag, the "fireball" plays, and the Dwarf is now understandable. Watch for `[#71] Consumed reward on grab` in the log. Also regression-check a normal chest grab + a CC-bundle reward grab. **Deferred:** the `isRecipe` consume-on-grab case (vanilla's third special) is not reachable via the museum reward menu; revisit if a recipe-reward menu is reported.
- **ROOT CAUSE — CONFIRMED on device (v3.7.43 session, G Cloud, 2026-05-28).** Earlier hypothesis (a `readBook` / `performUseAction -102/-103` use-item gap) was **WRONG** — discard it. The guide is `(O)326`, delivered through the museum's **"Rewards" `ItemGrabMenu`** (`LibraryMuseum.cs:337`, `showReceivingMenu: true`, `behaviorOnItemGrab = OnRewardCollected`). Vanilla's grab handler (`ItemGrabMenu.cs:826-867`) has three **consume-on-grab** special cases that DISCARD the item (`heldItem = null`) instead of giving it to the player:
  1. **`(O)326`** → `Game1.player.canUnderstandDwarves = true` + `playSound("fireball")` (the dwarf guide — this report);
  2. **`parentSheetIndex == 102`** → `foundArtifact("(O)102", 1)` (Lost Book) + fireball;
  3. **`isRecipe`** → learns the cooking/crafting recipe.
- **AC bypasses all three.** AC treats the Rewards `ItemGrabMenu` as a generic chest: `ItemGrabMenuPatches.TransferOneFromChest` / `TransferStackFromChest` do a plain `Game1.player.addItemToInventory(...)` then `InvokeBehaviorOnItemGrab`. So the controller grab dumps `(O)326` (or a lost book, or a recipe) into the bag, the consume-on-grab special never runs, and the skill/effect is never applied. Device proof (session `SMAPI-v3.7.43-20260528-160022.txt`, 15:32:18):
  - `[ChestTransfer] behaviorOnItemGrab invoked for Dwarvish Translation Guide`
  - `[ChestTransfer] Took 1x Dwarvish Translation Guide from chest`
  - `canUnderstandDwarves` setter NEVER fired → flag stayed false → dwarf stayed gibberish.
- **Why the v3.7.43 diagnostic logged nothing useful:** it patched vanilla `receiveGamePadButtonGrabbingItems` / `receiveLeftClick`, but AC's chest-transfer owns the grab and never calls those. `Patches/BookRewardDiagnosticPatches.cs` is therefore on the wrong path — **remove it** (or repoint to `TransferOneFromChest`/`TransferStackFromChest`) when implementing the fix.
- **Recommended fix (robust):** in AC's chest→player grab path, detect a **receiving/reward menu** (`ItemGrabMenu.source == source_chest`? no — use `showReceivingMenu` / a non-null `behaviorOnItemGrab` reward callback, or `reverseGrab == false` receiving menu) and, for the grabbed item, route through **vanilla's own reward-grab handler** rather than AC's transfer — that handler already covers `(O)326`, lost books, recipes, AND any future consume-on-grab rewards. Targeted alternative: replicate the three special cases inline before the `addItemToInventory` in `TransferOneFromChest`/`TransferStackFromChest` (set the flag / `foundArtifact` / learn recipe, play fireball, remove from grab menu, do NOT add to bag). Prefer route-through-vanilla so we don't have to chase every special case.
- **Regression caution:** this is shared chest-transfer code used by every chest/JunimoHut/AutoGrabber/StorageFurniture grab — gate the new behaviour strictly to reward/receiving menus so normal chest grabbing is untouched. Re-test a normal chest grab + a CC-bundle reward grab after the fix.
- **Files:** `Patches/ItemGrabMenuPatches.cs` (`TransferOneFromChest` ~1351, `TransferStackFromChest`, `InvokeBehaviorOnItemGrab` ~1582). Decompile ref: `ItemGrabMenu.cs:826-905` (the consume-on-grab special cases), `LibraryMuseum.cs:337` (reward menu construction).
- **Relation to #18:** both surfaced in the museum but are mechanically distinct — #18 is the donation *placement* menu (snap), #71 is the *reward collection* grab. Separate patches. (Note: the user had to use touch to donate because of #18, then grabbed the reward with the controller → hit #71.)
- **Milestone:** v3.8.0 — localized to `ItemGrabMenuPatches`, no new system.

### 18. Museum Donation + Rearrange Menu (pulled into v3.8.0)
- **✅ DONE — device-verified on G Cloud 2026-05-30 (donation v3.7.61, rearrange v3.7.63; cleaned up through v3.7.66).** User confirmed controller donation works (visible cursor, D-pad selects, A donates) and rearrange is reachable and movable. See `DONE.md` "#18 Museum Donation + Rearrange Controller Support". Original notes below.
- **Real root cause (NOT what the spec premised):** the snap chain was NOT gated off on G Cloud — `snappyMenus` field + `SnappyMenus` property were already true and the mechanics all worked. The blocker was **no visible cursor** (`drawMouse` suppressed because Android delivers the controller confirm as a synthesized touch → control type reads TOUCH). Fix = draw the snap cursor ourselves in a `MuseumMenu.draw` postfix (v3.7.61). Rearrange additionally needed `reOrganizing=true` (ctor postfix, reflected — Android-only field) so `receiveKeyPress` grid-navigates the placed pieces instead of the hidden inventory (v3.7.63). The SnappyMenus force is kept as cross-device insurance (no-op on G Cloud). A right-stick free-pan (v3.7.64) was tried and **reverted** (v3.7.65) — it broke the menu; zoom-before-donating covers the cramped-view case. Diagnostics stripped v3.7.66. **Don't relitigate the SnappyMenus theory** — see memory `museum-donation-controller-rootcause`.
- **Toggle:** `EnableMuseumDonationController` (default true). **File:** `Patches/MuseumMenuPatches.cs`.
- _(Original approach notes, kept for history):_ Controller-only placement was inaccessible; touch was required to select/place. The spec proposed a snap-based selection overlay with a virtual tile-space cursor — in practice the vanilla snap chain already handled selection/placement once the cursor was made visible, so no custom selection model was needed.

### 73. Seeds Show Touch-Style Multi-Tile Green Map (should be console single box) — ✅ DONE v3.8.20 (device-verified G Cloud 2026-06-04 — user: "seeds are perfect")
- **Resolved (v3.8.20):** the seed branch in `Patches/FurniturePlacementPatches.cs` now draws the single target-tile red/green box (no ghost sprite), matching console. Device-confirmed good.
- **NOT done.** _(historical)_ The v3.8.5 fix (exempting seeds from the #68 craftable ghost) just reverted seeds to **vanilla Android's touch behavior: ALL usable tiles on screen highlighted green** — not the console look. **Console shows a SINGLE red/green box on the target tile, NO ghost sprite.** User (2026-06-04, in-game): "instead of showing a red or green box without a ghost like it does on console, it highlights all the usable boxes on screen in green, like with the furniture. it's touchscreen behavior, not controller."
- **Desired:** give seeds the single target-tile box treatment (the `Object.DrawRedGreenRectangleForPlacing` path used by #68 furniture/craftables) but WITHOUT the translucent ghost sprite — just the red/green box on the tile the seed will plant into.
- **Separate code path from #76** (corrected after the #76 decompile dive): #76's marker is `Farmer.draw` tile-29 (`alwaysShowToolHitLocation`); seed placement is `Object.drawPlacementBounds` / `DrawRedGreenRectangleForPlacing`. They look alike but don't share rendering — #73 does NOT fall out of #76.
- **Files:** `Patches/FurniturePlacementPatches.cs` (the seed branch added in v3.8.5).
- _(v3.8.5 history: added `obj.Category == SObject.SeedsCategory` (-74) to `IsSpecialPlacementObject` so seeds fall back to vanilla instead of the craftable ghost — but that vanilla fallback IS the touch multi-tile green map. Decompile `Object.isPlaceable()` line 6118: seeds placeable via `Category == -74 && edibility < 0`; saplings separate category. Fertilizer (Category -19) hits the same path and likely also shows the touch map — fold into this fix.)_

### 76. Console "Always Show Tool Hit Location" (red target box) — ✅ IMPLEMENTED v3.8.18, awaiting device test
- **Requested 2026-06-04 (user, in-game):** always show the red box on the tile your tool will hit (hoe/water/pickaxe/axe). User chose: visible even while moving.
- **Root cause (verified):** `Options.alwaysShowToolHitLocation` defaults **false** on Android (`Options.cs:867`); the draw gate (`Farmer.cs:6364`) also needs LeftShift, which a controller never holds — so the marker never shows. `hideToolHitLocationWhenInMotion` defaults true (hides while walking). The draw path (mouseCursors tile 29 at `GetToolLocation`, `Farmer.cs:6366-6368`) IS present and live in `Farmer.draw`.
- **Fix (v3.8.18, Phase 1):** when `EnableToolHitLocation` (new GMCM toggle, default true) is on, force `alwaysShowToolHitLocation=true` + `hideToolHitLocationWhenInMotion=false` via a guarded per-tick enforce in `ModEntry.OnUpdateTicked`. Same fix-the-data pattern as #25b. **Files:** `ModConfig.cs`, `ModEntry.cs`.
- **⚠️ Device test pending (device off ADB):** confirm the box (a) appears for hoe/water/pickaxe/axe, (b) lands on the tile you're FACING (the draw uses `GetToolLocation(getMousePosition())`, not the gamepad-aware overload — risk it follows a phantom cursor), (c) stays visible while moving. **Phase 2 only if (b) is wrong:** override the draw to use the facing tile (`GetToolLocation(ignoreClick:true)`). Spec: `docs/superpowers/specs/2026-06-04-tool-hit-location-76-design.md`.

### 74. Dropped Items Are Instantly Re-Collectable (no console drop-blocker) — ✅ DONE v3.8.17 (device-verified G Cloud 2026-06-04, see `DONE.md`)
- **Resolved (Phase 1):** Android never assigned `Debris.DroppedByPlayerID`, leaving the vanilla ~1200ms drop-blocker dormant. `TagAsPlayerDrop` now sets it on deliberate player drops (`DropHeldItem` + `CancelHold` failsafe) in `Patches/InventoryManagementPatches.cs`, re-engaging the engine's own exclusion. GMCM `EnableConsoleDropBlocker`. User confirmed "working the same as the switch." **Phase 2 parked:** user recalls PC lets you drop + walk away indefinitely without re-pickup (a true leave-radius gate), unlike the 1.2s timer — only revisit if PC-style behavior is wanted; Switch parity is met. Full writeup in `DONE.md`.
- **Reported 2026-06-04 (user, in-game):** dropping an item and immediately re-picking it up — on console there's a pickup blocker until you leave and re-enter the item's pickup radius; on Android you re-grab it instantly (often before you've even left the menu).
- **Root cause — confirmed from the decompile (no runtime logs needed):** the console blocker is `Debris.DroppedByPlayerID` — the collection logic excludes whoever's ID matches it (`Debris.cs:594`, plus the 1200ms grace gate at `:682`). On this Android build **`DroppedByPlayerID` is never assigned anywhere** — it appears only inside `Debris.cs`'s own read logic and is set in zero places across the entire decompile. `Game1.createItemDebris` doesn't set it either. So it stays `0`, the dropper is never excluded, and re-pickup is immediate. Purely an Android omission.
- **Candidate fix:** `createItemDebris` returns the `Debris`; in `Patches/InventoryManagementPatches.cs::DropHeldItem` (line ~1369) capture it and set `debris.DroppedByPlayerID.Value = Game1.player.UniqueMultiplayerID`. Re-engages the existing exclusion. Audit the other `createItemDebris` drop sites (ItemGrabMenuPatches ChestSwap fallbacks ~1845/1863/1870/1923/1937/1945/2195/2203, InventoryManagementPatches:1426) and decide which should also tag the dropper (the deliberate user-drop should; the "inventory full" failsafes probably yes too).
- **⚠️ TEST THE FEEL BEFORE CHANGING (user concern):** `timeBeforeReturnToDroppingPlayer` is only **1200ms and counts down in real time** — drop 2–3 things in a row and the timer on the first may already be expiring by the time you finish, so you'd start re-collecting the earliest drops. Console may use a longer window or a true leave-radius gate. **Reproduce + observe the actual console timing before picking the implementation** (longer constant vs. a "must exit radius" guard). Don't ship the bare 1.2s without confirming it matches console.
- **NOT related to** the `CancelHold()` cursor-held-item safety net (`InventoryManagementPatches.cs:1403`), which returns an on-cursor item to the bag when the menu closes. Different mechanism; rings are not "essential items" so vanilla's auto-return doesn't cover them.
- **Files:** `Patches/InventoryManagementPatches.cs` (+ audit `Patches/ItemGrabMenuPatches.cs` drop sites). Decompile ref: `Debris.cs:89/111/594/682`, `Game1.cs:11357`.

### 75. Shop Buy-List Jumps To Top On Purchase — ✅ DONE (device-verified G Cloud 2026-06-04: scroll v3.8.22, whitespace-shift v3.8.23, whitespace cause-fix v3.8.24)
- **Clarified trigger (2026-06-04):** NOT regular scroll navigation — it's **after a PURCHASE**: the list viewport jumps instead of staying still (and when you buy out a stack it re-selects the right item but the list still moves), so the selection can end up off-screen. Inconsistent.
- **v3.8.21 was wrong (log-disproven 2026-06-04):** the v3.8.21 fix restored `currentItemIndex`, but the device log showed `currentItemIndex` reads **0 the whole time** while the list is actually scrolling. On Android the buy list does **not** page via `currentItemIndex` — that's a PC vestige. The real scroll lives in the **MobileScrollbox `scrollArea`** (a pixel offset). User corrected the agent: "the cart test DID scroll, you just aren't capturing it."
- **Root cause (decompile-confirmed, corrected):** there is one `forSaleButton` per item, all positioned by `scrollArea.getYOffsetForScroll()` (`ShopMenu.cs:732`). `rebuildSaleButtons()` recreates them at **unscrolled** Y (`:2580`) and `update()` only re-applies the offset during momentum (`:1951`). So after a purchase the list visually snaps to the top; the selection (restored by `setCurrentItem`) lands off-screen if it was below the fold.
- **Fix (v3.8.22):** in `RebuildSaleButtonsAndRestoreSnap`, capture `scrollArea.getYOffsetForScroll()` before the rebuild; after, recompute `maxYOffset` = `(forSale.Count - itemsPerPage) * (itemButtonHeight + 8)` (the `setScrollBarToCurrentIndex` formula, `:1350`), clamp the old offset into the new range, re-apply via `setYOffsetForScroll`, and call `updateItemButtons()` to reposition every button. MobileScrollbox is Android-only → reflected off the runtime object. New `[ShopScroll] restore offset A -> B` VerboseLogging line verifies capture/restore. **Files:** `Patches/ShopMenuPatches.cs`.
- **Resolved (device-verified):** v3.8.22 holds the scroll across a purchase ("tested well"); v3.8.23 stopped a purchase from shifting pre-existing top whitespace; v3.8.24 fixed the *cause* of that whitespace — the game's DPad-up nav overshoots the offset positive with no top clamp (`ShopMenu.cs:948-950`), so `ClampScrollTop` pins it at 0 each frame on the buy tab. User: "the whitespace fix worked, good job."
- **Future (4.0 candidate):** there is **no built-in console ShopMenu on Android** (the port replaced it wholesale with the mobile rewrite — the PC shop UI isn't in the binary). A true console-style shop would be a from-scratch custom `IClickableMenu`, using the PC-DLL decompile as a blueprint (can't ship copied game source). Big effort: retires the nav/cursor/layout hacks but carries all the transaction logic (purchase + MP sync, storage/dresser deposits, trade items, recipes, festival ActionsOnPurchase). Scoped as a 4.0 epic; revisit only if the mobile shop still feels off after this scroll fix lands.

### 77. Restore Stripped Vanilla Options to the Android Options Page — ✅ DONE v3.8.25-31 (device-verified G Cloud 2026-06-04; zoom range confirm pending)
- **Requested 2026-06-04 (user):** make the console tool-hit options visible/usable in the in-game Options page, plus screen zoom; hide the touch-only "joypad" options when a controller is connected; and make the page scroll far enough to reach GMCM's button. Spec: `docs/superpowers/specs/2026-06-04-options-page-injection-design.md`; plan: `docs/superpowers/plans/2026-06-04-options-page-injection.md`.
- **Implemented (7 commits, new `Patches/OptionsPageInjectionPatches.cs`):** v3.8.25 dynamic scroll-fit (GMCM button reachable); v3.8.26 inject native tool-hit checkboxes 11/12 + retire the #76 per-tick force (pure vanilla, persists via StartupPreferences); v3.8.27 hide the 5 touch-only options (139/140/146/147 + Adjust-joypad button) when `gamepadControls`, GMCM toggle default on; v3.8.28 inject zoom slider (18); v3.8.29 console-ish ordering (tool-hit after Advanced Crafting 34, zoom before audio slider 1); v3.8.30 **zoom actually works** — drive the mobile PinchZoom pipeline (`ApplyMobileZoom`: `PinchZoom.Instance.SetZoomLevel` + base/desired zoom + `Window_ClientSizeChanged`, mirroring Game1.cs:1782-1787) since `desiredBaseZoomLevel` alone is ignored on mobile; v3.8.31 widen zoom bar to 50-200 (default 150).
- **Device-gate RESOLVED:** zoom **does** move the render via the PinchZoom wiring → **ships in 3.9.0**. **Supersedes #76** (force retired in favor of the native checkbox).
- **Only pending:** user's final confirm that the 50-200 range feels right (default 150 mid-bar). Engineering verified (log: "OptionsPage injection patches applied", no errors).

### 78. Dialogue Choice Boxes Use Yellow Tint Instead of Console Finger-Cursor + Red Outline — ✅ DONE v3.8.32→v3.8.39 (device-verified G Cloud 2026-06-04)
- **Resolved:** a `DialogueBox.draw` postfix (`Patches/DialogueBoxPatches.cs`, GMCM `EnableConsoleDialogueCursor`) overlays the selected response with the console look — covers the yellow box with the plain box, full-alpha text, a **maroon `rgb(128,0,0)`** outline (4px staminaRect bars), and the menu finger cursor (`mouseCursors` tile 44) on the bottom line ~20% in from the right; the `setUpQuestions` postfix pre-selects Yes. **Key gotcha:** both the overlay and the pre-selection had to drop the `gamepadControls && !lastCursorMotionWasMouse` gate — on Android `lastCursorMotionWasMouse` reads True even on a controller (synthesized touch), so the gate bailed (log-confirmed). See `DONE.md` "#78" + memory `android-lastcursormotionwasmouse-true-on-controller`. Color chosen via `tools/dialogue-outline-color-picker.html`. Spec: `docs/superpowers/specs/2026-06-04-dialogue-cursor-78-design.md`.

---

## v3.9.0 — Console Parity: Big Systems — ✅ SHIPPED 2026-06-04 (GitHub release v3.9.0 + Nexus)

**Shipped:** all bundled items done and device-verified — #25, #25b, #74, #73, #75, #77 (supersedes #76), #78. GitHub release `v3.9.0`, Nexus file uploaded via the publish workflow, description + version updated to 3.9.0. Only manual step remaining at release time: paste `release-notes/3.9.0-nexus-changelog.txt` on the Nexus version-history page. Next milestone: **v4.0 — The Right Stick Update** (#12, #62) below.

**Release plan (decided 2026-06-04):** 3.9.0 is the **next public release** — we do NOT cut a release per 3.8.x patch. It bundles everything unreleased since 3.8.0: **#25 charge-while-moving** (done v3.8.13), **#25b slingshot** (done v3.8.16), and the remaining small parity items **#74** (drop-blocker, ✅ done v3.8.17), **#76** (always-show-tool-hit-location, NEW), **#73** (seed target box — reopened, likely folds into #76), and **#75** (shop scroll). **4.0 = The Right Stick Update** (#12, #62) ships separately and should put us at ~100% console parity. Better-than-console extras (e.g. dual-stick slingshot) are NOT in either — they become their own mods.

Three player-facing real-time gameplay systems. Each likely needs multiple patches with device testing. Bundling them into one focused arc keeps testing context warm.

### 25. Tool Charging Broken While Moving — ✅ DONE v3.8.13 (device-verified G Cloud 2026-06-04, see `DONE.md`)
- Holding tool button while moving rapid-fires single uses instead of charging. Player stops moving and tool keeps firing.
- **Expected (console):** Holding tool button while moving begins charging. Player hops one square at a time.
- **NOT mod-caused.** Occurs regardless of layout. Android port difference.
- **Resolved:** two root causes — the held-button auto-repeat (`Game1.cs:13640`) re-firing + re-zeroing the charge, and the Android tap-to-move teardown (`_mobileUpdateControlInput`) stalling the charge on movement. Fix in `Patches/ToolUsePatches.cs` (Stage 1 suppress re-fire + Stage 2 re-assert charge-hold), GMCM `EnableMoveWhileCharging`. Upgraded Hoe + Watering Can; hop-to-grid deferred. Full writeup in `DONE.md`.

### 25b. Slingshot Combat — ✅ DONE v3.8.14→v3.8.16 (device-verified G Cloud 2026-06-04, see `DONE.md`)
- Having slingshot equipped stopped movement; slingshot didn't behave like console.
- **Expected (console):** Move freely, hold tool button to aim (stick controls crosshairs), release to fire.
- **NOT mod-caused (same subsystem as #25).** Android's tap-to-move/mobile-input layer (`Game1._mobileUpdateControlInput`) injected the slingshot's use-tool button events from stick/tap motion (`tapToMove.mobileKeyStates`), so stick motion alone fired it, a held button auto-fired, and movement was blocked. Aim was also mirrored/down-defaulting because Android defaults `Options.useLegacySlingshotFiring = true` (Switch uses non-legacy).
- **Resolved:** postfix `_mobileUpdateControlInput` to drive the three use-tool flags from the physical tool button only (hold→draw/aim with left stick, release→fire once), and force `useLegacySlingshotFiring=false` for console direct-aim. GMCM `EnableSlingshotAim`. Fix in `Patches/SlingshotAimPatches.cs`. Full writeup in `DONE.md`.
- **Dual-stick (better-than-console) is NOT an AC feature** — per the console-parity-only scope, it's spun out as a future **standalone mod** (right-stick aim-and-fire while walking). Design seed preserved in `docs/superpowers/specs/2026-06-04-slingshot-combat-25b-design.md` "Making it even better". See memory `feedback_ac_console_parity_only_extras_are_separate_mods`.

---

## v4.0.0 — The Right Stick Update

**Placement rule:** *all right-stick features ship in v4.0*, with slingshot aim (#25b) as the deliberate v3.9 exception. Major version bump because the right-stick cursor is the only remaining feature class that doesn't exist on Switch — calling v4.0 "The Right Stick Update" makes the bump narratively legible.

### 12. Right Joystick — Full Console Behavior — 🚧 CORE DONE (dev v3.9.1→v3.9.10, device-verified G Cloud 2026-06-05; NOT released)
- **Status (2026-06-05):** the overworld right-stick cursor is **functionally complete and device-verified** — visible self-drawn cursor, 4s auto-hide, interaction follows cursor (chest/NPC/gift), tool swings hit the cursor tile (cardinal AND diagonal), placement/furniture (#62) follow the cursor, no center-snap, slingshot carve-out intact. Toggle `EnableRightStickCursor` (default on). All in `Patches/RightStickCursorPatches.cs`. Full writeup in `DONE.md` + handoff `docs/superpowers/specs/2026-06-05-handoff-right-stick-cursor-wip.md`. **Remaining before the v4.0.0 release:** #79 contextual cursor (below), the stuck #76 tool-hit box, #77 settings persistence, and pre-release hygiene (`[RStickDiag]` demote, `EnforceTick` cleanup). Spec/plan: `docs/superpowers/specs/2026-06-04-right-stick-update-v4-design.md` + `docs/superpowers/plans/2026-06-04-right-stick-update-v4.md`.
- **Out of v4.0 scope (decided with user):** menu right-stick scroll, R3 chat/hold-emote, minigame right-stick→D-pad. Don't build.
- **Bundled feature (LARGE).** Scope = EVERYTHING the right stick does on console, not just the cursor. **Research complete:** `docs/superpowers/specs/2026-06-04-right-stick-console-behavior-research.md` (wiki/community + decompile). Write an implementation spec from it before coding.
- **Headline:** on console the right stick **is the free mouse cursor** (same cursor that drives tool targeting, tile interaction, placement, menu clicks). **This code already exists, complete, in the Android build** — `Game1.UpdateControlInput ~13298-13334` calls `setMousePositionRaw` from `ThumbSticks.Right`, gated only on `options.gamepadControls`. The blocker is the mobile path (`_mobileUpdateControlInput`) making the control type read TOUCH (suppresses `drawMouse`, the #18 trap) and/or `gamepadControls` effectively off. So this is mostly "let the existing path run + actually draw the cursor," not "build a cursor system."
- **Behaviors to cover (from the research):**
  - **Overworld:** right stick moves the free cursor → tool targeting follows it (hoe/can/pickaxe/axe hit the cursor tile); auto-hides after ~4 s (`timerUntilMouseFade=4000`), reverting tools to the facing tile. Likely a GMCM toggle since it changes targeting from facing-tile to cursor-tile (a real feel change).
  - **R3 click = in-game chat; hold R3 ≥250 ms = emote wheel** (`emoteMenuShowTime=250`). Decide whether to enable chat on Android single-player (maybe hold-emote only).
  - **Menus = scroll** (`updateActiveMenu ~5747-5769`: `receiveScrollWheelAction(±1)` at 0.2, timer `220-|Y|*170`). Coexist with AC snap nav (left/D-pad selects, right scrolls).
  - **Placement (furniture #62 / carpenter):** ghost follows the cursor — wire into AC's existing CarpenterMenu ghost-follow.
  - **Minigames:** right stick → D-pad key events (`4834-4865`) — low priority.
  - **Map:** cursor over the map (no viewport pan).
- **Implementation hooks:** `RawRightStickX/Y` already cached in `GameplayButtonPatches`; `setMousePositionRaw` (public), `thumbstickToMouseModifier` (reflect — private static), `timerUntilMouseFade`/`lastCursorMotionWasMouse`/`rightStickHoldTime`/`emoteMenuShowTime` (public static). Force `drawMouse` (set `mostRecentlyUsedControlType=GAMEPAD` on motion, or draw the cursor ourselves like #18). Full line refs in the research doc.
- **~~Zoom control~~ — DROPPED 2026-06-04 (user decision).** Superseded by #77: the in-game Options page now has a working zoom slider (`OptionsPageInjectionPatches`, whichOption 18, drives the PinchZoom pipeline). The menu slider is the better home for zoom than a right-stick gesture — do NOT re-add right-stick zoom.

### 62. Right-Stick to Move Furniture Ghost
- **Source:** Original "console furniture placement" ask had two parts. v3.5.38–v3.5.39 covered part 1 (single ghost rectangle + translucent sprite). Part 2: right stick moves the ghost the way it moves the carpenter building ghost.
- **Belongs in v4.0** because it's a right-stick feature — same placement rule as #12. **Likely falls out of #12**: the research (`docs/superpowers/specs/2026-06-04-right-stick-console-behavior-research.md`) confirms console drives the placement ghost off the **mouse cursor** (the same right-stick cursor as #12), NOT a separate facing-tile offset. So once #12's cursor exists, the furniture ghost should follow it directly — build #12 first, then verify furniture placement already tracks the cursor.
- **Behaviour to match:** Picking up furniture produces a ghost that follows the right-stick **cursor** (console parity). A places, B cancels (returns furniture to inventory). _(The earlier "offset relative to the facing tile, not the cursor" sketch below was wrong per the research — console uses the cursor. Kept for history.)_
- **Implementation sketch:**
  1. New `_furnitureGhostOffset` Vector2 in `FurniturePlacementPatches.cs`, accumulated each tick from `RawRightStickX/Y` (already cached in `GameplayButtonPatches`).
  2. Patch `Game1.GetPlacementGrabTile` to add `_furnitureGhostOffset` when the player has a Furniture as `ActiveObject`.
  3. Reset offset on placement success / B cancel / item swap. Reuse `OnFurnitureUpdateTicked` cadence in `CarpenterMenuPatches`.
  4. Optional: configurable max-distance clamp so the ghost can't drift off-screen.
- **Reuses:** Same pattern as `CarpenterMenuPatches` building-ghost cursor override. Code there is the gold standard for this pattern.
- **Files:** `Patches/FurniturePlacementPatches.cs`, possibly `Patches/GameplayButtonPatches.cs` for stick polling.
- **Config:** Probably gate behind the existing `EnableConsoleFurniturePlacement` flag — same feature, just the second half.
- **✅ Status (2026-06-05):** falls out of #12 — furniture/craftable/seed placement ghost follows the right-stick cursor (device-verified). Closed with #12's core.

### 79. Contextual Cursor Sprite (hand over interactables, speech bubble over NPCs) — ✅ DONE (reduced scope, v3.9.11→v3.9.16, device-verified G Cloud 2026-06-05)
- **✅ DONE at tile scope.** `RightStickCursorPatches.ResolveContextualCursor()` draws the contextual sprite for actionable/inspectable **tiles** (chest/mailbox → hand, signs/inspectables → magnifier, MessageSpeech → talk) + un-petted animal → grab, replicating the engine's pick from its per-tick hint flags (it computes the cursor in `drawMouseCursor` then resets it before our `RenderedHud` draw). **NPC speech-bubble/gift + forage/furnace/shipping-bin grab dropped on purpose** — they need a per-frame `checkForCharacterInteractionAtTile`/`canGrabSomethingFromHere` call (cost + side effect) and NPCs only show `cursor_talk` with pending dialogue. See `DONE.md` "#79 …" + `docs/superpowers/specs/2026-06-05-contextual-cursor-79-design.md`. **Bundled session fixes:** logging-flood cleanups (v3.9.12/14), #54 trigger fast re-pull `TriggerReleaseConfirmTicks` 4→2 (v3.9.15, log-verified 107/107 pulls), #73 fertilizer box-only placement (v3.9.16). Original notes below.
- **Ask:** on console the cursor sprite changes by what's under it — a **hand/finger** over interactables (chests, mailbox, shipping bin), a **speech bubble** over NPCs you can talk to, a magnifying glass over inspectables, etc. Our self-drawn cursor (`RightStickCursorPatches.DrawCursor`) currently always draws the default pointer.
- **Where the engine computes it:** `Game1.drawMouseCursor` (`Game1.cs:15647-15649`) sets `mouseCursor` to `cursor_talk`/`cursor_look`/`cursor_grab` from `isActionAtCurrentCursorTile`/`isSpeechAtCurrentCursorTile`/`isInspectionAtCurrentCursorTile`, then **resets `mouseCursor` to `cursor_default` at 15688** before our `Display.RenderedHud` draw runs — which is why we only ever see the pointer.
- **Plan:** capture the contextual index before the reset (a small postfix/field read on `drawMouseCursor`, or replicate the hover hit-test) and feed it to `DrawCursor`. Use the **PC 1.6 decompile** (`…/decompiler/stardew-valley-pc/…`) for the clean hover→cursor mapping. **Files:** `Patches/RightStickCursorPatches.cs`.

### (v4.0 follow-up) Stuck #76 tool-hit box — ✅ DONE (v3.9.17, device-verified G Cloud 2026-06-05)
- **✅ Fixed.** Prefix on `Character.GetToolLocation(Vector2, bool)` forces `ignoreClick=true` when `!Game1.wasMouseVisibleThisFrame`, so the box (which calls the Vector2 overload with the stale mouse position) reverts to the facing tile on fade. See `DONE.md` "#76 …". Original notes below.
- **User-reported 2026-06-05:** move the cursor → the #76 tool-hit box appears at the cursor; let the cursor fade, walk a new direction → the box stays pointing the OLD cursor direction even though you now hit the way you face.
- **Cause:** the #76 box (`Farmer.cs:6364-6368`) always targets `Game1.getMousePosition()` and never reverts when the cursor is gone. **Fix:** when `!Game1.wasMouseVisibleThisFrame`, target the box at `GetToolLocation(ignoreClick: true)` (facing tile). Needs patching the box draw in `Farmer.draw` — check the PC code first. **Needs the box VISIBLE to test → do alongside the settings-persistence fix below.**

### (v4.0 follow-up) #77 settings persistence — ✅ DONE (v3.9.18, device-verified G Cloud 2026-06-05)
- **✅ Fixed.** Root cause was NOT "not re-applied" — Android's `StartupPreferences` simply **doesn't save** `desiredBaseZoomLevel`/`alwaysShowToolHitLocation`/`hideToolHitLocationWhenInMotion` (decompile-confirmed), so the values were lost. AC now stores them in `ModConfig` (`SavedZoomPercent`/`SavedAlwaysShowToolHit`/`SavedHideToolHitWhenMoving`) and re-applies on `SaveLoaded` (`OptionsPagePatches.ApplySavedSettings`); zoom recorded on slider change (only re-applied if >0), tool-hit snapshotted on Options/GameMenu close. See `DONE.md` "#77 …". Original notes below.
- **User-reported 2026-06-05:** the native zoom + tool-hit-box options (injected by #77, stored in the game's StartupPreferences, NOT AC config) **reset after a full game restart.** This session was the first cold restart since 3.9.0 shipped, surfacing a latent #77 gap. **NOT caused by the v4.0 work** (`ModConfig` stores neither).
- **Likely cause:** the zoom render (PinchZoom pipeline) isn't re-applied from saved prefs on load, and/or the tool-hit checkboxes aren't reloaded. **Fix:** re-apply both from saved prefs on `SaveLoaded`/`GameLaunched`. **Files:** `Patches/OptionsPageInjectionPatches.cs`, `ModEntry.cs`.

---

## Future — Custom Keymapper (Device-Agnostic Remapping)

**Version: TBD (candidate for a 4.x). Own brainstorming session required.** Born from the Nexus Switch Pro thread (toolbar bug #1087126 + "buttons backwards" follow-up, 2026-06-03).

### 72. Custom Keymapper — learn-by-press button remapping
- **Why a model table won't work:** keysend is `controller × Android device`, NOT controller alone. A real Nintendo Switch Pro Controller sent **Xbox-positional** codes (right button → raw `B`) on a Samsung S26, but the same physical controller almost certainly reports differently on other phones (Nexus reporter kabusann2008 on a Pixel 9a hit the toolbar bug but never reported backwards buttons). So a "Controller" dropdown that maps *model → keysend* reintroduces the exact device-specific quirk it's trying to remove. We explicitly rejected the controller-database approach for this reason.
- **The chosen direction:** a **calibration / learn-by-press** flow. "Press the button that should confirm… now cancel… now the tool button…" — the mod records the actual raw codes for THIS device and builds the correct map, with zero assumptions about hardware we don't own. Self-healing across any controller/device combo. Persist the learned profile per device (keyed on `InputDevice.Descriptor` or vendor/product) in config.
- **Building blocks already proven (3.8.3 diagnostics):**
  - Controller identity IS readable at runtime via reflection: `GamePad.GamePads[i]` (`static AndroidGamePad[4]`) → `._device` (Android `InputDevice`) → `.VendorId` / `.ProductId` / `.Name` / `.Descriptor`. Nintendo vendor ID = `1406` (0x057E), Pro Controller product = `0x2009`. Decompile: `MonoGame.Framework/.../AndroidGamePad.cs` + `GamePad.cs`. Reflection-only (PC-safe with fallback) per the Android-vs-PC pattern.
  - Raw→game button mapping is already captured by the `[BtnMap]` diagnostic in `GameplayButtonPatches.GetState_Postfix` (3.8.3) — reuse its edge-detection to drive the calibration capture.
- **Related sub-issue to fold in — digital-only triggers:** the Switch Pro's ZL/ZR are *true digital buttons* (analog axis flatlined at `0.00`, only `Buttons.LeftTrigger/RightTrigger` flips). This is controller-intrinsic (not device-dependent), and it breaks toolbar item-switching after a row swap because (a) our suppression + `HandleTriggersDirectly` are gated on the analog axis, so the mod's handler no-ops and the game's `pressSwitchToolButton` leaks through, and (b) the bumper row-swap arms `_triggerSlotTarget` and the digital trigger emits no SMAPI event to clear it, so the lock pins `CurrentToolIndex` (only a bumper / D-pad-L/R clears it — confirmed on device). **This trigger fix is device-agnostic and could ship independently/sooner** than the full keymapper: fold the digital flag into `RawLeftTrigger/RawRightTrigger` so the mod's handler runs (switching items + keeping the lock fresh) and strip the digital button so the game stops fighting. See 3.8.2 `[SPDiag]` analysis in the 2026-06-03 logs.
- **Current workaround (no code):** a Switch Pro on the S26 should be set to **layout = Xbox + style = Switch** (the Pro reports Xbox-positional there). User accepted this for now.
- **Cleanup owed before any release:** 3.8.2/3.8.3 added always-on INFO diagnostics (`[SPDiag]`, `[BtnMap]`, `[ToolIdx]`). These are local-only (not on Nexus) but must be gated behind Verbose Logging or removed before shipping anything past 3.8.1.

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

## Beyond Vanilla — Optimizations Over Console

These items intentionally diverge from console parity to make the Android experience FASTER or more efficient than vanilla / Switch. No version commitment yet — each likely needs its own brainstorming session.

### 70. Geode Cracking — Speed Past Vanilla
- v3.7.33–v3.7.35 brought the Clint geode menu to single-press A console parity (A redirects to X, full inventory gives a buzzer + slot shake, menu opens auto-selected on the first geode). This item is the FOLLOW-UP that goes beyond parity: cracking a 999-stack of Omni Geodes one-press-at-a-time is still a chore even with parity.
- **Vanilla flow recap:** each A press triggers ~2.7s of animation (geode lands on anvil → Clint swings hammer → fluff sprites → reward drawn). The animation is single-threaded — no overlap between cracks. So 999 geodes ≈ 45 minutes of holding A.
- **Possible improvements** (pick + brainstorm later):
  - Hold-to-crack with auto-repeat (same as #69 pattern): A press → crack one, hold A → keep cracking at the animation's natural cadence, ignoring single-press intent.
  - Bulk-crack mode: A long-press or a separate button cracks N at once with a single condensed animation. Treasure stacks merge into inventory in one batch.
  - Skip-animation toggle: GMCM option to compress the geode-crack animation to ~0.3s (or skip entirely) when a stack is being processed. Keep full animation for single cracks to preserve the satisfying feel.
- **Files:** new `Patches/GeodeMenuPatches.cs` extensions (existing file owns the menu); possibly a new `BulkActionMenu` UI if we add a confirm-to-bulk-crack flow.
- **Toggle:** `EnableFastGeodeCracking` (default true — most players will want this).

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
