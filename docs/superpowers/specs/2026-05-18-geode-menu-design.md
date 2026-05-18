# Design: Geode Breaking Menu — Visual Feedback (#19)

**Date:** 2026-05-18
**Milestone:** v3.8.0 — Console Parity: Quick Wins
**TODO item:** #19 — Geode Breaking Menu — Visual Feedback
**Target version:** v3.7.21
**Test device:** Ayaneo Pocket Air Mini (primary)

---

## Problem

`TODO.md` #19: "Partially works but no visual feedback. Geode highlighted
in inventory but does NOT visually move to anvil. Pushing up invisibly
moves to anvil area, A cracks it. Functional but unintuitive."

The TODO text is a slightly imprecise description of vanilla Android
behaviour. The actual decompile (`GeodeMenu.cs` lines 559–654) shows:

- **D-pad / left stick** moves `_selectedItemIndex` through valid
  geodes, skipping non-geodes via `inventory.highlightMethod =
  highlightGeodes`.
- **X** sets `heldItem` to the selected slot and calls
  `OnPlaceGeodeOnAnvil()`, which immediately invokes `startGeodeCrack()`.
  `geodeSpot.item` is set inside `startGeodeCrack` and remains for the
  ~2.7 s `geodeAnimationTimer` window — geode is visible on the anvil
  only *during* the crack animation.
- **A** toggles a tooltip (`_showTooltip = !_showTooltip`). Has no
  console analogue.
- **B** closes the menu.

There is no separate "placed on anvil, not yet cracked" state. One press
of X = atomic place + crack. AndroidConsolizer has no patch on this menu
today (`grep -r GeodeMenu Patches/` ⇒ no matches).

Console SDV (Switch) shows a clear two-press interaction: A places the
selected geode on the anvil (geode visibly sits there), second A starts
the hammer animation. The Android port collapsed it into a single press
on the wrong physical button.

## Decision (made during brainstorm, no clarifying questions asked)

- **Two-press A.** A while anvil empty = place. A while anvil occupied =
  crack. Mirror X behaviour to A (both place / crack), so users with
  muscle memory for either button get the right semantics.
- **Tooltip toggle on A is removed.** Not a console feature; the menu's
  inventory already shows the item normally.
- **Teleport, not animate.** Geode appears on the anvil instantly on the
  first A press. A "hop" animation would extend scope past v3.8.0
  "Quick Wins."
- **Sticky anvil state.** Navigating to a different inventory slot while
  the anvil is occupied does NOT swap the geode. Press B first to take
  it back, then A on the new selection. Prevents accidental swaps.
- **B cancels placement before closing.** B with anvil occupied returns
  the geode to inventory (no consume, no close). B with anvil empty
  closes the menu (vanilla).
- **All input blocked during the crack animation.** Same as vanilla —
  `geodeAnimationTimer > 0` short-circuits A / X / B path.
- **GMCM toggle:** `EnableConsoleGeodeMenu`, default `true`. Off
  ⇒ patch passes through to vanilla. Matches the project's "every
  feature toggleable" rule.

## Approach (chosen: A)

**A. Harmony prefix on `GeodeMenu.receiveGamePadButton(Buttons)`. Maintain
our own `_geodeOnAnvilIndex` state. Pass nav / unrelated buttons through.
(CHOSEN.)**

Pros: minimal surface area. Vanilla nav (`DPad*`, thumbstick) keeps
working with zero risk. Only the three buttons that change behaviour (A,
X, B) need handling. Pattern matches #44 (`highlightMethod` swap) and
#46 (bundle greyout) — small, surgical, fix-the-data-don't-rewrite-it.

Cons: prefix must reason about a four-cell state matrix (button × anvil
state). Mitigated by extracting two small helpers (`TryPlaceOnAnvil`,
`CrackPlacedGeode`).

**B. Prefix-return-false on `receiveGamePadButton`; reimplement entire
switch ourselves.** (REJECTED.) Bigger blast radius — we'd have to
replicate the two `highlightMethod`-aware navigation loops verbatim,
each ~15 lines. No behavioural improvement to nav. More surface for
Android-port-vs-PC differences to bite us (`Action`/`moving` carpenter
lesson).

**C. Keep one-press X, add a separate "place" key.** (REJECTED.) The
TODO explicitly asks for two-press A. A separate place key fragments
the UX further and doesn't match console.

## Architecture

**New file:** `Patches/GeodeMenuPatches.cs`.

**State (static fields in the patch class):**

```csharp
private static int _geodeOnAnvilIndex = -1;  // inventory slot of the
                                              // placed-but-not-cracked
                                              // geode. -1 = anvil empty.
```

State is reset on:
- `startGeodeCrack` Postfix (geode now consumed; animation owns the visual).
- `update` Postfix when `geodeAnimationTimer <= 0 && geodeSpot.item == null
  && _geodeOnAnvilIndex >= 0` (defensive — covers async paths like the
  golden coconut mutex callback or `emergencyShutDown`).
