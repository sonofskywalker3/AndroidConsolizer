# #25b Slingshot Combat — Console-Parity Aim (design)

**Date:** 2026-06-04
**Item:** TODO #25b (v3.9.x — the deliberate exception to the "right-stick ships in v4.0" rule)
**Status:** ✅ v1 console parity SHIPPED v3.8.14→v3.8.16 (device-verified G Cloud 2026-06-04, see `DONE.md` "#25b Slingshot Combat"). Dual-stick stretch below is still open.

## Problem

On Android, equipping a slingshot **stops movement**, and the slingshot **doesn't aim/fire like console**. We want full console parity, and (stretch) a better-than-console dual-stick mode.

## Confirmed target UX (console-exact)

Mirrors vanilla `Slingshot.beginUsing` / `tickUpdate` / `onRelease` + `Game1.UpdateControlInput`:

- Slingshot equipped, tool button **not** pressed → walk freely with the **left stick**.
- **Hold the tool button** → draw back / aim. Feet freeze; the **left stick** swings the aim crosshair (the aim sprite renders). This is `usingSlingshot`/`UsingTool` state.
- **Release the tool button** → fire a projectile toward the crosshair (`onRelease` → `DoFunction` → `PerformFire`). `doFinish` sets `controllerSlingshotSafeTime = 0.2` so the aim stick doesn't immediately walk the player.

**Key decompile fact:** console slingshot aim is driven by the **LEFT** thumbstick (`Slingshot.updateAimPos` reads `Game1.oldPadState.ThumbSticks.Left` when `gamepadControls && !lastCursorMotionWasMouse`, falling back to facing direction when the stick is centered). It never used the right stick — which is *why* #25b is correctly the v3.9 exception and not part of the v4.0 right-stick arc.

## Why it likely breaks (hypotheses — to be confirmed by Phase 1)

The slingshot mixes a **held tool button + a continuous held aim state** — the same class of problem as #25 (tool charging while moving). Suspects, in priority order:

1. **Android tap-to-move teardown** (`Game1._mobileUpdateControlInput`, memory `android-taptomove-clobbers-tool-on-move`) — the same subsystem that clobbered the #25 charge could be tearing down `usingSlingshot`/`canReleaseTool`/`UsingTool`, which would both stop aiming and (via the safe-time / state churn) interfere with movement.
2. **The mod's X/Y swap** (`GameplayButtonPatches.GetState_Postfix`) — Android's use-tool button is `Buttons.X` (`Game1.cs:13640`). The swap is applied per-tick and should be consistent across a hold, but it must be ruled out as corrupting the continuous pull-back.
3. **`SuppressRightStickInOverworld`** — almost certainly a **red herring** for v1, because console aim is the *left* stick. Confirm it isn't implicated. (It *will* matter for the dual-stick stretch below.)

Nothing in the existing AC code gates gameplay movement on a slingshot — `SlingshotPatches` only does in-menu ammo management — so the movement stop is either vanilla Android behaviour with a physical controller, or an emergent interaction with the swap/tap-to-move. Phase 1 resolves which.

## Phase 1 — Diagnostic (no behaviour change)

A `VerboseLogging`-gated, once-per-tick line, active only while `Game1.player.CurrentTool is Slingshot`, capturing the full state so we can pin where the chain breaks **without a guess**:

- `options.weaponControl`
- `usingSlingshot`, `UsingTool`, `canReleaseTool`, `CanMove`, `movementDirections.Count`
- `toolPower`, `controllerSlingshotSafeTime`
- raw left stick vs game-facing left stick
- game-facing tool button (`Buttons.X`) held state, and pre/post X/Y-swap X/Y states
- `GetSlingshotChargeTime()`, `GetBackArmDistance()`, `aimPos`

User equips the slingshot, tries to walk, then hold-aim-releases a couple of times. Agent pulls the log and reads where it breaks. Diagnostic stripped once the fix lands.

## Phase 2 — Targeted fix (branched on Phase 1)

- **If mod-caused** (swap corrupts the held pull-back, or a suppression zeroes the aim stick): stop interfering — gate the offending patch off while `CurrentTool is Slingshot`. Minimal, "fix-the-data" style.
- **If vanilla Android** (the touch-oriented control path doesn't drive the slingshot from a physical controller): re-assert the slingshot's held-aim state each tick the tool button is held — same shape as #25 Stage 2 (postfix the control path; let the engine's own `tickUpdate` aim and fire). Both are "held button + continuous state vs Android tap-to-move."

**Gating:** new GMCM toggle `EnableSlingshotAim` (default true), scoped strictly to slingshot use. No effect on any other tool/weapon.

## Making it even better — dual-stick slingshot (SPUN OUT to a separate mod — NOT AC)

> **Re-scoped 2026-06-04:** per the user, AndroidConsolizer stays **console-parity only**; better-than-console features become their own standalone mods rather than being rolled into AC (now feasible because publishing/updating multiple mods is automated). This design is preserved here as the **seed for a future standalone slingshot mod**, not an AC feature. Do NOT implement it in AC.

Better-than-console twin-stick aiming:

- Slingshot equipped: **left stick walks** (and still drives the console hold-to-aim when the tool button is held).
- **Right stick** simultaneously **aims the slingshot and fires** in that direction — twin-stick-shooter style, no tool-button hold required. Walk and shoot at once.

Design notes for this stretch:
- The mod currently **zeroes the right stick in the overworld** via `SuppressRightStickInOverworld` (to stop cursor drift). Dual-stick aim needs a **slingshot-specific carve-out** there so the right stick reaches the aim path.
- Feed the right-stick vector into the slingshot's aim (analogous to `updateAimPos`'s left-stick read) and trigger `PerformFire` on a fire cadence (mirror `CanAutoFire`/`GetAutoFireRate`, or a release/charge model) without entering the feet-freezing `usingSlingshot` hold.
- Keep it a **separate toggle** (e.g. `EnableSlingshotDualStick`, default off) and a **separate patch/commit** from the v1 console-parity fix — ship v1 first, layer dual-stick on top.

## Scope boundaries

- v1 = the slingshot draw/aim/fire loop only (console parity, left-stick aim).
- Not melee weapons. Not the broader v4.0 right-stick cursor.
- Ammo management (`SlingshotPatches`) is untouched.
- Dual-stick is NOT an AC feature — it's a separate-mod seed (see the re-scoped section above).

## Verification

- Engineering correctness from the device log (movement restored, `usingSlingshot`/`UsingTool` toggle correctly on hold/release, `toolPower`/charge ramps, projectile fires) — agent-verifiable, no playtest needed for the wiring.
- One meaningful playtest for feel: does aiming track the stick, does release fire where aimed, does walking feel right when not aiming.
