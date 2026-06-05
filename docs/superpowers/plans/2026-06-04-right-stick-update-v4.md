# v4.0 The Right Stick Update — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the console right-stick overworld cursor (right stick moves a visible mouse cursor that aims tools, auto-hiding after 4s) and the placement ghost (#62) to Android Stardew Valley.

**Architecture:** Lean on the engine. The Android build already has the entire overworld cursor (`Game1.UpdateControlInput:13303-13334` moves the cursor from `ThumbSticks.Right`; `Game1.drawMouseCursor:15618-15692` draws + fades it, gated on `options.gamepadControls`). AC currently *starves* it by zeroing the right thumbstick in the overworld (`GameplayButtonPatches.cs:405-415`). This plan diagnoses the runtime state, lifts that suppression behind a renamed `EnableRightStickCursor` toggle, and adds enforcement/fallback **only if** the diagnostic proves the native path is incomplete. The placement ghost is verified to fall out of the cursor before any ghost code is written.

**Tech Stack:** C# / SMAPI / Harmony, .NET 6, Android Stardew Valley (`abc.smapi.gameloader`). No unit-test harness — verification is build → device-deploy → SMAPI-log read (engineering correctness) + one reserved playtest (feel). Device = G Cloud (ControllerLayout Xbox + ControlStyle Switch).

**Spec:** `docs/superpowers/specs/2026-06-04-right-stick-update-v4-design.md`

---

## Project rules (NON-NEGOTIABLE — apply to every task)

- **One change per `0.0.1` version.** Bump `manifest.json` `Version` BEFORE building. Never overwrite an existing build.
- **Build:** `dotnet build AndroidConsolizer.csproj -c Release` (from the project root). Output: `bin/Release/net6.0/AndroidConsolizer X.X.X.zip`.
- **Commit each change** with `git add <specific files>` (never `-A`) and a descriptive `vX.X.X:` message. Co-author line: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- **Deploy + log-pull are the agent's job:** `cd ../SyncdewValley; pwsh -NoProfile -File sync.ps1 deploy` then (after the user tests / or for a pure diagnostic) `pwsh -NoProfile -File sync.ps1 logs`. Log lands at `test-output/SMAPI-latest.txt`.
- **NEVER `/sdcard/`** — use `/storage/emulated/0/`.
- **Read the decompile before any fix:** `…/decompiler/stardew-valley-android/decompiled/StardewValley/StardewValley/Game1.cs`.
- **Diagnose first.** Phase 0 must complete and its log be read before Phase 1 enforcement decisions.
- Pushing commits to GitHub is fine; **releases** (`gh release create`, Nexus) need an explicit "yes."

---

## File structure

| File | Responsibility | Tasks |
|------|----------------|-------|
| `Patches/GameplayButtonPatches.cs` | Add `RawRightStickX` caching; gate the overworld right-stick zeroing behind `!EnableRightStickCursor` + slingshot carve-out. | 1, 4 |
| `Patches/RightStickCursorPatches.cs` (new) | Phase 0 diagnostic; Phase 1 `gamepadControls` enforce + self-draw fallback (only if needed). | 2, 5, 6 |
| `ModConfig.cs` | Rename `SuppressRightStickInOverworld` → `EnableRightStickCursor` (default `true`). | 3 |
| `ModEntry.cs` | GMCM entry reword; register `RightStickCursorPatches`; per-tick enforce (only if needed). | 2, 3, 5 |
| `Patches/FurniturePlacementPatches.cs` / `Patches/CarpenterMenuPatches.cs` | Phase 2 — only if the ghost doesn't follow the cursor automatically. | 7, 8 |

---

## PHASE 0 — Diagnostic

### Task 1: Add `RawRightStickX` caching to GameplayButtonPatches

**Files:**
- Modify: `Patches/GameplayButtonPatches.cs` (field ~22, cached field ~53, restore ~333, capture ~351, cache-write ~495/523/558)

- [ ] **Step 1: Add the public + cached fields**

After the existing `RawRightStickY` field (`GameplayButtonPatches.cs:22`):

