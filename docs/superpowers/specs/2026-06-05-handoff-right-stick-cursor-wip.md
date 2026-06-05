# Handoff — v4.0 The Right Stick Update: cursor CORE done (dev v3.9.10), follow-ups remain

**Date:** 2026-06-05
**Tip:** **v3.9.10** — committed to `master`, NOT released. These are dev-iteration patches (`v3.9.1`→`v3.9.10`) toward the **v4.0.0** milestone. The version bumps to `4.0.0` only when the Right Stick Update is complete and the user says "release."
**Device:** G Cloud, loaded with v3.9.10, VerboseLogging ON. Deploy/logs via `cd ../SyncdewValley; pwsh -NoProfile -File sync.ps1 deploy|logs` (SyncdewValley lives at `Stardee Valoo/SyncdewValley`, NOT `Projects/SyncdewValley`).

> **Rules (current):** pushing commits is fine anytime; only **releases** (`gh release create`, Nexus) need an explicit "yes." Work on `master`. One change per commit, PATCH `0.0.1` bump in `manifest.json` BEFORE building; `git add <specific files>` (never `-A`). Build: `dotnet build AndroidConsolizer.csproj -c Release`. NEVER `/sdcard/` → `/storage/emulated/0/`. Deploy + log-pull are the agent's job; reserve playtests for meaningful feedback; verify engineering correctness from the log.

---

