# #79 Contextual Right-Stick Cursor Sprite — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make AC's self-drawn right-stick overworld cursor show the contextual sprite (hand/grab over interactables, speech bubble over NPCs, gift box over a giftable-NPC, magnifying glass over inspectables) instead of always the default pointer.

**Architecture:** `Game1.drawMouseCursor` computes the contextual `mouseCursor` each frame, then resets it to `cursor_default` (`Game1.cs:15688`) before our `Display.RenderedHud` draw runs. Rather than capture mid-method (transpiler — risky on Android mono), we **replicate the engine's pick** at draw time in a new `ResolveContextualCursor()` helper, using the persisted `isAction/isSpeech/isInspectionAtCurrentCursorTile` flags + one snapshot-restored call to `Utility.checkForCharacterInteractionAtTile` for NPC talk/gift + the small farm-animal loop. Priority mirrors the engine: NPC → tile hint → animal → default.

**Tech Stack:** C# Harmony mod (SMAPI), .NET 6, builds against the PC Stardew DLL, runs on Android. No unit-test harness — verification is `dotnet build` (clean) + on-device observation on the G Cloud.

**Spec:** `docs/superpowers/specs/2026-06-05-contextual-cursor-79-design.md`

**Reference (read before coding):**
- Android decompile `Game1.cs:15618-15692` (drawMouseCursor), `:7436-7457` (updateCursorTileHint), `:2943-2948` (cursor index constants).
- Android decompile `GameLocation.cs:14009-14044` (isActionableTile sets the flags), `Utility.cs:4925-4996` (checkForCharacterInteractionAtTile), `:4998-5016` (canGrabSomethingFromHere probes tile + tile-below).
- PC decompile confirms `Utility.checkForCharacterInteractionAtTile(Vector2, Farmer)` and `FarmAnimal.GetCursorPetBoundingBox()` exist on the compile target → **direct calls, no string reflection** for these. Cursor constants `Game1.cursor_default/_grab/_gift/_talk/_look` are `public static readonly int` on both DLLs → direct refs.

---

## File Structure

- **Modify only:** `Patches/RightStickCursorPatches.cs`
  - Add `private static int ResolveContextualCursor()`.
  - Change one line in `DrawCursor` to source the sprite index from it.
  - Extend `DiagnosticTick` with a `[RStickCtx]` verbose line.
- **Modify:** `manifest.json` (version bump only).

No other files. No new config toggle (the cursor already gates on `EnableRightStickCursor`).

---

## Task 1: Implement the contextual cursor (one commit, v3.9.11)

**Files:**
- Modify: `manifest.json:4`
- Modify: `Patches/RightStickCursorPatches.cs` (add method, edit `DrawCursor`, edit `DiagnosticTick`)

- [ ] **Step 1: Bump the version**

In `manifest.json`, change line 4 from:
```json
    "Version": "3.9.10",
```
to:
```json
    "Version": "3.9.11",
```

- [ ] **Step 2: Add the `ResolveContextualCursor` helper**

In `Patches/RightStickCursorPatches.cs`, add this method to the `RightStickCursorPatches` class (place it directly **above** the existing `DrawCursor` method):

