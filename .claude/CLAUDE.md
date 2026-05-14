# AndroidConsolizer — Claude Guidelines

## Project Goal
Make everything possible in the Android version of Stardew Valley work exactly the same as the console (Switch) version, with each fix group being optional via toggles in Generic Mod Config Menu (GMCM).

**Current shipped version:** see `manifest.json`. **Status snapshot:** [`STATUS.md`](../STATUS.md).

## Design Philosophy
- **Fix the data, not the engine.** When something doesn't work, first ask: what did the game get wrong? Bad component positions, broken neighbor IDs, missing registrations — fix those so the game's own systems work correctly. Don't suppress input, intercept navigation, or build parallel systems to replace the game's code. The engine works; give it correct data and let it do its job.
- **Console as baseline, not ceiling.** Where Switch behavior is the right answer, match it exactly. Where we can do better, do better.
- **Examples:** Toolbar, inventory, shipping bin, bait/tackle = match console. Shop quantity selector = improve on console (precision over hold-to-buy).
- **Every feature toggleable.** Users can disable anything they don't want via GMCM.
- **Diagnose first, fix second.** When something doesn't work and we don't know why, build diagnostic patches to gather information. Run 10 diagnostic builds to understand the real problem rather than 1 build that guesses at a workaround. Workarounds without understanding the root cause create fragile code. Diagnostic patches are cheap; wrong fixes are expensive.

## Workflow — Superpowers (replaces older GSD scaffolding)

| Step | What happens | Output |
|------|--------------|--------|
| 1. Brainstorm | Pick a pending item from `TODO.md`, talk through purpose / constraints / approach. | Design doc in `docs/superpowers/specs/YYYY-MM-DD-<topic>-design.md` |
| 2. Plan | Convert spec to a concrete task list with file-level changes. | Implementation plan |
| 3. Execute | Implement against the plan. Keep edits minimal and focused. | Working code, one `0.0.1` commit per change |
| 4. Verify | Build → device test → user confirms → move item from `TODO.md` to `DONE.md`. | Updated `DONE.md`, version bumped |

The `.planning/` GSD scaffolding has been removed. `TODO.md` and `DONE.md` are the source of truth for "what's left" and "what's done." `STATUS.md` is the orientation doc.

## Versioning & Commit Rules

### MANDATORY: One Change Per Version
Every code change gets a **0.0.1** version bump unless the user explicitly specifies a different version number. No exceptions.

- **0.0.+1** (patch) = ONE bug fix OR ONE feature. Never both. Never multiple.
- **0.+1.0** (minor) = Only when the user explicitly requests a minor version bump.
- **+1.0.0** (major) = Only when the user explicitly requests a major version bump.

### MANDATORY: Commit Every Change
Every single code change MUST be committed to git with detailed change notes. Non-negotiable.

1. Update version in `manifest.json` BEFORE building.
2. Build and verify.
3. `git add <specific changed files>` — never `git add .` or `git add -A`.
4. `git commit -m "vX.X.X: Detailed description of the ONE change made"`.
5. The commit message MUST describe: what was changed, why, and which file(s) were modified.

### MANDATORY: No Bundling Changes
- **ONE patch = ONE change.** Do not combine bug fixes. Do not combine a fix with a refactor. Do not combine a feature with cleanup.
- If a feature requires touching multiple files, that's fine — the entire commit is for that ONE feature.
- **NEVER refactor while fixing a bug.** If you see code that could be cleaner, that's a separate 0.0.1 patch.
- **NEVER extract/restructure code in the same patch as a behavior change.** Refactoring is its own patch.

### Why these rules exist
**v2.7.10 REGRESSION:** ~40 versions were built without commits. A regression couldn't be bisected, and all work from v2.7.1–v2.9.2 was lost. See [`docs/POSTMORTEM.md`](../docs/POSTMORTEM.md) for the full analysis. Key lessons:
- NEVER refactor and change behavior in the same commit — even "pure refactoring" can introduce bugs.
- Keep inline logic that works — do NOT extract into helper classes (this caused the regression).
- The v2.7.9 DLL's decompiled `InventoryManagementPatches.cs` is the gold standard — do NOT restructure this file's A-button handling.

**CRITICAL: NEVER overwrite an existing build.** Every build MUST increment the version by at least 0.0.1. Check the current version first, increment it, then build.

