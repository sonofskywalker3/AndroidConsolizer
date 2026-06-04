# Handoff — Tool Charging While Moving (#25), Phase 1 diagnostic COMPLETE

**Date:** 2026-06-04
**Shipped tip:** v3.8.7 (diagnostic build, deployed on the G Cloud)
**Status:** #25 Phase 1 (diagnostic) DONE. Root cause confirmed. Ready for Phase 2 (the fix) — data-grounded design, no more device captures needed.

## Where this sits

- Design spec: `docs/superpowers/specs/2026-06-04-tool-charging-while-moving-design.md`
- Phase 1 plan: `docs/superpowers/plans/2026-06-04-tool-charging-while-moving-phase1.md`
- Scope (v1): **Hoe + Watering Can only.** Pickaxe/Axe and console grid-hop are out of scope.
- Goal: holding the tool while walking should **charge** (wind up the area effect) instead of rapid-firing single uses + locking movement. Character may slide freely during charge (no console grid-snap in v1). Quick tap still = single use.

## THE ANSWER (Phase 1 diagnostic result)

**It is NOT mod-caused. It is vanilla Android behavior.** The mod delivers the tool button perfectly; the game refuses to charge while a movement direction is held.

Two diagnostic builds were used:
- **v3.8.6** (`Patches/ToolChargeDiagnosticPatches.cs`): instrumented `Game1.pressUseToolButton`, `Farmer.FireTool`, `Farmer.toolPowerIncrease`, `Farmer.canStrafeForToolUse`.
- **v3.8.7**: added raw-vs-final tool-button capture (`GameplayButtonPatches.DiagRawToolX/Y/DiagFinalToolX/Y`, logged once/tick in `CanStrafeForToolUse_Postfix`).

**Both are THROWAWAY — delete/demote them in Phase 2.**

### Evidence (G Cloud, Iridium Watering Can, from `test-output/SMAPI-latest.txt`, ~tick 120540+)

While holding the tool AND moving (`Lstk=(1.00,0.00)`), every tick showed:
```
rawX=True  finalX=True   power=0  hold=0  using=True  canStrafe=False  moving=True
```
- **`rawX == finalX == True`, continuously, zero flicker** → the mod's X/Y swap + suppression are NOT dropping the tool button. The game receives a clean sustained hold. **Mod exonerated.**
- `toolPowerIncrease` (charge tick) fired **8×, every one with `moving=False`. Zero while moving.**
- While moving, `FireTool` fired at ticks 120540, 120583, 120626, 120669, 120712, 120755, 120798, 120841 — **exactly ~43 ticks apart** (≈0.72s = the watering-can use animation). One use per animation cycle = the "rapid-fire."
- The moment the stick centered (`Lstk=(0,0)`), `toolPowerIncrease` began firing and it charged 0→4 normally.

Earlier (v3.8.6) a stationary charge cycle was captured working perfectly: hold ramps 200→8 → `toolPowerIncrease` → power 1; hold 600→8 → power 2 → 3 → 4; release fires the charged area.

### Mechanism (confirmed)

The discriminator is **movement input (left stick deflected)**. While moving, the game re-fires the tool once per animation cycle; each fire calls `pressUseToolButton()` which **zeroes `toolHold`/`toolPower`** (decompile `Game1.cs:12269-12270`), so the charge ramp can never accumulate past the reset. Stationary = no re-fire = ramp accumulates uninterrupted.

**Open question for Phase 2 (answerable from the decompile — no device needed):** *why* does the held tool button re-fire every ~43 ticks while moving when `Buttons.X` is held continuously (so `useToolButtonPressed`, which needs a rising edge per `Game1.cs:13452`, should be false)? Pin the re-fire path: read how the watering-can `beginUsing`/`endUsing` and `UpdateControlInput` (the press block at ~`Game1.cs:13884` and the charge ramp at ~`13914`) behave on animation-complete while a movement direction is held. The leading fix lever is to **suppress that movement-triggered re-fire** (so `toolHold` stops getting reset and the existing ramp builds), scoped to Hoe/WateringCan; alternative is to drive `toolPowerIncrease` ourselves.

