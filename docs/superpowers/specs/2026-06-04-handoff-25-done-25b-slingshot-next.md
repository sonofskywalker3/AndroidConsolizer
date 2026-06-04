# Handoff — #25 Tool-Charging-While-Moving DONE; #25b Slingshot is next

**Date:** 2026-06-04
**Tip:** v3.8.13 (committed to `master`, local only — NOT pushed/published)
**Device:** G Cloud is loaded with **v3.8.12** (functionally identical for normal play; v3.8.13 only adds an events/cutscene guard, not yet deployed). VerboseLogging is ON.

## What just finished — #25 Tool Charging While Moving ✅ (device-verified)

Holding an upgraded Hoe/Watering Can while walking now **charges** the area effect (console parity) instead of rapid-firing + locking movement. User-confirmed feel + log-confirmed (`power 1→2→3→4` while `moveDirs=1`, `canRelease=True`).

- Full writeup: `DONE.md` → "#25 Tool Charging While Moving".
- Design: `docs/superpowers/specs/2026-06-04-tool-charging-while-moving-phase2-design.md`
- Plan: `docs/superpowers/plans/2026-06-04-tool-charging-while-moving-phase2.md`
- Code: `Patches/ToolUsePatches.cs` (new), `Patches/GameplayButtonPatches.cs` (game-facing use-tool release detection), `ModConfig.cs` + `ModEntry.cs` (toggle `EnableMoveWhileCharging` + GMCM + registration).
- Commits v3.8.8 → v3.8.13.

### Two root causes pinned (both Android-only, from the decompile) — READ THESE, they recur in #25b
1. **Held-button auto-repeat** — `Game1.UpdateControlInput` `Game1.cs:13640` forces `useToolButtonPressed=true` *every frame* a non-melee tool button is held (`IsButtonDown(X) && oldPadState.IsButtonDown(X)`), not a rising edge. So a held tool button re-fires every animation cycle; `pressUseToolButton` re-zeroes `toolPower`/`toolHold` (`Game1.cs:12269-12270`) each fire.
2. **Tap-to-move teardown on movement** — `Game1._mobileUpdateControlInput` (`Game1.cs:13614`, runs every tick BEFORE the charge ramp `~13914`) tears down an in-progress tool use the instant a movement direction is held: `canReleaseTool`/`useToolHeld` drop, the ramp stalls, `Farmer.canStrafeForToolUse()` returns false, the use force-ends. **Memory: `android-taptomove-clobbers-tool-on-move`.**

The #25 fix = Stage 1 (suppress the re-fire: "one tool-use begin per hold") + Stage 2 (postfix `_mobileUpdateControlInput` to re-assert `useToolHeld`/`useToolButtonReleased`/`canReleaseTool`/`UsingTool` so the engine's own ramp + strafe work), guarded with an `eventUp || farmEvent` early-out.

## Next — #25b Slingshot Combat (TODO.md item 25b)

**Goal (console parity):** move freely with a slingshot equipped; **hold** the tool button to aim (stick controls the crosshairs); **release** to fire. Today on Android, equipping a slingshot stops movement and it doesn't aim like console.

**Why it's grouped with #25:** both mix a **held tool/weapon button + movement** + a **continuous held aim/charge state** — exactly what the two root causes above attack. Expect the tap-to-move clobber and possibly the held-button auto-repeat to be involved. The slingshot's aim is a *continuous held state* (pull-back), not a press, so it's especially exposed.

**Open question (answer FIRST, diagnostic-first):** is the broken slingshot behavior **vanilla Android** or **mod-caused**? Two specific suspects to rule in/out:
- The mod's **X/Y swap** in `Patches/GameplayButtonPatches.GetState_Postfix` (swaps X/Y during gameplay) could corrupt the slingshot's continuous held-button pull-back. Candidate test/fix: disable the X/Y swap while `Game1.player.CurrentTool is Slingshot`.
- The **tap-to-move clobber** (memory above) likely stops movement while aiming — same class as #25.
- **Right-stick suppression** (`SuppressRightStickInOverworld`, GameplayButtonPatches) — the aim crosshairs may need the right stick; confirm the mod isn't zeroing the stick the slingshot wants.

**Investigate (decompile at `C:\Users\Jeff\Documents\Projects\decompiler\stardew-valley-android`):** `Slingshot.beginUsing` / `tickUpdate` / `endUsing` / `draw` (crosshair); how Android delivers slingshot aim input (likely `VirtualJoypad` weaponControl modes + `tapToMove`); and `Game1.UpdateControlInput`'s slingshot branch. **Test with the mod disabled first** to establish the vanilla baseline before changing anything.

**Scope note:** #25b is the *deliberate exception* to the "all right-stick features ship in v4.0" rule (see TODO.md) — slingshot aim ships in v3.9.x. Don't expand into the broader right-stick cursor work.

## Workspace rules (unchanged)
- Diagnose-first: read the decompile before any fix; state the root cause in plain English before code. One diagnostic build > ten wrong fixes.
- `AccessTools` + null-checks for Android-differing members; wrap patch bodies in try/catch that falls through to vanilla.
- Bump `manifest.json` + commit one change per patch (PATCH 0.0.1). `git add <specific files>`. Local commits only.
- **NEVER push/publish** (GitHub, Nexus) without an explicit user "yes." v3.9.0 publish (README/release/Nexus) is pending and unstarted.
- **NEVER `/sdcard/`** — use `/storage/emulated/0/`.
- Deploy + log-pull are the agent's job: `cd ../SyncdewValley; pwsh -NoProfile -File sync.ps1 deploy|logs`. The user only does in-game tests on the G Cloud (Xbox layout + Switch style; A/B swap active, X/Y swap active in gameplay). Reserve playtests for meaningful feedback; verify engineering correctness from the log yourself.

## Loose ends (not blocking #25b)
- Deploy v3.8.13 to the device when convenient (currently on v3.8.12 — same for normal play).
- The `[MoveCharge]` VerboseLogging-gated diagnostic in `ToolUsePatches` is intentionally kept.
- Other tracked-but-untouched: #73 (seeds-ghost shipped v3.8.5, awaiting device verify + manual Nexus changelog), #74 (drop pickup-blocker — test the 1.2s feel first), #75 (shop list visual scroll).