```csharp
/// <summary>Raw right stick Y cached from GetState before suppression, for ShopMenuPatches navigation.</summary>
internal static float RawRightStickY;

/// <summary>Raw right stick X cached from GetState before suppression, for right-stick cursor / ghost math.</summary>
internal static float RawRightStickX;
```

After the existing `_cachedRawRightStickY` field (`:53`):

```csharp
private static float _cachedRawRightStickY;
private static float _cachedRawRightStickX;
```

- [ ] **Step 2: Restore X from cache on the intra-tick fast path**

In the cached-state early return block (`:333`), alongside `RawRightStickY = _cachedRawRightStickY;` add:

```csharp
RawRightStickY = _cachedRawRightStickY;
RawRightStickX = _cachedRawRightStickX;
```

- [ ] **Step 3: Capture X before suppression**

At the capture site (`:351`), replace:

```csharp
// Cache raw right stick Y before any suppression, so ShopMenuPatches can use it
RawRightStickY = __result.ThumbSticks.Right.Y;
```

with:

```csharp
// Cache raw right stick X/Y before any suppression (ShopMenu scroll reads Y; right-stick cursor reads both)
RawRightStickY = __result.ThumbSticks.Right.Y;
RawRightStickX = __result.ThumbSticks.Right.X;
```

- [ ] **Step 4: Write X to cache at each finalize site**

At EACH of the three cache-write sites (`:495`, `:523`, `:558` — search for `_cachedRawRightStickY = RawRightStickY;`), add the X line immediately after:

```csharp
_cachedRawRightStickY = RawRightStickY;
_cachedRawRightStickX = RawRightStickX;
```

- [ ] **Step 5: Bump version, build**

Edit `manifest.json` `Version` → `3.9.1`. Run:

```bash
cd "C:/Users/Jeff/Documents/Projects/Stardee Valoo/AndroidConsolizer" && dotnet build AndroidConsolizer.csproj -c Release
```

Expected: `Build succeeded`, produces `bin/Release/net6.0/AndroidConsolizer 3.9.1.zip`.

- [ ] **Step 6: Commit**

