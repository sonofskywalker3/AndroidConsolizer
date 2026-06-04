# Tool Charging While Moving (TODO #25) — Design

**Date:** 2026-06-04
**Milestone:** v3.9.0 — Console Parity: Big Systems
**Status:** Approved design, pending implementation plan
**Scope (v1):** Hoe + Watering Can only

## Problem

On the Android port, holding the tool button (Hoe / Watering Can) while the player
is moving **rapid-fires single tool uses** and **locks the player's movement**,
instead of entering the charge-up state. On console (Switch), holding the tool
button while moving begins charging the tool (winding up the larger area effect),
and the player keeps moving (console additionally hops one grid square at a time).

This is an Android port difference, not mod-caused — it reproduces regardless of
controller layout.

## Goal (v1)

Holding the **Hoe** or **Watering Can** while walking should **charge the tool**
(wind up the bigger area effect) rather than rapid-firing. On release, the charged
area effect fires at the tiles in front of the player. The character may slide
freely during the charge — the console-style **hop-to-grid-center is explicitly
out of scope for v1**. A quick tap must still perform a single normal use
(tap-vs-hold distinction preserved).

Out of scope for v1:
- Pickaxe / Axe (no charge area-effect in vanilla; "keep mining while walking" is a
  separate behavior, deferred).
- Console grid-snapping / hop-to-tile-center during charge.

## Decompiled Android findings (root-cause map)

