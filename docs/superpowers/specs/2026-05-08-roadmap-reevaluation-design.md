# Roadmap Re-evaluation (Post-3.6.0)

**Date:** 2026-05-08
**Status:** Approved structure — informs TODO.md reorganization
**Supersedes:** GSD-era M3 / M4 / M5 / M6 milestone framing in `STATUS.md`

---

## Why this exists

After v3.6.0 shipped (a "Bug Fix Release" that drained the responsive Nexus queue), the existing milestone structure was inherited from the older GSD planning scaffolding and had not been re-evaluated against current reality. The original ordering — M3 cursor → M4 dialogue polish → M5 combat → M6 advanced — assumed:

1. Right Joystick Cursor Mode (#12) was the load-bearing dependency for museum (#18) and geode (#19).
2. "Console parity baseline" was still the right north star, with cursor mode as the next big gap.
3. Items needed to be grouped by *theme* (dialogue, combat, advanced).

Two of those assumptions broke under examination:

- **#12 is NOT load-bearing for museum or geode.** Confirmed by play pattern — Switch handles museum donations and geode breaking with snap-based navigation, no free cursor required. Bundling them with #12 was a planning error, not a technical constraint.
- **The author's revealed working pattern is "quick-win 0.0.1 patches at high cadence,"** not multi-week feature arcs. v3.5.x shipped 39 sub-versions in roughly six weeks, almost all driven by Nexus reports. v3.6.0 chose bug-fix polish over the planned cursor work and that turned out to be the right call. The cursor MVP — which bundles right-stick→cursor + zoom + acceleration + auto-hide + snap-nav interaction — is the structural opposite of a quick win.

The remaining "console parity" surface is also smaller than the milestone count suggests. DONE.md covers chest, shop, inventory, shipping, toolbar, fishing, slingshot, CC bundles, all GameMenu tabs, CarpenterMenu (all modes), equipment, drop zone, cutscene skip, quest log, furniture placement v1, dresser, aquarium. The real parity gaps are roughly 10-12 small to medium items.

This document confirms a **revised milestone structure** rather than refining the old one.

---

## Proposed structure

| Release | Theme | Items |
|---|---|---|
| **v3.7.0** | Bug Fix Release 2 | #64 logging cleanup · #49 Nexus reply · #63 bed slot · #65 FTM diagnostic · #54 Gamesir/G Cloud trigger skip · #48 Y-overlap on Xbox/PS · #56 Luna freeze (analyze log) |
| **v3.8.0** | Console Parity — Quick Wins | #22b dialogue selection default · #17 title menu cursor · #35 load game cursor · #39 monster eradication page · #46 bundle highlight greying · #47 missed rewards chest · #27 toolbar size slider · #19 geode visual feedback |
| **v3.9.0** | Console Parity — Big Systems | #18 museum donations (snap-based, no cursor) · #25 tool charging while moving · #25b slingshot aim-and-fire |
| **v4.0.0** | The Right Stick Update | #12 cursor MVP + zoom slider + acceleration tuning + auto-hide + snap-nav interaction · #62 right-stick to move furniture ghost |
| **post-4.0** | Advanced Features | #23 lock inventory slots · #24 layout profiles · #38 GMCM two-tier redesign |

**Parked (Won't Fix unless re-reported):**

- #16 Trash can lid animation (5 fix attempts failed; cosmetic only)
- #16d CarpenterMenu direct ghost control (already labeled "someday"; A-tap workaround works)
- #15 Disable touchscreen option (already "Deprioritized"; touch is a useful fallback)
- #13c Color picker cursor slight offset (already "Not blocking. Cosmetic only.")
- #26 SMAPI menu button position on G Cloud (already "Not Our Bug")

**Moved to release-tooling doc (out of feature TODO):**

- #66 Nexus mod-page automation (description / version field)
- #67 Expired Nexus session cookie refresh

---

## Reasoning per release

### v3.7.0 — Bug Fix Release 2

**Why this exists, and why first.** Two months of stepping away ended with a desire for momentum, not a long testing cycle. The leftover Nexus queue (#48, #54, #56, #49) is finite and uninvestigated, not regenerating — the Nexus comment stream went quiet in late March and stayed quiet. Draining it as fast 0.0.1 patches matches the v3.5/3.6 pattern that's already proven to work and produces a clean v3.7.0 label.

**Why these items, in this order.** Ordered by cost-to-investigate so quick wins ship first and uncertainty is contained:

1. **#64 logging cleanup** — pure code cleanup, no investigation. Downgrades `[Bed]` and `[StartHold]` Info → Debug per the existing TODO spec. Ships immediately.
2. **#49 Nexus reply** — comms only, not a code patch. Closes a stale user thread. Doesn't consume a version bump.
3. **#63 bed lands in first inventory slot** — well-spec'd in TODO; single prefix on `removeQueuedFurniture`.
4. **#65 FTM diagnostic** — one-shot patch logs `__result` before/after our prefix on `Furniture.canBeRemoved`. Confirms or rules out the FTM postfix-overwrite hypothesis. Doesn't change the v3.5.35 fix either way; closes a known unknown that could affect other patches.
5. **#54 Gamesir/G Cloud trigger skip** — newly self-reproducible (author has both controllers). Diagnostic-first: log raw and processed trigger values when crossing threshold, see what the actual readings look like, then tune. Same methodology as bed bouncing.
6. **#48 Y-button overlap on Xbox/PS** — investigation phase first (trace `ButtonRemapper.RemapButton()`, check whether vanilla `receiveGamePadButton` re-processes the raw button, test with `EnableButtonRemapping=false`), then targeted fix.
7. **#56 Luna freeze** — pull the existing SMAPI log link from Feb 16 and analyze it. May yield a fix; may be inconclusive. Treated as an investigation rather than a guaranteed patch.

### v3.8.0 — Console Parity — Quick Wins

**Why split from v3.9.** Mixing one-postfix fixes (dialogue defaults, title cursor) with multi-patch system arcs (museum, tool charging) makes for a lumpy release that's hard to bisect and hard to write release notes for. Splitting preserves the quick-wins cadence in v3.8 while letting v3.9 take the time bigger systems need.

**Why these items.** Each is small to medium scope and can be solved with localized patches:

- **#22b dialogue selection default** — selection index init in dialogue choice class. Probably one prefix.
- **#17 title menu cursor / #35 load game cursor** — initial `currentlySnappedComponent` fix at menu open. Same pattern as several existing menu nav fixes.
- **#39 monster eradication page** — page navigation + cursor visibility in the Adventurer's Guild tracking menu. Mid-sized but bounded.
- **#46 bundle highlight greying** — `highlightMethod` override on bundle donation page (same pattern as #44 zero-price greying).
- **#47 missed rewards chest** — investigation-first (does `showMissedRewardsChestEvent` fire on Android? does the tile modification work?), then likely a hook on `markAreaAsComplete` or `doRestoreAreaCutscene`.
- **#27 toolbar size slider** — Options menu work. Slider injection if vanilla doesn't expose it on Android. Bumped to v3.8 because Bug #1050718 is a public Nexus report.
- **#19 geode visual feedback** — apply inventory-style patches on `GeodeMenu`. Same pattern as the GameMenu tab work, not a new system. Belongs in quick wins, not big systems, despite being grouped with cursor in the original M3.

### v3.9.0 — Console Parity — Big Systems

**Why these three together.** Each requires touching a player-facing real-time gameplay system (museum placement, tool charging, slingshot aim) and likely needs multiple patches with device testing. Bundling them into one focused arc keeps testing context warm.

- **#18 Museum donations** — snap-based item selection overlay over the museum's free-placement grid. Confirmed possible without #12 (Switch does it). Implementation is non-trivial because the museum grid doesn't map cleanly to discrete components.
- **#25 Tool charging while moving** — Android port difference, not mod-caused. Decompile the tool-use state machine, find the `Farmer.isMoving()` check that prevents charge-state entry, neutralize it. Test across all upgradeable tools at all levels.
- **#25b Slingshot aim-and-fire** — *explicit exception to the "right-stick features ship in v4.0" rule below.* Slingshot is high-usage parity (people actually fight monsters), and gating it on the cursor release would leave a working ranged weapon hostage to a much bigger feature arc. Investigation-first: does our X/Y swap interfere with slingshot's pull-back mechanic? Test with mod disabled, then either disable swap during slingshot use or rework the swap to preserve held-button state. Possibly related to #25.

After v3.9 ships, console parity is materially complete. Anything remaining is either polish (the parked items above) or genuine improvements over console.

### v4.0.0 — The Right Stick Update

**Why a major version bump.** The right-stick cursor is the only remaining feature class that doesn't exist on Switch. Calling it "the Right Stick Update" makes the version bump narratively legible (`v4.0` = the right-stick release) instead of a grab-bag of advanced features. Once shipped, every previously-deferred surface that benefits from a free cursor (future menu work, accessibility tuning, possibly a Steam Deck variant) gets unlocked.

**Placement rule:** *right-stick features ship in v4.0.* The exception is slingshot aim (#25b), which lands in v3.9 because slingshot combat is high-usage gameplay parity and waiting on v4.0 would deny too many users a working ranged weapon. Every other right-stick feature, including the originally-numbered #62, belongs here.

**What's in it.**

- **#12 cursor MVP** — right stick → cursor coords (overworld + menus).
- **#12 zoom slider** — on the in-game Options page (subclass `OptionsSlider`, inject into `OptionsPage.options`).
- **#12 polish** — dead zones, acceleration curves, auto-hide on inactivity.
- **#12 snap-nav interaction** — when cursor mode overrides snap, when snap overrides cursor.
- **#12 GMCM scroll bounds fix** — Mod Options button partially cut off; cursor work is the natural place to fix this.
- **#62 right-stick to move furniture ghost** — right stick offsets the ghost relative to the player's facing tile (not the cursor); A places, B cancels. Reuses the CarpenterMenu cursor-override pattern; closes "console furniture placement" part 2 (part 1 shipped in v3.5.38–v3.5.39).

**Why nothing else.** Bundling right-stick work with locked slots / layout profiles / GMCM redesign would muddy the release narrative and pull testing focus. v4.0 stays the right-stick release; advanced features ship after.

### post-4.0 — Advanced Features

These items are genuine Android-better territory, not parity. They sit after v4.0 because (a) they're not blocking anything users have asked for, (b) they need their own design conversations, and (c) deferring them keeps the cursor release clean.

- **#23 Lock inventory slots** — pairs naturally with #24.
- **#24 Layout profiles** — save / restore inventory arrangements.
- **#38 GMCM two-tier redesign** — needs its own brainstorming session (categories vs presets vs multi-page; depends on what GMCM API supports).

No version commitment yet. These get scheduled when the time comes.

---

## What changed vs the original GSD milestones

| Original | Revised | Reason |
|---|---|---|
| M3 (v3.7) = #12 + #18 + #19 | v3.7 = bug fixes; #18 → v3.9; #19 → v3.8 | #12 is not actually load-bearing for #18 or #19. "Quick wins" mood pushes cursor down. |
| M4 (v3.8) = #39 + #22b + #16 + #16d + #17 + #35 | v3.8 = #22b + #17 + #35 + #39 (plus other quick-win parity items pulled forward from M6) | #16 and #16d cut to Parked. M4's "dialogue & interaction polish" theme dissolved into "console parity quick wins." |
| M5 (v3.9) = #25 + #25b | v3.9 = #18 + #25 + #25b | M5 was only two items — added #18 to make a coherent "big systems" arc. |
| M6 (v4.0) = grab-bag of #23, #24, #27, #15, #13c, #46, #47, #38 | v4.0 = #12 cursor + #62 right-stick furniture move; #23/#24/#38 → post-4.0; #46/#47/#27 → v3.8; #15/#13c → Parked | M6 was conflating "advanced features" with "leftover parity." Split: parity items landed in v3.8, genuinely advanced items moved past 4.0, cosmetic items parked. v4.0 stays a clean right-stick release. |
| Post-3.6 polish list (#62, #63, #64, #65) | #63/#64/#65 → v3.7; #62 → v4.0 | #63/#64/#65 are bug-fix scope. #62 is a right-stick feature — per the placement rule, it belongs in The Right Stick Update, not the bug-fix cycle. |
| #66, #67 in TODO | Moved to release-tooling doc | They're release infrastructure, not mod features. Mixing surfaces inflates perceived backlog. |

---

## What's preserved unchanged

The `0.0.1`-per-change commit rule stays. POSTMORTEM.md is explicit about why it exists, and v3.5.x's success (39 patches, fully bisectable) confirms it's still serving the project. No proposal to relax it.

The diagnostic-first methodology stays. The bed-bouncing chain (v3.5.33 → v3.5.34 → v3.5.35 → v3.5.36 → v3.5.37) is the model — diagnostics first, then a targeted fix. v3.7's #54, #56, #65, and #48 all benefit from the same pattern.

`TODO.md` and `DONE.md` remain the source of truth for "what's left" and "what's done." This document does not replace them; it informs the next reorganization of TODO.md.

---

## Followup work (not part of this design)

After this design is approved:

1. **Reorganize TODO.md** to match the v3.7 / v3.8 / v3.9 / v4.0 / post-4.0 structure. Move parked items to a `## Won't Fix` section at the bottom with one-line rationale each. Remove #66/#67 from TODO.
2. **Create a release-tooling doc** (likely `docs/RELEASE_TOOLING.md`) capturing the Nexus automation state, the "ruled out" paths from #66, the cookie-refresh procedure from #67, and the Cloudflare-blocking constraint. Keep it as living infrastructure documentation, not a TODO surface.
3. **STATUS.md** mirrors the new milestone table once TODO.md is reorganized.

These are mechanical follow-ups and don't need their own brainstorming sessions.

---

## Open questions deliberately not addressed here

- **Exact item ordering inside each release.** The order shown is reasonable but should adapt to whatever's diagnosable on the day. The point of `0.0.1`-per-change is that ordering inside a release is cheap to revisit.
- **Cursor patch ordering inside v4.0.** v4.0 covers the full #12 scope, but which sub-feature ships first (right-stick → cursor as `v4.0.1`, zoom slider as `v4.0.2`, etc.) is a slicing decision best made closer to the work. Today's decision is just "cursor is its own release, not bundled with parity."
- **Test-device coverage for v3.9 big systems.** Tool charging needs all upgradeable tools at all levels, slingshot needs aim mechanics, museum needs the donation grid. The test plan is its own design.