```bash
git add manifest.json Patches/GameplayButtonPatches.cs
git commit -m "v3.9.1: Cache RawRightStickX in GameplayButtonPatches for right-stick cursor

Only RawRightStickY was cached (ShopMenu/Social scroll). Add RawRightStickX
(public field + cached field + restore/capture/finalize sites) so the v4.0
right-stick cursor and placement-ghost math can read the X axis. No behavior
change — the field is unused until Phase 1.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Create RightStickCursorPatches with the Phase 0 diagnostic

**Files:**
- Create: `Patches/RightStickCursorPatches.cs`
- Modify: `ModEntry.cs` (register the patch class where the other `*.Apply(harmony, Monitor)` calls live)

- [ ] **Step 1: Find where patches are applied**

Run:

```bash
grep -n "Patches.Apply\|\.Apply(harmony" "C:/Users/Jeff/Documents/Projects/Stardee Valoo/AndroidConsolizer/ModEntry.cs" | head
```

Note the block where classes like `GameplayButtonPatches.Apply(...)` / `SlingshotAimPatches.Apply(...)` are called. Use the SAME signature the neighbouring patch classes use (most take `(Harmony harmony, IMonitor monitor)`; confirm by reading one neighbour, e.g. `SlingshotAimPatches.cs:43`).

- [ ] **Step 2: Write the diagnostic patch class**

Create `Patches/RightStickCursorPatches.cs`. This Phase-0 build does TWO things: (a) when `VerboseLogging` is on, logs the engine cursor state once per tick while the right stick is non-zero; (b) provides a static flag `DiagnosticLiftSuppression` that GameplayButtonPatches will consult so the engine path can run during the diagnostic without yet renaming the config. The diagnostic reads `Game1` statics by reflection where they aren't public.

```csharp
using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// v4.0 right-stick overworld cursor. Phase 0 = diagnostic: confirm the engine's
    /// native cursor path (Game1.UpdateControlInput:13303-13334 + drawMouseCursor:15618-15692)
    /// fires once AC stops zeroing the right thumbstick in the overworld.
    /// </summary>
    internal static class RightStickCursorPatches
    {
        private static IMonitor Monitor;

        /// <summary>
        /// Phase 0 only: when true, GameplayButtonPatches does NOT zero the overworld right
        /// stick, so the engine cursor path can run and the diagnostic can observe it.
        /// Phase 1 removes this in favour of the EnableRightStickCursor config flag.
        /// </summary>
        internal static bool DiagnosticLiftSuppression = true;

        private static int _lastLoggedTick = -1;

        // timerUntilMouseFade is public static int on Android; reflect defensively (absent on PC DLL).
        private static readonly System.Reflection.FieldInfo _timerUntilMouseFade =
            AccessTools.Field(typeof(Game1), "timerUntilMouseFade");

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            // No Harmony patch needed for the diagnostic — it polls from OnUpdateTicked.
            // (Registration kept symmetric with the other patch classes.)
        }

        /// <summary>Call from ModEntry.OnUpdateTicked. Logs engine cursor state while the right stick moves.</summary>
        public static void DiagnosticTick()
        {
            try
            {
                if (ModEntry.Config?.VerboseLogging != true) return;
                if (Game1.activeClickableMenu != null) return;
                if (Game1.player == null) return;

                float rx = GameplayButtonPatches.RawRightStickX;
                float ry = GameplayButtonPatches.RawRightStickY;
                if (rx == 0f && ry == 0f) return;

                if (Game1.ticks == _lastLoggedTick) return;
                _lastLoggedTick = Game1.ticks;

                int fade = -1;
                try { fade = (int)(_timerUntilMouseFade?.GetValue(null) ?? -1); } catch { /* ignore */ }

                Monitor.Log(
                    $"[RStickDiag] rstick=({rx:0.00},{ry:0.00}) gamepadControls={Game1.options?.gamepadControls} " +
                    $"mouseXY=({Game1.getMouseX()},{Game1.getMouseY()}) transparency={Game1.mouseCursorTransparency:0.00} " +
                    $"timerUntilMouseFade={fade} lastCursorMotionWasMouse={Game1.lastCursorMotionWasMouse} " +
                    $"liftSuppression={DiagnosticLiftSuppression}",
                    LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[RStickDiag] error: {ex.Message}", LogLevel.Trace);
            }
        }
    }
}
```

> NOTE: confirm `Game1.lastCursorMotionWasMouse`, `Game1.mouseCursorTransparency`, `Game1.getMouseX/Y` are accessible (public static on Android per the decompile). If any aren't visible from the mod assembly, reflect it the same way as `timerUntilMouseFade`. `Game1.options` may be `Game1.options` static — confirm against a neighbouring patch's usage.

- [ ] **Step 3: Make GameplayButtonPatches honour the diagnostic lift**

In `GameplayButtonPatches.cs:405`, change the overworld suppression guard so the diagnostic can disable it. Replace:

```csharp
if (ModEntry.Config?.SuppressRightStickInOverworld == true
    && Game1.activeClickableMenu == null
    && __result.ThumbSticks.Right != Vector2.Zero)
```

with:

```csharp
if (ModEntry.Config?.SuppressRightStickInOverworld == true
    && !RightStickCursorPatches.DiagnosticLiftSuppression
    && Game1.activeClickableMenu == null
    && __result.ThumbSticks.Right != Vector2.Zero)
```

- [ ] **Step 4: Wire registration + the tick hook in ModEntry**

In the patch-application block, alongside the other `.Apply(...)` calls:

```csharp
RightStickCursorPatches.Apply(harmony, this.Monitor);
```

In `OnUpdateTicked` (find it: `grep -n "OnUpdateTicked" ModEntry.cs`), add near the other per-tick diagnostics:

```csharp
RightStickCursorPatches.DiagnosticTick();
```

- [ ] **Step 5: Bump version, build**

`manifest.json` `Version` → `3.9.2`. Build (command as Task 1 Step 5). Expected `Build succeeded`.

- [ ] **Step 6: Commit**

```bash
git add manifest.json Patches/RightStickCursorPatches.cs Patches/GameplayButtonPatches.cs ModEntry.cs
git commit -m "v3.9.2: Phase 0 right-stick cursor diagnostic