## Build Command
```bash
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"
dotnet build AndroidConsolizer.csproj -c Release
```
Output: `bin/Release/net6.0/AndroidConsolizer X.X.X.zip`

## Log Files

User drops SMAPI logs in the test output folder after testing on device:
`AndroidConsolizer/test-output/SMAPI-latest.txt`

Or pull logs via SyncdewValley: `cd ../SyncdewValley && .\sync.ps1 logs`

Always check this file for test results after the user reports back from device testing.

## Release Process

Steps 1–5 are the **per-patch** flow — run for every 0.0.1 change. Step 6 is the **publish** flow — only run when the user explicitly asks to release (typically at a minor-version boundary).

1. Update version in `manifest.json` (always 0.0.+1 unless user says otherwise).
2. Build.
3. Test or have user test the ONE change.
4. `git add <specific files changed>` — list each file by name.
5. `git commit -m "vX.X.X: Detailed description of the single change"`.

### 6. Publish — only when the user explicitly requests a release

Run these in order:

1. **Push code to GitHub** — `git push origin master`.
2. **Update `README.md`** — keep the current style. Add a new "What's New" entry for this release at the top, and move the *previous* "What's New" content down into the bulk changelog list. Also bump the "Current Version" line and update feature sections / config table / known issues as needed. **Claude Code generates this content itself** from the actual changes (`docs/CHANGELOG.md`, commit history, `DONE.md`) — the README is the definitive source of user-facing copy, NOT downstream of Nexus.
3. **Push the README** — commit and `git push origin master`.
4. **Create the GitHub release** — `gh release create vX.X.X "bin/Release/net6.0/AndroidConsolizer X.X.X.zip" --title "vX.X.X - Title" --notes "<changelog since the last release>"`. The release ZIP uploads automatically.
5. **Update Nexus** — update the mod description, version, and changelog on Nexus to match the README/release.

## Decompiled Android Source

The entire Stardew Valley Android build has been decompiled and is available at:
`C:\Users\Jeff\Documents\Projects\decompiler\stardew-valley-android`

**Use this when you need to understand how the game actually works on Android** — method implementations, field names, code paths, draw methods, scroll handling, etc. The PC DLL often differs from Android (see workspace memory: "Android vs PC DLL Differences"). Always check the decompiled Android source rather than guessing.

All agents have full read permissions to this directory.

## MANDATORY: Diagnostic-First Development

These rules are NON-NEGOTIABLE. Every agent working on this project MUST follow them.

### Before writing ANY fix:
1. **Read the decompiled Android source** for the relevant methods. If you haven't read the actual Android implementation, you don't understand the problem. Do NOT guess based on PC DLL or method names.
2. **State the root cause in plain English** before proposing code. If you can't explain WHY the game behaves wrong, build a diagnostic patch instead of a fix.
3. **Check existing logs** at `test-output/SMAPI-latest.txt` (or under `test-output/log-archive/`) — they often already contain the answer.
4. **Check `DONE.md`** for similar solved problems. The same fix patterns recur (fix the data, same-frame vs cross-frame suppression, etc.).

### When root cause is unknown:
5. **Build a DIAGNOSTIC patch** that logs what the game is actually doing. Do NOT guess at a fix. One diagnostic build > ten wrong fix attempts.
6. **Each diagnostic must answer a SPECIFIC question** — not "log everything," but "does `receiveLeftClick` hit-test against `sprites` or `characterSlots`?"

### After the user tests:
7. **Read the SMAPI log FIRST** before proposing next steps. Do not skip this.

### Hard rules:
- **NEVER propose a fix without explaining the root cause.** "Let's try X" is not acceptable.
- **NEVER break a working feature to fix a new one.** If your fix touches working code, stop and re-think.
- **NEVER ignore the decompiled source.** The answer is in there. Read it.

## Key files

