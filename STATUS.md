# AndroidConsolizer — Status

**Current version:** **3.7.57** (committed locally). **Last GitHub release: v3.7.0** (2026-05-14). **Everything from v3.7.1 → v3.7.57 is committed locally but NOT pushed** — a publish is owed whenever the user gives the explicit go-ahead (push + GitHub release + README + Nexus).

**Session 2026-05-29 (evening) — geode menu hard-crash found + fixed (device-verified, G Cloud over wireless ADB):**
- **Geode menu crashed the whole game** on open/interaction. Root cause via 6-build wireless bisection (v3.7.50→v3.7.57): a `releaseLeftClick` diagnostic patch I'd added in v3.7.50 (for the since-dropped #19d) — **Harmony-patching `GeodeMenu.releaseLeftClick` hard-crashes the Android mono runtime** (native SIGSEGV, broken trampoline; no managed exception → SMAPI log just stops; read it from `adb logcat -b crash -d`). Vanilla runs the method fine; only the patching is fatal.
- **Fix v3.7.57:** reverted `Patches/GeodeMenuPatches.cs` to its v3.7.49 state (removes the crashing patch + all diagnostic scaffolding). Geode menu device-confirmed working again (opens, single-press A cracks, golden coconut OK).
- **#19d dropped** — its only fix path was the crashing patch. Buzzer+shake (v3.7.34) already covers rejection feedback. Landmine recorded in `DONE.md` + user memory `geodemenu-releaseleftclick-harmony-crash`.
- **Wireless ADB now set up:** G Cloud on stable `192.168.228.87:5555` (`adb tcpip 5555`, survives restarts). Use PowerShell (not Bash) for `/storage/...` paths. Deploy: `ANDROID_SERIAL=192.168.228.87:5555 pwsh -NoProfile -File sync.ps1 deploy`.
- **#18 museum donation pulled into v3.8.0** (user request, public Nexus comment). Fresh-agent brief prepared; brainstorm-first.

**Session 2026-05-29 (device-verified on G Cloud, all closed):**
- **#71** v3.7.45 — museum reward grab consumes the Dwarvish Translation Guide `(O)326` into the skill (set `canUnderstandDwarves`) instead of dumping the book in the bag; also lost books (pSI 102). Fix in `Patches/ItemGrabMenuPatches.cs` (`TryConsumeRewardOnGrab`). Diagnostic `BookRewardDiagnosticPatches` was wrong-path, removed v3.7.44.
- **#68** v3.7.46 — single-tile placement ghost for craftables (machines/sprinklers), parallel branch in `Patches/FurniturePlacementPatches.cs`; toggle `EnableConsoleCraftablePlacement`.
- **#69** v3.7.47 — hold A to craft continuously; new `Patches/CraftingPagePatches.cs`; toggle `EnableHoldToCraft`.
- **#54b** v3.7.49 — trigger double-skip on first press after a row switch: re-arm `_triggerSlotTarget` on LB/RB row switch in `ModEntry.HandleToolbarNavigation` (v3.7.48 snapshot attempt failed — see `DONE.md`).
- See `DONE.md` "v3.8.0 Console Parity: Quick Wins" + "#54b Trigger Double-Skip" for full root causes/lessons.

**Earlier (v3.7.1–v3.7.20), still committed-not-pushed:**
- **v3.7.1** — #22b dialogue option box pre-selection (device-verified).
- **v3.7.2** — first attempt at #17 title cursor; FAILED on device (spec premises were wrong about snappyMenus and transparency).
- **v3.7.3** — #17 diagnostic build that revealed the real root cause (drawMouse suppression on Android title screen, snappyMenus=True on Ayaneo, transparency was already 1, `GamePad.IsConnected` unreliable on Ayaneo).
- **v3.7.4** — #17 real fix: draw the cursor ourselves in a `TitleMenu.draw` postfix (per #40a pattern). Device-verified on Ayaneo (cursor visible on Load) and S26 (touch-only: no cursor — vanilla preserved). *Reverted in v3.7.19 — see below.*
- **v3.7.5–v3.7.14** — #35 LoadGameMenu cursor + navigation arc. Two diagnostic builds (v3.7.5, v3.7.7) and eight fix iterations resolved entry snap, wasted-first-press, vanilla scroll-flicker, and the delete-confirmation dialog cursor. Final fix v3.7.14 device-verified on G Cloud. See `DONE.md` "#35 Load Game Screen Cursor / Navigation".
- **v3.7.15–v3.7.19** — #39 Adventurer's Guild kill list + multipage mail (`LetterViewerMenu`). Diagnostic (v3.7.15) → ctor snap fix on the `(string)` overload (v3.7.16) → diagnostic-with-fix to verify (v3.7.17) → re-snap on page change when arrow becomes invisible + transient cursor-draw debug aid (v3.7.18) → final, cursor-draw removed (v3.7.19). v3.7.19 also deleted `Patches/TitleMenuPatches.cs` — the title cursor sprite drawn by v3.7.4 turned out to be wrong UX: console SDV doesn't show a visible cursor on these menus, snap is the indicator. See memory note `feedback_console_ux_no_cursor`. Device-verified on Ayaneo Pocket Air Mini.
- **v3.7.20** — #46 bundle donation greyout. Save/swap/restore `InventoryMenu.highlightMethod` on `JunimoNoteMenu` donation-page enter/exit; filter delegates to `Bundle.canAcceptThisItem(item, null, ignore_stack_count: true)`. Mirrors #44 sell-tab greyout pattern. Device-verified on Ayaneo.

**Current milestone: v3.8.0 — Console Parity: Quick Wins** (nearly complete). Done: #22b, #17, #35, #39, #46, #71, #68, #69, #54b, **#19 geode** (single-press crack, spatial nav, auto-select, tooltip; crash fixed v3.7.57, device-confirmed working; 19d dropped). **Remaining: #27** (toolbar size slider — has public Nexus bug #1050718), **#47** (missed-rewards chest), and **#18 museum donation** (pulled in from v3.9.0) — all need on-device work. #27/#47 need on-device diagnosis; #18 needs a brainstorm-first design pass.

**Test setup:** primary device is **G Cloud** (ADB push works; `2240TN022448`/`GR0006`). Test save **Cheatside** has CJB Item Spawner installed (menu key `I`), plus SVE/Grandpa's Farm content packs. `tools/seed-dwarf-test.ps1` seeds bombs + 4 Dwarf Scrolls into a save's player inventory for the #71 repro. Deploy via direct ADB or `../SyncdewValley/sync.ps1`; `sync.ps1 logs` pulls + archives.

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
| v3.7.0 | Bug Fix Release 2 | **Complete — shipped to GitHub 2026-05-14** |
| **v3.8.0** | **Console Parity: Quick Wins** | **In progress** |
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

**v3.8.0 — Console Parity: Quick Wins:** ✅ #22b dialogue defaults (v3.7.1), ✅ #17 title cursor (v3.7.4, then reverted to cursor-less in v3.7.19 — snap was the right console-parity behaviour all along), ✅ #35 load game cursor (v3.7.5–v3.7.14), ✅ #39 monster eradication kill list + multipage mail (v3.7.15–v3.7.19), ✅ #46 bundle donation greyout (v3.7.20). Remaining: #47 missed rewards chest, #27 toolbar size slider, #19 geode visual feedback.

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