Adds RightStickCursorPatches with a VerboseLogging-gated once-per-tick line
logging gamepadControls / mouse position / mouseCursorTransparency /
timerUntilMouseFade while the right stick is non-zero in the overworld, and a
DiagnosticLiftSuppression flag that lets the engine's native cursor path run by
skipping AC's overworld right-stick zeroing. Answers: does the engine move +
draw the cursor on the G Cloud once un-starved? No behaviour change for users
(VerboseLogging off by default).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 7: Deploy + pull the diagnostic log (agent task — no playtest)**

```bash
cd "C:/Users/Jeff/Documents/Projects/SyncdewValley" && pwsh -NoProfile -File sync.ps1 deploy
```

Ask the user to: load a save, nudge the right stick a few times in the overworld, walk around, wait ~5s without touching it, then say "done." Then:

```bash
cd "C:/Users/Jeff/Documents/Projects/SyncdewValley" && pwsh -NoProfile -File sync.ps1 logs
```

Read `AndroidConsolizer/test-output/SMAPI-latest.txt`. **Record the answers** (drive Phase 1):
1. `gamepadControls=True` or `False`?
2. Does `mouseXY` change as the stick moves?
3. Does `transparency` go > 0 then fall to 0 after ~4s (`timerUntilMouseFade` counts 4000→0)?

---

## PHASE 1 — Enable the overworld cursor

> Tasks 3–4 are the expected core. Tasks 5–6 are CONDITIONAL on the Phase 0 log and may be skipped.

### Task 3: Rename config flag → `EnableRightStickCursor` (default true)

**Files:**
- Modify: `ModConfig.cs:57-62`, `ModEntry.cs:1254-1262` (GMCM), `Patches/GameplayButtonPatches.cs:405`, `Patches/RightStickCursorPatches.cs`

- [ ] **Step 1: Rename + flip the config property**

In `ModConfig.cs:57-62`, replace:

```csharp
/// <summary>
/// Stops the right thumbstick from moving the mouse cursor during overworld gameplay.
/// Vanilla Android maps the right stick to cursor motion, which causes interact/sickle
/// to target tiles many squares away from the player. Disable to restore vanilla behavior.
/// </summary>
public bool SuppressRightStickInOverworld { get; set; } = true;
```

with:

```csharp
/// <summary>
/// Console right-stick cursor: the right thumbstick moves an on-screen mouse cursor that
/// aims tools/interaction at the cursor tile (Switch parity), auto-hiding after ~4s of no
/// input (reverting tools to the facing tile). When false, the right stick is zeroed in the
/// overworld (no cursor drift) — the pre-v4.0 behaviour.
/// </summary>
public bool EnableRightStickCursor { get; set; } = true;
```

- [ ] **Step 2: Update GMCM entry**

In `ModEntry.cs:1254-1262`, replace the `AddBoolOption` block with:

```csharp
configMenu.AddBoolOption(
    mod: this.ModManifest,
    name: () => "Enable Right-Stick Cursor",
    tooltip: () => "Right stick moves an on-screen cursor that aims your tools, matching Switch. " +
                  "Auto-hides after a few seconds of no input, reverting tools to the tile you're facing. " +
                  "Turn off to keep the right stick from moving the cursor (no drift).",
    getValue: () => Config.EnableRightStickCursor,
    setValue: value => Config.EnableRightStickCursor = value
);
```

- [ ] **Step 3: Update the GameplayButtonPatches guard to the new flag semantics**

In `GameplayButtonPatches.cs:405`, replace the (Task-2-modified) guard:

```csharp
if (ModEntry.Config?.SuppressRightStickInOverworld == true
    && !RightStickCursorPatches.DiagnosticLiftSuppression
    && Game1.activeClickableMenu == null
    && __result.ThumbSticks.Right != Vector2.Zero)
```

with (suppress only when the cursor is DISABLED; drop the diagnostic flag):

