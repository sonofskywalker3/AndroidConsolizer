# Museum Donation Menu — Controller Support (#18)

**Date:** 2026-05-29
**Milestone:** v3.8.0 — Console Parity: Quick Wins (pulled forward from v3.9.0)
**Status:** Design approved; pending spec review → implementation plan
**Scope:** Donation placement only. Museum "Rearrange" mode (`OpenRearrangeMenu`) is explicitly **out of scope**.

## Problem

On Android, the museum donation menu requires touch to select an item from the
inventory and place it on the museum grid. A controller cannot do it.

- Public report (Nexus comment, 2026-05-28): *"When I wanted to donate items at
  the museum, I couldn't use my controller to donate."*
- Confirmed on G Cloud.
- Mechanically distinct from #71 (the reward-collection `ItemGrabMenu`, already
  fixed). #18 is the donation *placement* menu (`MuseumMenu`).

## Root cause (decompile-verified)

The complete console-style controller logic **already exists** in the Android
`MuseumMenu`, but every part of it is gated behind `Game1.options.SnappyMenus`,
which is **`false` on Android**.

Walking the Android decompile
(`decompiler/stardew-valley-android/decompiled/StardewValley/`):

1. **`MuseumMenu` ctor** (`StardewValley.Menus/MuseumMenu.cs:83`) only snaps the
   cursor to an inventory slot `if (Game1.options.SnappyMenus)`
   (`populateClickableComponentList` → `currentlySnappedComponent =
   getComponentWithID(0)` → `snapCursorToCurrentSnappedComponent()`).
2. **`receiveKeyPress`** (`MuseumMenu.cs:127`) branches on the same flag:
   - `if (!SnappyMenus)` → D-pad/arrows call `Game1.panScreen(...)` — they scroll
     the camera; they do **not** move a selection or a tile cursor.
   - `else` (SnappyMenus on) → the real console behavior: snap inventory
     selection, and `findMuseumPieceLocationInDirection(...)` to hop a tile-cursor
     across valid museum spots, panning the viewport to follow it.
3. **Selection** happens in `releaseLeftClick` → `inventory.selectItemAt(...)`
   (`MuseumMenu.cs:258`); **placement** in `placeItem(...)` (`MuseumMenu.cs:546`)
   at the current mouse tile.

The A button itself *does* reach the menu: Game1's dispatch
(`StardewValley/Game1.cs:5842–5853`) maps A-press → `receiveLeftClick` and
A-release → `releaseLeftClick` at `getMousePosition()`, gated only on
`!areGamePadControlsImplemented()` — which is `false` for `MuseumMenu`
(`IClickableMenu.cs:165` base returns `false`, not overridden), so it fires.

**But** the D-pad/stick → `receiveKeyPress` dispatch in Game1
(`Game1.cs:5788`, `5820`) is *itself* gated on `options.snappyMenus`. So with
`SnappyMenus == false` and a controller:

- D-pad / left stick → **nothing** (never reaches `receiveKeyPress`; the menu's
  own `!SnappyMenus` panScreen branch is therefore never hit either).
- A → `receiveLeftClick`/`releaseLeftClick` at a **stale, un-moved**
  `getMousePosition()`, with no snap and no visible cursor.

Net: there is no way to land on an inventory slot or aim at a museum tile. Touch
works because a tap delivers a real coordinate to the click handlers.

**One flag is the master switch** for the whole chain: Game1 dispatch, the ctor
snap, and the in-menu grid navigation are all keyed on `Game1.options.SnappyMenus`.

**Corroborating evidence:**
- AC's own code documents the Android default: `Patches/OptionsPagePatches.cs:18`
  and `:558`, and `Patches/LetterViewerMenuPatches.cs:118` all note
  "snappyMenus = false on Android".
- User memory `ayaneo-runtime-quirks`: `snappyMenus == True` on Ayaneo. This
  predicts donation **already works** with a controller on Ayaneo and fails only
  where `SnappyMenus == false` (G Cloud) — a clean cross-check for the diagnosis.

## Approach (chosen)

**Flip the snap flag, scoped to the donation menu.** Turn `SnappyMenus` on only
while the donation menu is open; restore the prior value on close. Stardew's own
complete console code then performs inventory snap, D-pad tile navigation, and
placement. We write **zero** navigation logic.

Two approaches considered and rejected (kept as documented fallbacks):
- **Un-gate this menu only** (Harmony-replicate the snappy branches without
  touching the global flag) — more code; this is the **fallback** if flipping the
  global flag shows a side effect we cannot contain on device.
- **Full custom virtual cursor** (CarpenterMenu / JunimoNote pattern) — most code,
  most fragile; only if both above fail.