```csharp
        /// <summary>
        /// Resolve the contextual cursor sprite index for the right-stick cursor, mirroring the
        /// engine's own pick in Game1.drawMouseCursor — which computes it (NPC talk/gift via
        /// canGrabSomethingFromHere, tile hint at 15647-15649, farm animals at 15653-15671) then
        /// resets mouseCursor to cursor_default at Game1.cs:15688, before our RenderedHud draw runs.
        /// Priority matches the engine: NPC talk/gift -> actionable tile hint (grab/look/talk) ->
        /// un-petted animal (grab) -> default. Read-only except the NPC probe, whose side effect on
        /// Game1.mouseCursor/mouseCursorTransparency is snapshot-restored. Any error -> cursor_default.
        /// </summary>
        private static int ResolveContextualCursor()
        {
            try
            {
                Farmer player = Game1.player;
                GameLocation loc = Game1.currentLocation;
                if (player == null || loc == null) return Game1.cursor_default;

                // Cursor world-tile (same formula as updateCursorTileHint, Game1.cs:7446-7447).
                Vector2 cursorTile = new Vector2(
                    (Game1.viewport.X + Game1.getOldMouseX()) / 64,
                    (Game1.viewport.Y + Game1.getOldMouseY()) / 64);

                // 1) NPC talk/gift. checkForCharacterInteractionAtTile sets Game1.mouseCursor as a
                //    side effect (cursor_talk / cursor_gift); snapshot + restore mouseCursor and
                //    transparency so engine state is untouched. Probe the cursor tile and the tile
                //    below, matching canGrabSomethingFromHere (Utility.cs:5009-5013). We call this
                //    directly rather than canGrabSomethingFromHere so we don't double-fire hoverAction.
                int savedCursor = Game1.mouseCursor;
                float savedAlpha = Game1.mouseCursorTransparency;
                Game1.mouseCursor = Game1.cursor_default;
                bool npc = Utility.checkForCharacterInteractionAtTile(cursorTile, player)
                        || Utility.checkForCharacterInteractionAtTile(cursorTile + new Vector2(0f, 1f), player);
                int npcCursor = Game1.mouseCursor;
                Game1.mouseCursor = savedCursor;
                Game1.mouseCursorTransparency = savedAlpha;
                if (npc && (npcCursor == Game1.cursor_talk || npcCursor == Game1.cursor_gift))
                    return npcCursor;

                // 2) Actionable tile hint: chest/mailbox/shipping-bin = grab, MessageSpeech = talk,
                //    Dialogue/Message = look. Flags persist from this tick's updateCursorTileHint.
                if (Game1.isActionAtCurrentCursorTile)
                {
                    return Game1.isSpeechAtCurrentCursorTile ? Game1.cursor_talk
                         : Game1.isInspectionAtCurrentCursorTile ? Game1.cursor_look
                         : Game1.cursor_grab;
                }

                // 3) Un-petted farm animal under the cursor -> grab (mirrors Game1.cs:15653-15671).
                var animals = loc.animals;
                if (animals != null)
                {
                    Vector2 mouseWorld = new Vector2(
                        Game1.getOldMouseX() + Game1.uiViewport.X,
                        Game1.getOldMouseY() + Game1.uiViewport.Y);
                    foreach (var pair in animals.Pairs)
                    {
                        FarmAnimal animal = pair.Value;
                        if (!animal.wasPet.Value
                            && animal.GetCursorPetBoundingBox().Contains((int)mouseWorld.X, (int)mouseWorld.Y))
                            return Game1.cursor_grab;
                    }
                }

                return Game1.cursor_default;
            }
            catch (Exception ex)
            {
                Monitor.Log($"[RStickCtx] resolve error: {ex.Message}", LogLevel.Trace);
                return Game1.cursor_default;
            }
        }
```

- [ ] **Step 3: Source the drawn sprite from the resolver**

In `DrawCursor`, replace this line:
```csharp
                int tile = Game1.mouseCursor >= 0 ? Game1.mouseCursor : 0;
```
with:
```csharp
                int tile = ResolveContextualCursor();
```

(The surrounding `b.Draw(... getSourceRectForStandardTileSheet(Game1.mouseCursors, tile, 16, 16) ...)` call is unchanged — it already indexes the sprite by `tile`.)

- [ ] **Step 4: Add the `[RStickCtx]` verbose diagnostic**

In `DiagnosticTick`, immediately **after** the existing `[RStickDiag]` `Monitor.Log(...)` call (and before the closing of the `try`), add:

```csharp
                Monitor.Log(
                    $"[RStickCtx] tile=({(Game1.viewport.X + Game1.getOldMouseX()) / 64},{(Game1.viewport.Y + Game1.getOldMouseY()) / 64}) " +
                    $"isAction={Game1.isActionAtCurrentCursorTile} isSpeech={Game1.isSpeechAtCurrentCursorTile} " +
                    $"isInspect={Game1.isInspectionAtCurrentCursorTile} resolvedCursor={ResolveContextualCursor()}",
                    LogLevel.Info);
```

(`DiagnosticTick` already early-returns unless `VerboseLogging` is on, an overworld tick, the right stick is moving, and the tick hasn't been logged yet — so this line is rate-limited and only fires while you wiggle the stick over a target. Known minor gap: if you hold the cursor still over an NPC the line won't re-fire; the on-screen sprite is the real check.)

- [ ] **Step 5: Add the `FarmAnimal` using if needed**

`FarmAnimal` lives in the `StardewValley` namespace, which the file already imports (`using StardewValley;` at the top). `GameLocation`, `Farmer`, `Utility` are likewise in `StardewValley`. No new `using` required — confirm the top of the file still has `using StardewValley;` and `using Microsoft.Xna.Framework;` (it does). No change needed in this step unless a build error says otherwise.