```csharp
// When the right-stick cursor is OFF, zero the overworld right stick so it doesn't
// drift the engine's mouse cursor. When ON, let the engine move the cursor (Task 4
// adds the slingshot carve-out). Menus are unaffected (activeClickableMenu == null gate).
if (ModEntry.Config?.EnableRightStickCursor == false
    && Game1.activeClickableMenu == null
    && __result.ThumbSticks.Right != Vector2.Zero)
```

- [ ] **Step 4: Remove the now-dead diagnostic lift flag usage**

In `RightStickCursorPatches.cs`, delete the `DiagnosticLiftSuppression` field and its doc comment (the config flag now governs suppression). Keep `DiagnosticTick()`. The `liftSuppression={...}` token in the log line: replace with the live config value:

```csharp
$"cursorEnabled={ModEntry.Config?.EnableRightStickCursor}",
```

- [ ] **Step 5: Bump version, build**

`manifest.json` `Version` → `3.9.3`. Build. Expected `Build succeeded` with no reference errors to the old name.

- [ ] **Step 6: Commit**

```bash
git add manifest.json ModConfig.cs ModEntry.cs Patches/GameplayButtonPatches.cs Patches/RightStickCursorPatches.cs
git commit -m "v3.9.3: Rename SuppressRightStickInOverworld -> EnableRightStickCursor (default on)

Flips the flag's semantics for the v4.0 console cursor: true = right-stick
cursor active (aims tools at the cursor tile, auto-hides after 4s), false =
old no-drift behaviour. GMCM reworded. Overworld suppression now applies only
when the cursor is disabled; the Phase 0 DiagnosticLiftSuppression flag is
retired. Default on = parity-by-default per the design decision.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Slingshot carve-out (don't drive the cursor while a slingshot is equipped)

**Files:**
- Modify: `Patches/GameplayButtonPatches.cs` (the overworld guard from Task 3)

- [ ] **Step 1: Add the slingshot carve-out**

Per the research ("with slingshot equipped, right stick does nothing; aim = left stick" — AC #25b), keep zeroing the right stick whenever the active tool is a slingshot, even with the cursor enabled. In `GameplayButtonPatches.cs`, replace the Task-3 guard with:

```csharp
// Zero the overworld right stick when EITHER the cursor is disabled OR a slingshot is
// the active tool (console: slingshot aim is the LEFT stick; right stick does nothing —
// AC #25b). Otherwise let the engine move the cursor. Menus unaffected.
bool slingshotEquipped = Game1.player?.CurrentTool is StardewValley.Tools.Slingshot;
if ((ModEntry.Config?.EnableRightStickCursor == false || slingshotEquipped)
    && Game1.activeClickableMenu == null
    && __result.ThumbSticks.Right != Vector2.Zero)
{
    __result = new GamePadState(
        new GamePadThumbSticks(__result.ThumbSticks.Left, Vector2.Zero),
        __result.Triggers,
        __result.Buttons,
        __result.DPad
    );
}
```

(Delete the old guard+body it replaces — do not leave a duplicate zeroing block.)

- [ ] **Step 2: Bump version, build**

`manifest.json` `Version` → `3.9.4`. Build. Expected `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add manifest.json Patches/GameplayButtonPatches.cs
git commit -m "v3.9.4: Slingshot carve-out for the right-stick cursor

Keep zeroing the overworld right stick whenever a Slingshot is the active tool,
even with EnableRightStickCursor on, so the cursor never fights #25b slingshot
aim (left stick). Matches console: slingshot equipped => right stick does nothing.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 4: Deploy + reserved playtest (feel)**

```bash
cd "C:/Users/Jeff/Documents/Projects/SyncdewValley" && pwsh -NoProfile -File sync.ps1 deploy
```

Ask the user to test on the G Cloud and give SUBJECTIVE feedback (this is the one reserved meaningful playtest):
- Move the right stick: does a visible cursor appear and move?
- Use hoe/watering can/pickaxe/axe with the cursor active: do they hit the cursor tile?
- Stop touching the stick ~4s: does the cursor fade and tools revert to the facing tile?
- Does cursor-tile targeting + auto-hide *feel* like the Switch?
- Equip a slingshot: does the right stick correctly do nothing (aim still left stick)?

