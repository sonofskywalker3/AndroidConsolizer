# Tool Charging While Moving (TODO #25) — Phase 2 Design (the fix)

**Date:** 2026-06-04
**Milestone:** v3.9.0 — Console Parity: Big Systems
**Status:** Approved design, pending implementation plan
**Scope (v1):** Hoe + Watering Can, **upgraded (Copper+) only**
**Supersedes:** the Phase-2 sketch in `2026-06-04-tool-charging-while-moving-design.md`
(this is the data-grounded version after the Phase-1 diagnostic).

## Problem (recap)

On Android, holding the Hoe / Watering Can button while moving **rapid-fires single
tool uses** and **locks movement**, instead of charging the area effect like console.
Phase 1 confirmed this is **vanilla Android behavior, not mod-caused** — the mod
delivers a clean sustained hold (`rawX == finalX == True`, zero flicker).

## Confirmed root cause (Phase-1 diagnostic + decompile)

**The re-fire is Android's held-button auto-repeat at `Game1.UpdateControlInput`
(`Game1.cs:13640-13648`):**

```csharp
if (state2.IsButtonDown(Buttons.X) && oldPadState.IsButtonDown(Buttons.X) && !(player.CurrentTool is MeleeWeapon))
    flag2 = true;
...
if (flag2) { useToolButtonPressed = true; mouseClickPolling = 100; }
```

A **held** X (down this frame *and* last frame) forces `useToolButtonPressed = true`
**every frame** — deliberately *not* a rising edge. This is the Android affordance so a
held finger keeps re-using a tool. The gamepad rising-edge at `Game1.cs:13452` fires only
once; this block is what keeps the press "stuck on."

That forced flag drives the press block at `Game1.cs:13884`, gated by
`(!player.UsingTool …)`:

1. Held X → `useToolButtonPressed = true` every tick.
2. Press block calls `player.FireTool()` (single use) **then** `pressUseToolButton()`,
   which **zeroes `toolPower`/`toolHold`** (`Game1.cs:12269-12270`) and `BeginUsingTool()`.
3. While the use animation runs, `UsingTool = true` → press block is skipped.
4. The moment the use ends (`UsingTool → false`, `CanMove → true`), the still-forced
   `useToolButtonPressed` **re-fires instantly** → back to step 2, ~43 ticks/cycle.

**Why it only breaks while moving (confirmed in `test-output/SMAPI-latest.txt`):**
the upgraded can's *charge* path (`Tool.cs:638-660`) sets a **static hold frame** and
leaves `canReleaseTool = true`, so when the use settles into that hold, `UsingTool` stays
true forever and the engine's charge ramp (`Game1.cs:13914`) accumulates
`power 0→1→2→3→4` (log ticks 121722–122166, charging while *stationary*). But while
moving, the use keeps **resolving to a completing animation** (`UsingTool` drops each
cycle), so the auto-repeat re-fires and `pressUseToolButton` re-zeroes the charge every
43 ticks (log ticks 120540–120841, `hold=0` throughout). Movement is the discriminator
because it keeps ending the use instead of letting it settle into the static charge-hold.

> Note: the exact secondary path that ends the use each cycle while moving (one of the
> `completelyStopAnimatingOrDoingAction` / halt / animation-done paths) could not be
> pinned with certainty from static reading. The staged plan below is designed so we do
> **not** need to know it for Stage 1, and only confront it in Stage 2 if the device test
> proves we must.