Source: `C:\Users\Jeff\Documents\Projects\decompiler\stardew-valley-android\decompiled\StardewValley\StardewValley\`

The charge state machine on Android is **structurally identical to console**:

- **Charge fields** — `Farmer.cs`: `toolPower` (L402), `toolHold` (L405),
  `toolHoldStartTime` (L1111). Charge increments via `Farmer.toolPowerIncrease()`
  (L6984).
- **Press path** — `Game1.UpdateControlInput` (L13884–13901): on a tool-button
  *press*, `player.FireTool()` (L13888) fires immediately, then
  `Game1.pressUseToolButton()` runs. `pressUseToolButton()` **resets**
  `toolPower` and `toolHold` to 0 (Game1.cs L12269–12270) on every press.
- **Charge ramp** — `Game1.UpdateControlInput` (L13914–13935): only on a *sustained
  hold* (`useToolHeld && canReleaseTool && …`) does `toolHold` count down and then
  `toolPowerIncrease()` (L13932) fire.
- **Movement gate** — `Game1.UpdateControlInput` (L13956): movement input is only
  processed when `!player.UsingTool || player.canStrafeForToolUse()`.
  `Farmer.canStrafeForToolUse()` (Farmer.cs L8603–8614) returns `false` while
  `toolPower < 1` for the first 150ms of the hold, then `true`.
- **Movement halt on use** — `Farmer.performBeginUsingTool` (L6881–6882) sets
  `CanMove = false`, `UsingTool = true`; `Tool.beginUsing` (Tool.cs L594) calls
  `who.Halt()`.
- **Mobile input source** — `Game1.cs` L2446–2457: `useToolButtonPressed` /
  `useToolHeld` / `useToolButtonReleased` are fed from
  `currentLocation.tapToMove.mobileKeyStates` on Android.

**Leading hypothesis:** Android delivers the controller tool button as **repeated
press-edges** rather than one **sustained hold**. Each frame re-fires the tool
(L13888) and re-zeroes the charge (L12269–12270), so the charge ramp never
accumulates and `canStrafeForToolUse()` stays `false` (movement stays locked).
**This input-delivery detail cannot be confirmed from the decompile — it requires a
device diagnostic.**

## Approach: diagnostic-first, two-phase

Chosen over a direct fix because the precise Android input-delivery behavior is
unknown, and the project rule is "one diagnostic build > ten wrong fix attempts"
for unknown root causes (`.claude/CLAUDE.md` — Diagnostic-First Development).

### Phase 1 — Diagnostic build

New throwaway patch set `Patches/ToolChargeDiagnosticPatches.cs`. Logs one tagged
`[ToolCharge]` line per relevant tick, **only while a Hoe or Watering Can is the
current tool**, **gated behind Verbose Logging** so it can never leak into a
shipped build (per the always-on-diagnostic cleanup lesson from 3.8.2/3.8.3).

Instrumentation points:
- `Game1.pressUseToolButton` (prefix) — tick; `toolPower`/`toolHold` *before* reset;
  `UsingTool`; `CanMove`; `movementDirections.Count`; tool name. **Call cadence is
  the key signal**: frequent calls ⇒ repeated press-edges (rapid-fire cause).
- `Farmer.FireTool` (prefix) — tick the tool actually fired (rapid-fire cadence).
- `Farmer.toolPowerIncrease` (prefix) — tick charge actually incremented (does it
  ever?).
- `Farmer.canStrafeForToolUse` (postfix) — tick + return value (movement-gate
  decisions).

All patch targets resolved via `AccessTools` with null-checks and PC-safe fallback
(Android-vs-PC reflection pattern); every patch body wrapped so a diagnostic
failure cannot break tool use.

**Test (meaningful playtest):** Claude deploys to G Cloud. User holds the Hoe and
walks a few seconds, then the Watering Can and walks. Claude pulls + analyzes the
log. Ships as one `0.0.1` (v3.8.6).

**Checkpoint:** Phase 2 is specified from the captured data before any fix code.

### Phase 2 — The fix (data-driven)

Decided from Phase 1 data. Leading-hypothesis levers, already identified:
1. **Sustain the hold** — make the game observe a continuous `useToolHeld` (derived
   from the mod's existing raw button-state tracking in
   `Patches/GameplayButtonPatches.cs`) so the charge ramp runs, and stop the
   per-frame fire-and-reset after the first press so `toolPower` accumulates.
2. **Free the movement** — ensure `canStrafeForToolUse()` effectively returns true
   for these two tools so walking is not gated during the charge.

Likely lands in a new `Patches/ToolUsePatches.cs`, plus `ModConfig.cs` (toggle) and
`ModEntry.cs` (GMCM entry + patch registration); may read held-tool-button state
from `Patches/GameplayButtonPatches.cs`. The Phase-1 diagnostic is demoted/removed
in this phase.

**Config:** new GMCM toggle **`EnableMoveWhileCharging`** (default on). Toggle off
restores vanilla behavior.

## Testing & regression (Phase 2)

- Hold Hoe while walking → charges; release tills the charged area. Same for
  Watering Can (waters charged area).
- No rapid-fire single uses; no movement lock during charge.
- Quick tap still performs exactly one single use (tap-vs-hold preserved).
- Pickaxe / Axe behavior unchanged (must not regress).
- Toggle off ⇒ vanilla behavior returns.

## Error handling

- All Android-differing members (`canStrafeForToolUse`, `FireTool`,
  `toolPowerIncrease`, `pressUseToolButton`) reached via `AccessTools.Method/Field`
  with null-checks and a PC-safe fallback path (the mod compiles against the PC DLL
  but runs on Android).
- Every patch body wrapped in try/catch that logs and falls through to vanilla —
  a fault in this feature must never break tool use.

## Versioning

- Phase 1 diagnostic: one `0.0.1` (v3.8.6).
- Phase 2 fix: subsequent `0.0.1`(s), one change per commit per project rules.

## Files

**Phase 1:** `Patches/ToolChargeDiagnosticPatches.cs` (new), `ModEntry.cs`
(register the diagnostic patch set).

**Phase 2 (anticipated):** `Patches/ToolUsePatches.cs` (new), `ModConfig.cs`
(`EnableMoveWhileCharging`), `ModEntry.cs` (GMCM + registration), possibly
`Patches/GameplayButtonPatches.cs` (held-button state).