Then pull the log and read it for errors:

```bash
cd "C:/Users/Jeff/Documents/Projects/SyncdewValley" && pwsh -NoProfile -File sync.ps1 logs
```

---

### Task 5 (CONDITIONAL — only if Phase 0 showed `gamepadControls=False`): Force gamepadControls

**Files:**
- Modify: `Patches/RightStickCursorPatches.cs`, `ModEntry.cs` (`OnUpdateTicked`)

- [ ] **Step 1: Skip if not needed**

If the Phase 0 log (Task 2 Step 7) showed `gamepadControls=True`, SKIP this whole task — the engine path already runs. Proceed to Task 7.

- [ ] **Step 2: Add a guarded per-tick enforce**

Only if `gamepadControls=False`. In `RightStickCursorPatches.cs`, add:

```csharp
/// <summary>Force options.gamepadControls true when the cursor is enabled, so the engine's
/// UpdateControlInput right-stick block + drawMouseCursor run (Phase 0 showed it was false).</summary>
public static void EnforceTick()
{
    try
    {
        if (ModEntry.Config?.EnableRightStickCursor != true) return;
        if (Game1.options != null && !Game1.options.gamepadControls)
            Game1.options.gamepadControls = true;
    }
    catch (Exception ex) { Monitor.Log($"[RStickCursor] enforce error: {ex.Message}", LogLevel.Trace); }
}
```

In `ModEntry.OnUpdateTicked`, alongside `RightStickCursorPatches.DiagnosticTick();`:

```csharp
RightStickCursorPatches.EnforceTick();
```

- [ ] **Step 3: Bump version, build, commit, deploy, verify from log**

`manifest.json` → `3.9.5`. Build. Commit (`git add manifest.json Patches/RightStickCursorPatches.cs ModEntry.cs`, message `v3.9.5: Force gamepadControls when right-stick cursor enabled`). Deploy. Pull log, confirm `gamepadControls=True` now and the cursor moves (read `[RStickDiag]`). No playtest unless feel changed.

---

### Task 6 (CONDITIONAL — only if the engine still doesn't DRAW the cursor): Self-draw fallback

**Files:**
- Modify: `Patches/RightStickCursorPatches.cs`

- [ ] **Step 1: Skip if not needed**

If Task 4's playtest confirmed the cursor is visible, SKIP this task. Only do it if the cursor MOVES (mouseXY changes) but never draws (`transparency` stays 0 / no sprite) despite Task 5.