- [ ] **Step 6: Build (clean)**

Run:
```
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer" && dotnet build AndroidConsolizer.csproj -c Release
```
Expected: `Build succeeded.` with 0 errors. Output: `bin/Release/net6.0/AndroidConsolizer 3.9.11.zip`.

If it fails on `wasPet.Value`, fall back to `(bool)animal.wasPet` (the PC DLL exposes the implicit `NetBool -> bool` operator). If it fails on `uiViewport`, use `Game1.viewport` instead (both are valid origins for the animal hit-test; uiViewport matches the engine exactly).

- [ ] **Step 7: Commit**

```
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer" && git add manifest.json Patches/RightStickCursorPatches.cs && git commit -m "v3.9.11: #79 contextual right-stick cursor sprite

DrawCursor now sources its sprite from ResolveContextualCursor() instead of the
default pointer. Resolver mirrors Game1.drawMouseCursor's pick (which the engine
computes then discards at Game1.cs:15688 before our RenderedHud draw): NPC
talk/gift via a snapshot-restored Utility.checkForCharacterInteractionAtTile
probe -> actionable tile hint (grab/look/talk via isAction/isSpeech/isInspection
flags) -> un-petted farm animal (grab) -> default. VerboseLogging [RStickCtx]
line logs the inputs + resolved index. Single file + manifest bump."
```

---

## Task 2: Device deploy + verify

**Files:** none (deploy + observe).

- [ ] **Step 1: Deploy to the G Cloud**

```
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\SyncdewValley" && pwsh -NoProfile -File sync.ps1 deploy
```
Expected: DLL + manifest pushed, game relaunched. (Deploy/log-pull is the agent's job.)

- [ ] **Step 2: Ask the user to playtest (meaningful — sprites are visual)**

Ask the user to, with the right-stick cursor visible, hover over:
- a **chest / mailbox / shipping bin** → expect a **hand** cursor,
- a **villager** → expect a **speech bubble** (and a **gift box** while holding a giftable item),
- a **sign / inspectable tile** → expect a **magnifying glass**,
- **empty ground** → expect the **plain pointer**.

- [ ] **Step 3: Pull + read the log**

```
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\SyncdewValley" && pwsh -NoProfile -File sync.ps1 logs
```
Then read `AndroidConsolizer/test-output/SMAPI-latest.txt`. Confirm: no exceptions, `[RStickCtx]` lines present, and `resolvedCursor` is 2/3/4/5 over the matching targets (2=grab, 3=gift, 4=talk, 5=look) and 0 over empty ground. If the flags are all false / `resolvedCursor` always 0 over a real interactable, the data isn't populated on this path — diagnose before iterating (the spec's diagnostic-first fallback).

---

## Task 3: Close-out docs (separate commit, after user confirms)

**Files:**
- Modify: `TODO.md` (mark #79 done), `DONE.md` (add #79 writeup), `STATUS.md` (update remaining-work line), the handoff doc if still referenced.

- [ ] **Step 1: Update the docs**

- In `TODO.md`, mark the `### 79.` item ✅ DONE (v3.9.11, device-verified) with a one-line root cause.
- In `DONE.md`, add a short #79 entry (root cause: engine computes contextual cursor then resets at 15688; fix replicates the pick in `ResolveContextualCursor`).
- In `STATUS.md`, drop #79 from the "remaining before v4.0.0 release" list.

- [ ] **Step 2: Commit**

```
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer" && git add TODO.md DONE.md STATUS.md && git commit -m "docs: #79 contextual cursor done (v3.9.11, device-verified)"
```

---

## Self-Review

- **Spec coverage:** NPC talk/gift ✅ (Task 1 Step 2 part 1), tile grab/look/talk ✅ (part 2), animal grab ✅ (part 3), full-parity all-states ✅ (engine-priority order), no new toggle ✅, diagnostic-first ✅ (Step 4 + Task 2), single file + manifest ✅.
- **Placeholder scan:** none — all code is concrete.
- **Type consistency:** `ResolveContextualCursor()` returns `int`, consumed by `int tile = ...` and the `[RStickCtx]` log; cursor constants and `checkForCharacterInteractionAtTile`/`GetCursorPetBoundingBox` signatures verified against the PC DLL.