### Decompile references (Android build)
- Held-button auto-repeat (the re-fire source): `Game1.cs:13640-13648`.
- Press block (fire + reset): `Game1.cs:13884-13901`; `FireTool` is called **only** here.
- `pressUseToolButton` zeroes `toolPower`/`toolHold`: `Game1.cs:12269-12270`.
- Charge ramp (the engine's own, works when uninterrupted): `Game1.cs:13914-13935`.
- Edge vs held flags: `Game1.cs:13452` (`useToolButtonPressed` = X rising edge),
  `Game1.cs:13471` (`useToolHeld` = X level).
- Movement gate: `Game1.cs:13956` (`!UsingTool || canStrafeForToolUse()`);
  `Farmer.canStrafeForToolUse()` `Farmer.cs:8603-8614` (false while `toolPower < 1` for
  the first 150ms, then true).
- Charge-hold vs single-use branches: `Tool.beginUsing` `Tool.cs:589-660` (watering can
  charge-hold at 638; basic-tool instant end at 596).
- Charge fields: `Farmer.cs` `toolPower:402`, `toolHold:405`,
  `toolPowerIncrease():6984`.

## Goal (v1)

Holding an **upgraded Hoe or Watering Can** while walking should **charge** the area
effect (wind it up) instead of rapid-firing single uses + locking movement. On release,
the charged area fires at the tiles in front. The character slides freely during the
charge (**no console grid-hop in v1**). A quick **tap** still performs exactly one normal
single use.

**Confirmed scope decisions (this session):**
- **Basic (level-0) Hoe / Watering Can stay single-use** — vanilla has no level-0 area
  effect (`Tool.cs:596` ends the use instantly), so there is nothing to charge. Matches
  console. Charging applies to **upgraded (Copper+)** tools only.
- **Keep vanilla's brief start hitch** — movement stays locked for the first ~150ms of a
  charge (until `toolPower ≥ 1`, per `canStrafeForToolUse`), then slides freely. We do
  **not** force movement from frame one. Lowest risk, matches the engine's own pacing.

**Out of scope for v1 (unchanged):**
- Pickaxe / Axe (no vanilla charge area; "keep mining while walking" is separate, deferred)
  — **must not regress.**
- Console grid-snap / hop-to-tile-center during charge.

## Approach (A — staged, minimal-first)

**Core model — "one begin per hold":** allow exactly one tool-use *begin* per physical
hold of the use-tool button; **block every subsequent re-fire** during that same hold.
This removes the auto-repeat's spurious `FireTool` (the rapid single uses) and its
spurious `pressUseToolButton` (the charge reset). With the resets gone, the engine's
*own* charge ramp (`Game1.cs:13914`) keeps its accumulated `toolPower`,
`canStrafeForToolUse()` lets the player slide once `power ≥ 1`, and vanilla
`EndUsingTool` fires the charged area on release. **No custom charge logic in Stage 1.**

### Stage 1 — hold-session suppression (this build)

New `Patches/ToolUsePatches.cs`.

**Hold-session state.** Track the physical use-tool button (post X/Y-swap) via the
existing raw-state tracking in `Patches/GameplayButtonPatches.cs`
(`RawLeftStickX/Y` already live there; the button's physical state is read the same way).
- On the **rising edge** of the use-tool button while a **qualifying tool** (upgraded Hoe
  / Watering Can) is equipped and `EnableMoveWhileCharging` is on → start a session:
  `_holdActive = true`, `_beginConsumed = false`.
- The **first** use-begin while `_holdActive && !_beginConsumed` is the legitimate press:
  let it through and set `_beginConsumed = true`.
- Every **subsequent** use-begin while `_holdActive && _beginConsumed` is a spurious
  re-fire; **suppress it** (see below).
- On the button's **release** → clear `_holdActive` / session flags and let vanilla
  `useToolButtonReleased → EndUsingTool` fire the charged area.

**Suppression points** (prefixes that skip the original — return `false` — only when
`EnableMoveWhileCharging` && qualifying tool && `_holdActive` && `_beginConsumed`, i.e. it
is a re-fire, **never** the first press):
- `Farmer.FireTool` — skips the spurious single-use swing.
- `Game1.pressUseToolButton` — skips the spurious charge reset + re-begin.

The **first** press of each hold passes through untouched → vanilla begins the tool and
the charge-hold, preserving tap-vs-hold and normal single-use.

**Stage-1 bet:** with the re-fire churn removed, the static charge-hold persists and the
vanilla ramp accumulates while moving. Cheap to test; if true, we are done with the most
vanilla-aligned fix possible.

**Test (meaningful playtest).** Claude deploys to G Cloud. User: (a) equip an upgraded
Watering Can with water, hold the use button and walk a few tiles, release; (b) same with
an upgraded Hoe on tillable ground. Claude pulls + analyzes the log: confirm the charge
ramps while moving (`toolPowerIncrease` fires with movement input present) and the
charged area fires on release, with **no** rapid single uses. Ships as one `0.0.1`.

### Stage 2 — active charge maintenance (only if Stage 1's device test fails)

Triggered **only** if Stage 1 shows the use still self-terminates while moving and the
charge will not accumulate. Adds, in the same `ToolUsePatches.cs`:
- Keep the use alive while held + qualifying tool: re-assert `UsingTool = true` /
  `canReleaseTool = true` and re-establish the charge-hold frame after the engine tries
  to end it (intercept the end path or re-arm each tick).
- If the vanilla ramp still will not run, **drive the charge ourselves** — advance
  `toolHold` / call `Farmer.toolPowerIncrease()` on the vanilla cadence (the 600ms ×
  `AnimationSpeedModifier` schedule from `Game1.cs:13914-13935`), capped at the tool's
  `upgradeLevel`.
- Release → vanilla `EndUsingTool` fires the charged area (already correct).

Stage 2 is its own `0.0.1` (or several), one change per commit.

## Configuration

New GMCM toggle **`EnableMoveWhileCharging`** in `ModConfig.cs` (default `true`),
following the existing `Enable*` pattern, wired into the GMCM menu in `ModEntry.cs`.
Toggle **off** → all suppression/maintenance is bypassed and vanilla behavior returns.

## Diagnostic cleanup (part of this phase)

Remove the Phase-1 throwaways:
- Delete `Patches/ToolChargeDiagnosticPatches.cs` and its registration in `ModEntry.cs`
  (`ModEntry.cs:212`).
- Remove the diagnostic captures in `Patches/GameplayButtonPatches.cs`:
  `DiagRawToolX/DiagRawToolY/DiagFinalToolX/DiagFinalToolY` (declared line 121; assigned
  ~353-354, 477-478, 506-507, 542-543) and any now-dead references. Keep `RawLeftStickX/Y`
  (still used by the new feature). Do this as its own `0.0.1`, separate from the fix
  commits.

## Error handling

- All Android-differing members (`FireTool`, `pressUseToolButton`, `toolPowerIncrease`,
  `canStrafeForToolUse`, `toolPower`/`toolHold` fields) reached via
  `AccessTools.Method/Field` with null-checks and a PC-safe fallback (compiles against the
  PC DLL, runs on Android).
- Every patch body wrapped in try/catch that logs (Debug, gated on `VerboseLogging`) and
  **falls through to vanilla** — a fault in this feature must never break tool use.
- "Qualifying tool" guard (`is Hoe or WateringCan` && `UpgradeLevel >= 1`) re-checked in
  every suppression body so a tool swap mid-hold cannot strand the suppression on.

## Testing & regression

- Upgraded Watering Can: hold + walk → charges; release waters the charged area. No rapid
  single uses, no movement lock past the ~150ms start hitch.
- Upgraded Hoe: hold + walk → charges; release tills the charged area.
- Quick tap (either tool) → exactly one single use (tap-vs-hold preserved).
- Basic Hoe / Watering Can → unchanged single-use.
- Pickaxe / Axe → unchanged (must not regress).
- Toggle off → vanilla behavior returns.
- Stationary charge (already working) → unchanged.

## Versioning

- Diagnostic cleanup: one `0.0.1`.
- Stage 1 fix: one `0.0.1` (e.g. v3.8.x).
- Stage 2 (if needed): subsequent `0.0.1`(s), one change per commit per project rules.

## Files

- `Patches/ToolUsePatches.cs` (new) — hold-session tracking + suppression (Stage 1) and,
  if needed, charge maintenance (Stage 2).
- `Patches/GameplayButtonPatches.cs` — remove Phase-1 `Diag*` captures; keep `RawLeftStickX/Y`.
- `ModConfig.cs` — `EnableMoveWhileCharging` (default `true`).
- `ModEntry.cs` — register `ToolUsePatches`; add GMCM entry; remove the
  `ToolChargeDiagnosticPatches.Apply` call.
- `Patches/ToolChargeDiagnosticPatches.cs` — **deleted**.