- [ ] **Step 2: Draw the cursor ourselves (the #18 museum pattern)**

Add a `Display.RenderedWorld`-driven draw (or a Harmony postfix on a world-draw method) that renders the menu finger / cursor sprite at `Game1.getMousePosition()` when `EnableRightStickCursor` and `timerUntilMouseFade > 0` and no menu. Use the established AC cursor-draw pattern from `MuseumMenuPatches` / `DialogueBoxPatches` (mouseCursors sheet, tile 44 or `Game1.mouseCursor`). Reference the #18 implementation for the exact `spriteBatch.Draw` call and UI-scale handling.

```csharp
// Pseudocode shape — mirror MuseumMenuPatches' cursor draw for exact sprite/scale args:
// if (Config.EnableRightStickCursor && Game1.activeClickableMenu == null && timerUntilMouseFade > 0)
//     e.SpriteBatch.Draw(Game1.mouseCursors, new Vector2(Game1.getMouseX(), Game1.getMouseY()),
//         Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, Game1.mouseCursor, 16, 16),
//         Color.White, 0f, Vector2.Zero, Game1.pixelZoom, SpriteEffects.None, 1f);
```

- [ ] **Step 3: Bump version (`3.9.6`), build, commit, deploy, playtest the cursor is now visible.**

```bash
git add manifest.json Patches/RightStickCursorPatches.cs
git commit -m "v3.9.6: Self-draw the right-stick cursor (engine draw suppressed on Android)"
```

---

## PHASE 2 — Placement ghost (#62)

### Task 7: Verify the placement ghost follows the cursor (verify-first, likely NO code)

**Files:** none expected (verification task)

- [ ] **Step 1: Reason from the decompile**

With Phase 1 live, the furniture / craftable / seed placement ghost draws its single box/ghost at the *target tile*, which is derived from the mouse position. Since the right-stick cursor now moves the mouse, the target tile = cursor tile, so the ghost should track the cursor with zero new code. Confirm the placement code reads the mouse (not a facing-tile-only override) by re-reading `Patches/FurniturePlacementPatches.cs` (the #68/#73 box-draw branch) and `Game1.GetPlacementGrabTile` in the decompile.

- [ ] **Step 2: Device verification**

After Phase 1 ships, ask the user to: hold furniture → move the right stick → confirm the ghost follows the cursor; A places, B cancels. Also a craftable (e.g. furnace/sprinkler) and a seed. Confirm from their report + log (no errors).

- [ ] **Step 3: If it falls out cleanly**

No commit needed beyond noting it in `DONE.md` (Task 9). If it does NOT follow the cursor, proceed to Task 8.

---

### Task 8 (CONDITIONAL — only if the building/furniture ghost does NOT follow the cursor)

**Files:**
- Modify: `Patches/CarpenterMenuPatches.cs` (building ghost) and/or `Patches/FurniturePlacementPatches.cs`

- [ ] **Step 1: Identify the divergence**

The CarpenterMenu building ghost uses a `GetMouseState` override (the gold-standard ghost-follow pattern). If, post-Phase-1, the building ghost ignores the right stick, it's because that override pins the cursor to its own value rather than the engine mouse. Read `CarpenterMenuPatches.cs` to find the override and feed the right-stick-moved `Game1.getMousePosition()` into it (or remove the override so the engine cursor drives the ghost directly).

- [ ] **Step 2: Apply the minimal wiring**

Drive the existing ghost cursor from `Game1.getMouseX()/getMouseY()` (already right-stick-moved) instead of the override's stored position. Keep A = place, B = cancel.

- [ ] **Step 3: Bump version (`3.9.7` or next), build, commit, deploy, verify**

```bash
git add manifest.json Patches/CarpenterMenuPatches.cs
git commit -m "vX.X.X: Wire right-stick cursor into the carpenter/furniture placement ghost (#62)"
```

---

## PHASE 3 — Wrap-up (no release without explicit "yes")

### Task 9: Docs + version
- [ ] Move #12 and #62 from `TODO.md` to `DONE.md` with root-cause + implementation notes (engine-native cursor, suppression-lift, slingshot carve-out, whether #62 fell out).
- [ ] Update `docs/CHANGELOG.md` with the v4.0 entries (note the `SuppressRightStickInOverworld` → `EnableRightStickCursor` rename so power users know the toggle moved).
- [ ] When the user explicitly says "release," bump `manifest.json` to `4.0.0`, follow the project Release Process (README ≡ Nexus description, What's New, `gh release create`, Nexus update). NOT before an explicit "yes."

---

## Self-review notes (author)

- **Spec coverage:** Overworld cursor → Tasks 2–6. Tool targeting → falls out (Task 4 playtest verifies). Auto-hide 4s → engine-native (verified Task 2 diagnostic, Task 4 playtest). Config rename + default-on → Task 3. Cursor speed (console, no slider) → engine `thumbstickToMouseModifier`, no task needed (no code). Slingshot carve-out → Task 4. Menus unaffected → guard's `activeClickableMenu == null` (Task 3/4). `RawRightStickX` → Task 1. Placement ghost #62 → Tasks 7–8. Every spec §maps to a task.
- **Conditionals are explicit:** Tasks 5, 6, 8 each open with a SKIP condition tied to a specific Phase 0/earlier observation, so an out-of-order reader won't run dead work.
- **Type consistency:** `EnableRightStickCursor` (not `Enable...Mode`), `RawRightStickX`, `DiagnosticTick()`/`EnforceTick()`, `Game1.player.CurrentTool is StardewValley.Tools.Slingshot` used consistently.
- **Version numbers** are placeholders past the linear core (3.9.1→3.9.4 fixed; 3.9.5+ depend on which conditionals run) — each task says "bump to next 0.0.1," which is the project rule.