- `ModEntry.OnMenuChanged` when the new menu is not `GeodeMenu` — if the
  geode was on the anvil but never cracked, we leave the inventory item
  untouched (we never consumed it; only `geodeSpot.item = slot.getOne()`,
  a copy). Just zero `_geodeOnAnvilIndex`.

**Patches:**

| Patch | Method | Purpose |
|-------|--------|---------|
| Prefix | `GeodeMenu.receiveGamePadButton(Buttons)` | Two-press A/X, B-cancels-placement. |
| Postfix | `GeodeMenu.startGeodeCrack()` | Reset `_geodeOnAnvilIndex`. |
| Postfix | `GeodeMenu.update(GameTime)` | Defensive reset when anvil visually empty. |
| Hook | `ModEntry.OnMenuChanged` | Reset on menu close. |

**Button table (prefix logic):**

| Button | Anvil empty (`_geodeOnAnvilIndex == -1`) | Anvil occupied | Crack animation running OR `waitingForServerResponse` |
|---|---|---|---|
| **A** | `TryPlaceOnAnvil()`; return false | `CrackPlacedGeode()`; return false | Return false (block) |
| **X** | Same as A | Same as A | Return false |
| **B** | Pass to vanilla (close) | Cancel placement: clear `geodeSpot.item`, `_geodeOnAnvilIndex = -1`, play `"smallSelect"`; return false | Return false |
| anything else | Pass to vanilla | Pass to vanilla | Pass to vanilla |

The third column merges two animation-busy states: vanilla's
`geodeAnimationTimer > 0` (Clint mid-hammer) and
`waitingForServerResponse` (golden coconut mutex pending). Vanilla's
`receiveLeftClick` bails on `waitingForServerResponse` (line 204); we
match that for gamepad input.

When `Config.EnableConsoleGeodeMenu == false`, the prefix returns `true`
immediately — vanilla handles everything.

**`TryPlaceOnAnvil` logic:**

1. Bail if `_selectedItemIndex < 0` or selected slot is null / non-geode.
2. Bail if `Game1.player.Money < 25` — set `wiggleWordsTimer = 500` and
   `Game1.dayTimeMoneyBox.moneyShakeTimer = 1000` (mirror vanilla
   `OnPlaceGeodeOnAnvil` lines 690–693 — no sound, just the visual
   shake).
3. Bail if inventory is full and selected stack > 1 — set
   `descriptionText = fullText`, `wiggleWordsTimer = 500`,
   `alertTimer = 1500` (mirror vanilla).
4. Set `geodeSpot.item = inventory.actualInventory[_selectedItemIndex].getOne()`.
5. `_geodeOnAnvilIndex = _selectedItemIndex`.
6. Play `"stoneStep"` (vanilla's place sound; matches what
   `startGeodeCrack` would play).

**`CrackPlacedGeode` logic:**

1. Set `heldItem = inventory.actualInventory[_geodeOnAnvilIndex]` (don't
   `ConsumeStack` here — vanilla's `startGeodeCrack` does that on its own
   from `heldItem`).
2. Clear `geodeSpot.item = null` *before* invoking
   `OnPlaceGeodeOnAnvil` — vanilla's `startGeodeCrack` re-sets it on
   line 180 (`geodeSpot.item = heldItem.getOne()`).
3. Call `OnPlaceGeodeOnAnvil` via reflection (`AccessTools.Method`).
   `OnPlaceGeodeOnAnvil` does the full validity check chain (money,
   space, golden-coconut mutex path) and either calls `startGeodeCrack`
   directly or routes through the mutex callback.
4. Our `startGeodeCrack` Postfix resets `_geodeOnAnvilIndex = -1`.

Going through `OnPlaceGeodeOnAnvil` (rather than `startGeodeCrack`
directly) preserves the golden coconut path automatically — that path
already sets `geodeTreasureOverride` and resolves asynchronously.

**Display:** No new draw code. Vanilla `GeodeMenu.draw` already renders
`geodeSpot.item` when non-null (line 514). Vanilla's
`highlightMethod = highlightGeodes` already greys out non-geodes in the
inventory.

**No cursor sprite.** Per `feedback_console_ux_no_cursor` —
`inventory.currentlySelectedItem` (already driven by vanilla's
`_selectedItemIndex` ↔ post-switch fall-through at line 646) is the
selection indicator on the inventory side, and `geodeSpot.item != null`
(rendered as the geode sprite) is the indicator on the anvil side.

## Edge cases