| File | Purpose |
|------|---------|
| `ModEntry.cs` | Main entry point, event handlers, GMCM config menu |
| `ModConfig.cs` | Configuration model |
| `ButtonRemapper.cs` | Button remapping logic for layout/style |
| `Patches/GameplayButtonPatches.cs` | Low-level `GamePad.GetState` patches for A/B and X/Y swaps |
| `Patches/ShopMenuPatches.cs` | Shop purchase with A button, quantity handling |
| `Patches/InventoryPagePatches.cs` | Inventory sorting, X button deletion fix |
| `Patches/ItemGrabMenuPatches.cs` | Chest sorting and add-to-stacks |
| `Patches/GameMenuPatches.cs` | Social tab, animals tab, menu navigation |
| `Patches/CarpenterMenuPatches.cs` | Building ghost, joystick panning, furniture debounce |
| `Patches/JunimoNoteMenuPatches.cs` | Community center bundle navigation |
| `Patches/ShippingBinPatches.cs` | Shipping bin controller support |
| `Patches/ToolbarPatches.cs` | Custom toolbar rendering |
| `Patches/FarmerPatches.cs` | Toolbar row locking |
| `Patches/FishingRodPatches.cs` | Fishing rod bait/tackle attachment via controller |
| `Patches/SlingshotPatches.cs` | Slingshot ammo management |
| `Patches/InventoryManagementPatches.cs` | Console-style A/Y button inventory management |

## Reference docs — read these when relevant

| Doc | When to read |
|-----|--------------|
| `STATUS.md` | First — gives current snapshot of milestones, recent commits, pending work |
| `TODO.md` | Before starting any bug fix or feature. Pending work organized by milestone with implementation notes |
| `DONE.md` | Technical reference for completed features. Implementation details, root causes, lessons |
| `docs/POSTMORTEM.md` | When working on inventory management or tempted to refactor / extract helper classes. Full v2.7.10 regression analysis |
| `docs/ANDROID_INVENTORY_NOTES.md` | Before working on any inventory feature — equipment slots, item transfer, chest interactions, held item behavior |
| `docs/BUTTON_MAPPING_REFERENCE.md` | Button remapping or layout/style combinations |
| `docs/CARPENTER_PAN_SPEC.md` | CarpenterMenu joystick panning spec |
| `docs/CHESTNAV_SPEC.md` | Chest sidebar navigation spec |
| `docs/SHIPPING_BIN_SPEC.md` | Shipping bin implementation spec |
| `docs/CONTROLLER_MATRIX.md` | Controller compatibility testing matrix |
| `docs/CHANGELOG.md` | Version-by-version changelog (may lag behind manifest) |
| `docs/archive/` | Stale GSD-era subagent prompts and old planning docs — historical reference only |

## Subdirectory map

| Path | Purpose |
|------|---------|
| `Patches/` | Harmony patches — one file per major game system |
| `docs/` | Technical reference and specs |
| `docs/superpowers/specs/` | New design docs from the brainstorming flow |
| `docs/archive/` | Stale GSD-era documents kept for history |
| `release/` | Nexus / GitHub release tooling: `sync-nexus.mjs`, `nexus_description.*`, `package.json`, etc. |
| `release-notes/` | Per-version release notes and BBCode snippets |
| `marketing/` | Forum / Reddit / community-post drafts |
| `tools/` | Dev/debug PowerShell scripts (`boot-test.ps1`, `mtp_*.ps1` legacy, `screenshot-stardew.ps1`) |
| `references/` | APK extracts, Android DLL, screenshots, old `spec.md` |
| `BUGS/` | Per-bug investigation notes |
| `sync/` | Legacy ADB sync — superseded by `../SyncdewValley/sync.ps1` |
| `test-output/` | Latest pulled SMAPI log + per-version log archive |
| `AndroidControllerFix.Tests/` | Old MSTest project from pre-rename (`AndroidControllerFix` → `AndroidConsolizer`) |

## SyncdewValley — device sync tool (sibling project)

Located at `../SyncdewValley/` — separate git repo. Auto-detects ADB vs MTP transport and syncs saves/mods/configs/APKs between PC and Android.

```powershell
cd ../SyncdewValley
.\sync.ps1 status          # Show device + local state
.\sync.ps1 deploy          # Push DLL + manifest, restart game
.\sync.ps1 logs            # Pull SMAPI-latest.txt
.\sync.ps1                 # Full sync (updates + saves + mods + configs)
```

Flags: `-Force` (skip confirmations), `-DryRun` (preview only).

**Replaces:** `tools/mtp_sync.ps1` and `sync/sync.sh` — both kept here as legacy.

### MANDATORY: No /sdcard/ paths — ever

NEVER use `/sdcard/` in any ADB command, script, path, or documentation. Always use `/storage/emulated/0/` instead. The `/sdcard/` path is a misleading legacy symlink — many devices don't have an SD card at all.

**Correct:** `/storage/emulated/0/Android/data/abc.smapi.gameloader/files/`
**WRONG:** `/sdcard/Android/data/abc.smapi.gameloader/files/`