### Decompile references (Android build)
- Charge ramp + press block: `Game1.cs` ~13884 (press → FireTool + pressUseToolButton), ~13914 (ramp: `useToolHeld && canReleaseTool && !flag4 && !dialogueUp && Stamina>=1`), ~13452 (`useToolButtonPressed` = X rising edge), ~13471 (`useToolHeld` = X level).
- `pressUseToolButton` resets toolPower/toolHold: `Game1.cs:12269-12270`.
- Movement gate: `Game1.cs:13956` (`!UsingTool || canStrafeForToolUse()`); `Farmer.canStrafeForToolUse()` `Farmer.cs:8603-8614` (false while toolHold==0 / first 150ms).
- Charge fields: `Farmer.cs` toolPower:402, toolHold:405, toolPowerIncrease():6984.

## NEXT STEP

Phase 2 = the fix. Recommended path: data-grounded brainstorm → spec (`docs/superpowers/specs/2026-06-04-tool-charging-while-moving-phase2-design.md`) → plan → implement (subagent-driven). First design task: pin the re-fire path from the decompile, then choose the suppression lever. Remove the v3.8.6/3.8.7 diagnostics as part of Phase 2.

## Device / workflow state
- **v3.8.7 is deployed + loaded on the G Cloud.** VerboseLogging is ON in the device config.
- Deploy + log-pull are Claude's job (`cd SyncdewValley; pwsh -NoProfile -File sync.ps1 deploy|logs`). User only does in-game tests. NEVER tell the user to pull logs.
- `sync.ps1 logs` force-stops the game; don't relaunch unless something is testable.
- The analyzed log is `test-output/SMAPI-latest.txt` (will be overwritten on the next pull — key numbers are quoted above). The user may switch the device to another mod (CartCatalog) between sessions, so re-confirm v3.8.7 is loaded before trusting a fresh pull.
- Primary test device: G Cloud (Xbox layout + Switch style; X/Y swap active in gameplay; A/B swap active).

## Other open items touched this session (logged, NOT being worked yet)
- **#73 Seeds exempt from placement ghost** — FIX SHIPPED in **v3.8.5** (released to Nexus). `Patches/FurniturePlacementPatches.cs` excludes `Category == SeedsCategory`. **Awaiting device verification** (plant seeds → no ghost). NOTE: the Nexus **changelog paste is still a manual step** for v3.8.5 (`release-notes/3.8.5-nexus-changelog.txt`) if not already done.
- **#74 Dropped items instantly re-collectable** — logged in TODO.md. Root cause confirmed: Android never sets `Debris.DroppedByPlayerID` (set nowhere in the decompile), so the console drop-blocker never engages. Candidate fix: set `debris.DroppedByPlayerID.Value = Game1.player.UniqueMultiplayerID` after `createItemDebris` in `InventoryManagementPatches.DropHeldItem`. **⚠️ TEST THE 1.2s `timeBeforeReturnToDroppingPlayer` FEEL FIRST** (user concern: too short — counts down in real time; dropping 2-3 things may let the earliest re-collect). Not a mod safety net (that's `CancelHold`, unrelated).
- **#75 Shop list visual scroll** — logged in TODO.md. Visual only (cursor/selection correct, list viewport scrolls; selected item can go off-screen). Needs a `Patches/ShopMenuPatches.cs` code dive + visual diagnostic; logs don't capture it. CartCatalog also patches ShopMenu — rule in/out interaction.
- **Creekside Farm save** backed up to `SyncdewValley/sync/backups/Creekside_428888887_20260604-102542/`.

## Hard rules (this workspace)
- Never push/publish (`git push`, `gh release`, Nexus) without explicit user "yes." Local commits + version bump on every change are expected.
- One change per commit, bump `manifest.json` first. PATCH 0.0.1 for iteration/diagnostics; MINOR 0.1.0 for a fix release.
- NEVER `/sdcard/` — always `/storage/emulated/0/`.
- Diagnose-first: read the decompile (`C:\Users\Jeff\Documents\Projects\decompiler\stardew-valley-android`) before any fix; use `AccessTools` + null-checks for Android-differing members; wrap patch bodies in try/catch.