## Design

### Flag lifecycle (donation-only by construction)

- **Prefix on `LibraryMuseum.OpenDonationMenu`**: if the feature toggle is on,
  save `Game1.options.SnappyMenus` into a field, set it `true`, set
  `_weForcedSnappy = true`. `OpenDonationMenu` is the donate path **only**
  (`OpenRearrangeMenu` is a separate method and is left alone), so this is
  inherently scoped to donation. Setting the flag here guarantees it is already
  `true` when the `MuseumMenu` constructor runs its `if (SnappyMenus)` snap-setup
  block.
- **Postfix on `MuseumMenu.cleanupBeforeExit`**: if `_weForcedSnappy`, restore the
  saved value and clear `_weForcedSnappy`. `cleanupBeforeExit` is guaranteed to run
  when the menu closes.
- **Safety net**: also restore in an `OnDonationMenuClosed` postfix (the menu's
  `exitFunction`) in case of an abnormal close, guarded by `_weForcedSnappy` so the
  restore is idempotent.

Once the flag is on, the game's own `receiveKeyPress` (snappy branch with
`findMuseumPieceLocationInDirection`), `releaseLeftClick` → `inventory.selectItemAt`,
and `placeItem` do all the work.

### Configuration

- New `ModConfig` option **`EnableMuseumDonationController`**, default **`true`**.
- Registered in `ModEntry`'s GMCM setup alongside existing toggles.
- When `false`: the `OpenDonationMenu` prefix no-ops (no flag flip) → vanilla touch
  behavior preserved.

### Reflection / platform-difference notes

- Use Harmony patches via `AccessTools.Method` for `LibraryMuseum.OpenDonationMenu`,
  `MuseumMenu.cleanupBeforeExit`, and `LibraryMuseum.OnDonationMenuClosed`.
- No platform-differing fields are read directly; the fix only toggles a public
  `Game1.options.SnappyMenus` bool, so the PC-DLL-vs-Android-runtime field hazard
  does not apply here.

### Files touched (one feature, one commit)

- `Patches/MuseumMenuPatches.cs` (new)
- `ModConfig.cs` (add toggle)
- `ModEntry.cs` (GMCM registration; wire config into the patch)
- `manifest.json` (0.0.1 version bump)

## Verification plan (diagnostic-first)

1. **First build carries transient logging** (the established "debug aid, remove
   before shipping" pattern): log `SnappyMenus` value at `OpenDonationMenu` entry,
   confirm the flip, and log that `MuseumMenu.receiveKeyPress` fires on D-pad. This
   confirms the root cause on G Cloud in the same build as the fix — the fix is
   self-confirming, so no separate diagnostic round is needed.
2. **G Cloud device test** (SnappyMenus=false → the failing case): select an
   inventory item, D-pad across museum tiles, place with A. This is a *meaningful*
   playtest (navigation feel + placement correctness), appropriate to request from
   the user.
3. **Ayaneo cross-check** (SnappyMenus=true natively): confirms donation already
   worked there and that the fix does not regress it.
4. **Strip transient logging** in a follow-up 0.0.1 build before shipping.

### Landmine watch

- Per user memory `geodemenu-releaseleftclick-harmony-crash`: if the game dies on
  museum open/interact with **nothing in the SMAPI log**, suspect a native
  SIGSEGV from patching a release/cross-platform override — pull
  `adb -s <dev> logcat -b crash -d` first. This design patches `OpenDonationMenu` /
  `cleanupBeforeExit` / `OnDonationMenuClosed` (not an input override like
  `releaseLeftClick`), specifically to stay clear of that hazard.

## Risk & fallback

The one real risk is a side effect from flipping a global flag mid-session — most
plausibly an interaction with AC's always-on input patches (e.g., the A/B-swap
`GetState` patches) while the flag is `true`. Mitigations: the HUD is already
hidden during the menu (`Game1.displayHUD = false`), no other menu is open
simultaneously, and the flip is strictly bracketed by the menu's open/close.

If device testing reveals a side effect that cannot be cleanly contained, fall back
to **un-gate this menu only** (Harmony-replicate the snappy branches; no global flag
change). Only escalate to the full custom virtual cursor if both fail.

## Success criteria

- With a controller on G Cloud (SnappyMenus=false), a player can: open the donation
  menu, snap-select a donatable item from the inventory, navigate the museum grid
  with the D-pad, and place the item with A — no touch required.
- `SnappyMenus` is restored to its prior value on every close path.
- Rearrange mode is unchanged.
- Feature is toggleable in GMCM (default on).
- No regression on Ayaneo (where it natively worked).