## Read first
- This handoff.
- `docs/superpowers/specs/2026-06-04-right-stick-update-v4-design.md` (the design spec) and `docs/superpowers/plans/2026-06-04-right-stick-update-v4.md` (the plan — note it was adapted heavily once device tests revealed the real gaps).
- `Patches/RightStickCursorPatches.cs` (the whole feature lives here).
- Memory `switch-rightstick-tools-hit-facing-not-cursor` (console behavior, user-confirmed) and `android-lastcursormotionwasmouse-true-on-controller`.
- **Decompiles:** Android at `…/decompiler/stardew-valley-android/decompiled/StardewValley/StardewValley/`; **PC 1.6 now cloned** at `…/decompiler/stardew-valley-pc/Stardew Valley/StardewValley/` (use it to confirm console-intended behavior vs the Android mobile overrides — it's how we proved the tool-targeting fixes).

---

## What got done this session — the overworld right-stick cursor (ALL device-verified on G Cloud)

The console right-stick cursor is **engine-native** (NOT the #18 menu trap): `Game1.UpdateControlInput:13303-13334` moves the cursor from `ThumbSticks.Right` (gated on `options.gamepadControls`, which is **True** at runtime — Phase 0 confirmed), and `drawMouseCursor` fades it via `timerUntilMouseFade=4000`. AC was *starving* it by zeroing the right stick (`SuppressRightStickInOverworld`). Everything below is in `Patches/RightStickCursorPatches.cs` + small wiring in `GameplayButtonPatches.cs`/`ModEntry.cs`/`ModConfig.cs`.

1. **Config rename (v3.9.3):** `SuppressRightStickInOverworld` → **`EnableRightStickCursor`** (default **on**, semantics flipped). `GameplayButtonPatches` now zeros the overworld right stick only when the cursor is OFF (or a slingshot is equipped — v3.9.4 carve-out, #25b: aim = left stick). Added `RawRightStickX` caching (only Y existed).
2. **Cursor visibility (v3.9.6):** the Android engine computes cursor state but renders **no overworld cursor sprite** (mobile strips it). We self-draw it: `DrawCursor` via `Display.RenderedHud`, `Game1.mouseCursors` pointer at `getMouseX/Y`, gated on `timerUntilMouseFade > 0` → **auto-hides ~4s** after the last stick motion (engine counts the timer down). 
3. **Interaction follows the cursor (v3.9.7):** prefix on `Game1.pressActionButton` sets `lastCursorMotionWasMouse = true` while the cursor is active. Needed because the controller A-press handler nulls that flag (`13449`) before the tile gate (`11971`) reads it; setting it in `UpdateTicked` is too late. Now: cursor onto a chest/NPC while facing away + press action → spins you and opens it (Switch parity). Reverts to facing tile once the cursor fades.
4. **Tool swings + held-object placement follow the cursor (v3.9.8):** postfix on `Character.GetLocationNextToWhereYoureFacing` returns the cursor tile while active. Android's `pressUseToolButton` overrides the target to the facing tile whenever a controller button is held (`12317-12327`); PC has **no such override** (confirmed in the PC decompile — `pressUseToolButton` just uses the cursor). `GetLocationNextToWhereYoureFacing` has only 2 callers (both the tool path), so the postfix is surgical. Method is Android-only → resolved by **string** via `AccessTools` (absent on the PC reference DLL).
5. **Diagonal tool hits (v3.9.9):** prefix on `Character.GetToolLocation(bool)` drops the `isAnyGamePadButtonBeingHeld()` term while the cursor is active so the hit resolves from `lastClick` (the cursor tile) instead of snapping to the facing cardinal tile — diagonal cursor tiles are now hittable. (You still turn to a cardinal facing, which is correct — Switch can't face diagonal — but the *hit* lands on the diagonal tile.)
6. **No center-snap (v3.9.10):** postfix on `Game1.setMousePositionRaw` re-asserts `lastCursorMotionWasMouse = true` while the right stick is driving the cursor, so the engine's post-fade recenter (`13328-13331`) never fires — the cursor resumes where it was instead of jumping to mid-screen.

Also still present from earlier in the session: `EnforceTick()` (sets the flag each moving tick from `OnUpdateTicked`) and the `[RStickDiag]` VerboseLogging-gated diagnostic line. `EnforceTick` is now largely redundant with the `setMousePositionRaw` postfix — **a candidate cleanup** (verify nothing regresses, then remove). The `[RStickDiag]` Info line should be **demoted/removed before the v4.0.0 release**.

**Device-confirmed working:** cursor visible + moving, auto-hides 4s, interaction follows cursor, tools hit cursor tile (cardinal AND diagonal), furniture (#62) + craftable/seed placement follow the cursor, no center-reset, slingshot still left-stick aim. **#12 core + #62 are functionally complete.**

---

## What's LEFT (before the v4.0.0 release)

1. **#79 — Contextual cursor (NEW item, user-requested).** On console the cursor sprite changes by what's under it: a **hand/finger** over interactables (chests, mailbox, shipping bin), a **speech-bubble** over NPCs you can talk to, etc. Our self-draw (`DrawCursor`) currently always draws the default pointer (`Game1.mouseCursor`, which the engine resets to 0). The engine computes the contextual index in `drawMouseCursor` (`Game1.cs:15647-15649`: `cursor_talk`/`cursor_look`/`cursor_grab` from `isActionAtCurrentCursorTile`/`isSpeechAtCurrentCursorTile`/`isInspectionAtCurrentCursorTile`) but resets `mouseCursor` to `cursor_default` (15688) before our HUD draw runs. Plan: capture the contextual index before it's reset (small postfix/field-read in `drawMouseCursor`, or replicate the hover hit-test) and feed it to `DrawCursor`. Use the PC decompile for the clean source of the hover→cursor mapping.
2. **Stuck tool-hit box.** The #76 tool-hit box (`Farmer.cs:6364-6368`) always targets `getMousePosition()` and never reverts to the facing tile when the cursor fades — so after the cursor auto-hides and you walk a new direction, the box points the OLD way while you hit the way you face. Fix: when `!Game1.wasMouseVisibleThisFrame`, target the box at `GetToolLocation(ignoreClick: true)` (facing tile). Likely needs patching the box draw in `Farmer.draw` (transpiler or a draw override) — check the PC code first.
3. **Settings persistence (#77 follow-up) — user-reported this session.** Zoom + the tool-hit-box option (native game options injected by #77, stored in the game's StartupPreferences, NOT AC config) **reset on a cold restart** — this session was the first full restart since 3.9.0 shipped, surfacing the gap. Likely the zoom render (PinchZoom pipeline) isn't re-applied from saved prefs on load, and/or the tool-hit checkboxes aren't reloaded. Fix: re-apply both from saved prefs on `SaveLoaded`/`GameLaunched`. NOT caused by the v4.0 work; ModConfig stores neither value. Bundle with the stuck-box fix if convenient (both touch tool-hit).
4. **Pre-release hygiene:** demote/remove the `[RStickDiag]` Info diagnostic; consider removing the now-redundant `EnforceTick`; move #12/#62 fully to `DONE.md`; write the README ≡ Nexus "What's New" for 4.0.0.

**Out of v4.0 scope (decided with the user):** menu right-stick scroll, R3 chat / hold-emote, minigame right-stick→D-pad. Don't build these.

---

## Console behavior reference (user-confirmed on real Switch, 2026-06-05)
- Cursor drives **interaction + placement** (chests, NPCs, gifts, furniture, tool target) — moving the cursor onto a chest while facing away + action **spins you and opens it**.
- **Tool swings hit the SELECTED (cursor) tile**, including **diagonal** tiles — NOT the facing tile (this corrected an earlier wrong assumption; see memory). You turn to a cardinal facing but the hit lands on the cursor tile.
- The cursor sprite is **contextual** (hand/finger over interactables, speech bubble over people) → that's #79.

---

## Coding gotchas reinforced this session
- **Android-only members must be resolved by STRING via `AccessTools`**, never `nameof`/direct ref — they're absent on the PC reference DLL and won't compile (`GetLocationNextToWhereYoureFacing` bit this). `GetToolLocation`, `pressActionButton`, `setMousePositionRaw`, `lastCursorMotionWasMouse`, `wasMouseVisibleThisFrame` DO exist on the PC DLL (compiled directly).
- **The PC 1.6 decompile is now cloned** — use it to tell "console-intended" from "Android mobile override." The mobile overrides that block cursor targeting (`pressUseToolButton:12317`, the `isAnyGamePadButtonBeingHeld` term) are NOT in the PC source.
- **`lastCursorMotionWasMouse` is the master flag** for "cursor is the pointer" — but multiple engine sites null it within a single tick (`setMousePositionRaw:5536`, the A/X-press handlers `13449/13455`). Set it true at the **read site** (a prefix on the consuming method), not earlier.
- Patching `pressActionButton`, `GetToolLocation`, `GetLocationNextToWhereYoureFacing`, `setMousePositionRaw` with simple prefixes/postfixes is **safe** on Android (only `GeodeMenu.releaseLeftClick` is the SIGSEGV landmine).