- **Selected slot becomes empty after placement** (e.g. player held a
  stack of one). `_geodeOnAnvilIndex` still points to the slot index;
  `CrackPlacedGeode` reads `inventory.actualInventory[index]` which is
  now `null`. Guard: bail out of `CrackPlacedGeode` if the slot has
  become null or non-geode; reset `_geodeOnAnvilIndex` and clear
  `geodeSpot.item` (treat as cancellation). Should be unreachable in
  normal play (we don't consume on place) but defensive.
- **Player runs out of money between place and crack.** Vanilla
  `OnPlaceGeodeOnAnvil` handles this — runs the money-shake animation
  and doesn't crack. Geode stays on anvil. User can B-cancel or earn
  25g and try again.
- **`emergencyShutDown` triggers.** Vanilla puts `heldItem` back in
  inventory. We never set `heldItem`, so nothing to clean up there. The
  menu close hook resets `_geodeOnAnvilIndex`.
- **Golden coconut.** Goes through `OnPlaceGeodeOnAnvil` →
  `goldenCoconutMutex.RequestLock` async path. `waitingForServerResponse`
  is `true` during the wait — our prefix should also bail (return false,
  no-op) while `waitingForServerResponse == true`, mirroring the
  vanilla check on line 204.
- **`heldItem` set by some other code path before our `CrackPlacedGeode`
  runs.** Shouldn't happen on Android (we are the only A/X handler now)
  but guard: bail out of `TryPlaceOnAnvil` if `heldItem != null`
  (something is mid-flight; let vanilla resolve).

## Diagnostic-first carve-out

If the first device test reveals the second-A path doesn't reach
`startGeodeCrack` — e.g. `OnPlaceGeodeOnAnvil` resolves to a different
Android-port field name, or the reflection call returns null — we ship a
diagnostic build first (one Info-level log line in `TryPlaceOnAnvil` and
`CrackPlacedGeode` reporting the state-machine transitions) before
iterating on a fix. Per project rule (`CLAUDE.md` "Diagnostic-First
Development"). The reflection-resolved member of concern is
`OnPlaceGeodeOnAnvil` itself — it's private in the decompile, and
`MenuWithInventory.OnTapCloseButton` exists on Android but not PC, so
related private members may also be name-shifted.

## Files

- **New:** `Patches/GeodeMenuPatches.cs`.
- **Modified:** `ModEntry.cs` — register the new patch, hook
  `OnMenuChanged` reset path, GMCM entry for the toggle.
- **Modified:** `ModConfig.cs` — add `EnableConsoleGeodeMenu` (bool,
  default `true`).
- **Modified:** `manifest.json` — version bump to `3.7.21`.

## Test plan

1. Build, deploy to Ayaneo via `../SyncdewValley/sync.ps1 deploy`.
2. Pull logs after each scenario via `sync.ps1 logs`.
3. Scenarios:
   - Open GeodeMenu at Clint with a stack of regular geodes. Navigate to
     a geode. Press A → geode appears on anvil. Press A → crack
     animation plays, treasure to inventory.
   - Two-press A on a stack of frozen geodes (different sprite — confirms
     the sprite shown is the right one).
   - A on inventory slot with selected non-geode (shouldn't happen due to
     `highlightGeodes` nav, but verify guard).
   - Place geode, navigate to different slot, press A → cracks the
     ON-ANVIL geode (not the new selection).
   - Place geode, press B → geode returns to inventory, menu stays open.
     Press B again → menu closes.
   - Place geode, money drops below 25g via cheat / event → next A
     shows money-shake, no crack. B returns geode.
   - Inventory full, place + crack final geode whose treasure won't fit
     → vanilla `fullText` warning, no crack (vanilla path).
   - Golden coconut: place + crack. Async mutex resolves, treasure is
     the coconut (handled by vanilla `OnPlaceGeodeOnAnvil`).
   - GMCM: turn `EnableConsoleGeodeMenu` off, re-test — vanilla one-press
     X behaviour returns, A toggles tooltip again.
   - First-press-after-open: open menu, press A before any nav. Should
     do nothing (matches vanilla X-guard at line 633). User must D-pad
     once to select a geode. If this turns out to be a friction point in
     UAT, fix in a follow-up patch by auto-snapping `_selectedItemIndex`
     to the first valid geode on menu open — out of scope for this one.
4. Verify in SMAPI log: no exceptions, no Harmony patch failures.

## Out of scope

- Hop / slide animation on placement (deferred — v3.8.0 is "Quick Wins").
- Auto-place on menu open (always require explicit A first — matches
  Switch).
- Cracking multiple geodes per A-hold (rapid-fire mode). Not console
  behaviour; users with stacks press A twice per geode, same as Switch.
- Right-stick-controlled "anvil cursor" (right stick is gated to v4.0).
- Tooltip toggle (vanilla A behaviour) — removed. Item info is already
  visible via standard inventory rendering; no users have requested the
  toggle.

## References

- Decompile: `C:\Users\Jeff\Documents\Projects\decompiler\stardew-valley-android\decompiled\StardewValley\StardewValley.Menus\GeodeMenu.cs`.
- Similar patterns:
  - `DONE.md` #44 — `highlightMethod` swap on sell tab.
  - `DONE.md` #46 — bundle donation greyout, also a
    `highlightMethod` swap pattern.
  - `DONE.md` "9a Donation Page A-press" — same "two-state inventory A
    handler that uses inventory index + custom validity check" shape,
    but more invasive (full cursor draw). We can do less here because
    vanilla nav already works.
- Memory: `feedback_console_ux_no_cursor` — no cursor sprite,
  snap is the indicator.
- Memory: "Android vs PC DLL Differences" — `OnPlaceGeodeOnAnvil`
  reached via reflection (`AccessTools.Method`).
