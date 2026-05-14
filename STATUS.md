# AndroidConsolizer — Status

**Shipped version (local):** 3.6.9. Not yet pushed to Nexus. Last released to Nexus is still 3.6.0. v3.7.0 milestone is **complete** — ready to ship pending the user's go-ahead.

## Latest commits since v3.6.0

| SHA | Note |
|-----|------|
| `2a0a126` | **v3.6.9:** Revert #48 X/Y diagnostic — bug confirmed stale (fixed by intervening pipeline work) |
| `c337394` | **v3.6.8:** #48 diagnostic — trace X/Y button identity across all input handlers |
| `449c242` | docs: resolve #65 — bed-bounce root cause is async removal pipeline, not FTM |
| `5d54708` | **v3.6.7:** Diagnostic — log every CurrentToolIndex setter change with stack trace |
| `965cd3b` | **v3.6.6:** #54 Trigger release-confirmation state machine fixes dropout-bounce |
| `8952c7e` | docs: drop #56 (Luna freeze) — cold case |
| `9c362e8` | docs: mark #63 done across v3.6.3 — v3.6.5 |
| `21ed48c` | **v3.6.5:** Non-furniture pickups steer into the active toolbar row (#63 finish) |
| `33b6e61` | **v3.6.4:** Furniture pickup steers into the active toolbar row (#63 part 2) |
| `39e740a` | **v3.6.3:** Add EnablePickupToActiveRow toggle (scaffolding) |
| `14d96de` | docs: mark #49 (Nexus reply re feature toggles) as sent |
| `7e7f325` | **v3.6.2:** #64 Diagnostic logging cleanup — Bed/StartHold Info -> Debug |
| `2fead04` | **v3.6.1:** Exclude AndroidControllerFix.Tests/ from main project build |
| `b2b8ce0` | **v3.6.0:** Bug Fix Release |

Uncommitted: `<EnableModDeploy>false</EnableModDeploy>` line in `AndroidConsolizer.csproj` (pre-existing, intentional — prevents PC mod-folder deploys; AC is Android-only).

## Milestone state

Roadmap was re-evaluated after v3.6.0 — see [`docs/superpowers/specs/2026-05-08-roadmap-reevaluation-design.md`](./docs/superpowers/specs/2026-05-08-roadmap-reevaluation-design.md) for full reasoning. The GSD-era M3 → M4 → M5 → M6 ordering has been replaced.

| Release | Theme | Status |
|---------|-------|--------|
| v3.4.x | GameMenu Tabs | **Complete** |
| v3.5.x → v3.6.0 | Chest & Item Polish + Bug Fix Release | **Complete** |
| **v3.7.0** | **Bug Fix Release 2** | **Complete — v3.6.1 – v3.6.9 local, not yet on Nexus** |
| v3.8.0 | Console Parity: Quick Wins | Pending |
| v3.9.0 | Console Parity: Big Systems | Pending |
| v4.0.0 | The Right Stick Update | Pending |
| post-4.0 | Advanced Features | Pending |

**Key roadmap shifts:**
- **#12 cursor decoupled from #18 / #19.** Switch handles museum donations and geode breaking with snap-based navigation, no free cursor required. Bundling them with the cursor was a planning error.
- **v3.7 is now a Bug Fix Release** (not the cursor release) — drains leftover Nexus + post-3.6 polish as fast 0.0.1 patches.
- **v4.0 is "The Right Stick Update"** — the major bump is justified by the single feature class (right-stick cursor + zoom + #62 furniture move). Slingshot aim (#25b) is the explicit v3.9 exception because waiting for v4.0 would deny too many users a working ranged weapon.

## What's pending right now

See [`TODO.md`](./TODO.md) for full detail. High-level summary:

**Completed milestone (v3.7.0 — Bug Fix Release 2) — all items resolved, ready to ship:**
- ✅ #64 Diagnostic logging cleanup — **v3.6.2**
- ✅ #49 Reply to v2.0.0 user re: feature toggles — sent on Nexus, no version bump
- ✅ #63 Pickup steers into the active toolbar row — **v3.6.3 / v3.6.4 / v3.6.5**
- ✅ #54 Trigger column-skip — **v3.6.6** (hysteresis + 4-tick release confirmation; verified on G Cloud analog 37% → 18% spurious, Gamesir X2 digital 0% spurious)
- ➕ Diagnostic-only — **v3.6.7** (CurrentToolIndex setter stack-trace logging, in place to catch a one-off observation that didn't reproduce)
- ✅ #65 — root cause of `removeQueuedFurniture` firing for just-placed bed: async removal pipeline, not FTM. No code change; v3.5.35 gate confirmed correct. See `DONE.md` "#53 Bed Bouncing"
- ✅ #48 Y button overlap on Xbox/PS layout — **confirmed stale.** v3.6.8 diagnostic + G Cloud device test showed no double-fire; the v3.3/v3.4 symptom was fixed by intervening input-pipeline work. Diagnostic reverted in **v3.6.9**. See `DONE.md` "#48 X/Y Button Overlap"
- 🗑 #56 Luna freeze — **dropped 2026-05-13** (cold case; only artifact was a clean v3.3.0 startup log, no freeze captured)

**v3.8.0 — Console Parity: Quick Wins:** #22b dialogue defaults, #17 title cursor, #35 load game cursor, #39 monster eradication, #46 bundle highlight greying, #47 missed rewards chest, #27 toolbar size slider, #19 geode visual feedback.

**v3.9.0 — Console Parity: Big Systems:** #18 museum donations (snap-based), #25 tool charging while moving, #25b slingshot aim (explicit right-stick rule exception).

**v4.0.0 — The Right Stick Update:** #12 cursor MVP + zoom + acceleration + auto-hide, #62 right-stick to move furniture ghost.

**Post-4.0 — Advanced Features:** #23 lock inventory slots, #24 layout profiles, #38 GMCM two-tier redesign.

**Parked (Won't Fix unless re-reported):** #16 trash lid, #16d direct ghost, #15 disable touchscreen, #13c color cursor offset, #26 SMAPI menu button. Details in [`TODO.md`](./TODO.md).

**Release tooling:** Formerly TODO #66 / #67 — now in [`docs/RELEASE_TOOLING.md`](./docs/RELEASE_TOOLING.md). Nexus mod-page description/version field still requires manual paste; `unex changelog` cookie expired and needs refresh before next release if changelog automation is wanted.

## What's done

[`DONE.md`](./DONE.md) is the technical reference — implementation notes, root causes, lessons. Major systems:

- Shop purchasing, selling, quantity, inventory tab, scrolling (v2.7.5–v2.8.22)
- Console-style chest transfer with A/Y, sidebar nav, color picker (v2.9.8–v2.9.34, v3.2.9–v3.3.13)
- Equipment slot handling
- CarpenterMenu — joystick panning, cursor, all build modes (v3.1.14–v3.1.44)
- Furniture placement debounce (v3.1.13)
- Community Center bundles — donation page, overview, ingredient list, reward menu (v3.2.26–v3.4.83)
- Fishing rod bait/tackle + slingshot ammo (v2.7.1, v3.2.17)
- Cutscene skip (v3.3.1)
- Analog trigger multi-read fix (v3.3.2–v3.3.11)
- Touch interrupt handling + drop zone (v3.2.9–v3.2.13)
- 12-slot toolbar with row switching
- Shipping bin controller support
- All GameMenu tabs — Animals, Social (with right-stick scrolling and gift log), Collections, Crafting, Powers, Skills/Levels, Options, CC trigger nav (v3.3.17–v3.3.95)
- Shop cursor fixes, CC bundle reward menu, equipment tooltips (v3.4.30–v3.4.83)
- Nexus Feedback Release 2: right-stick drift, dresser fix, aquarium fix, hold-Start quest log, bed bouncing, console furniture placement (v3.5.11–v3.6.0)

## Reference docs

Reorganized under [`docs/`](./docs/):

| Doc | When to read |
|-----|--------------|
| [`DONE.md`](./DONE.md) | Technical reference for completed work — root causes, file paths, lessons |
| [`docs/POSTMORTEM.md`](./docs/POSTMORTEM.md) | v2.7.10 regression — read before refactoring inventory or extracting helpers |
| [`docs/ANDROID_INVENTORY_NOTES.md`](./docs/ANDROID_INVENTORY_NOTES.md) | Before any inventory feature work |
| [`docs/BUTTON_MAPPING_REFERENCE.md`](./docs/BUTTON_MAPPING_REFERENCE.md) | When working on button remapping or layout/style |
| [`docs/CARPENTER_PAN_SPEC.md`](./docs/CARPENTER_PAN_SPEC.md) | CarpenterMenu joystick panning spec |
| [`docs/CHESTNAV_SPEC.md`](./docs/CHESTNAV_SPEC.md) | Chest sidebar navigation wiring |
| [`docs/SHIPPING_BIN_SPEC.md`](./docs/SHIPPING_BIN_SPEC.md) | Shipping bin implementation spec |
| [`docs/CONTROLLER_MATRIX.md`](./docs/CONTROLLER_MATRIX.md) | Per-device controller compatibility matrix |
| [`docs/CHANGELOG.md`](./docs/CHANGELOG.md) | Version-by-version changelog (may lag behind manifest) |

Stale GSD-era subagent prompts archived under [`docs/archive/`](./docs/archive/).

## Workflow

Planning now follows the **Superpowers** workflow (replacing the older GSD scaffolding that was removed in this reorg):

1. **Brainstorm** the next item from `TODO.md` — produces a design doc in [`docs/superpowers/specs/`](./docs/superpowers/specs/) named `YYYY-MM-DD-<topic>-design.md`.
2. **Plan** — turn the spec into an implementation plan.
3. **Execute** — implement against the plan, one `0.0.1` patch per change (per the project's commit rules).
4. **Verify** — confirm via build + device test before marking done in `DONE.md`.

Project conventions (mandatory diagnostic-first development, one-change-per-version commits, no refactoring inside bug fixes) live in [`.claude/CLAUDE.md`](./.claude/CLAUDE.md).
