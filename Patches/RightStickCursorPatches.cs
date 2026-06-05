using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// v4.0 right-stick overworld cursor. Phase 0 = diagnostic: confirm the engine's
    /// native cursor path (Game1.UpdateControlInput:13303-13334 + drawMouseCursor:15618-15692)
    /// fires once AC stops zeroing the right thumbstick in the overworld.
    ///
    /// The console overworld cursor is engine-native (NOT the #18 menu drawMouse trap): when
    /// options.gamepadControls is true, the right stick moves the mouse via setMousePositionRaw
    /// and drawMouseCursor fades it after timerUntilMouseFade (4000ms). AC currently starves it
    /// by zeroing the right stick in GameplayButtonPatches. This diagnostic observes whether the
    /// engine path runs on the G Cloud once un-starved.
    /// </summary>
    internal static class RightStickCursorPatches
    {
        private static IMonitor Monitor;

        // Last semantic cursor state logged by DiagnosticTick, to debounce the diagnostic so it
        // logs only on change (not every tick) — the per-tick flood was the #79 v3.9.11 input-lag
        // regression. Format: isAction|isSpeech|isInspect|resolved|npc|npcRaw.
        private static string _lastCtxKey = null;

        // timerUntilMouseFade is public static int on Android; reflect defensively (Android-vs-PC pattern).
        private static readonly FieldInfo _timerUntilMouseFade =
            AccessTools.Field(typeof(Game1), "timerUntilMouseFade");

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                // Make INTERACTION (check / open / gift / talk) follow the right-stick cursor.
                // Game1.pressActionButton's tile gate (Game1.cs:11971) uses the cursor only when
                // lastCursorMotionWasMouse is true, but the controller A-press handler sets it
                // false (Game1.cs:13449) earlier in the same tick. A prefix re-asserts it true
                // right before the gate reads it, while the cursor is active. Tool-swinging
                // (pressUseToolButton) is deliberately NOT patched — it stays on the facing tile
                // (console parity, user-confirmed on Switch 2026-06-05).
                var pressAction = AccessTools.Method(
                    typeof(Game1), nameof(Game1.pressActionButton),
                    new[] { typeof(KeyboardState), typeof(MouseState), typeof(GamePadState) });
                if (pressAction != null)
                {
                    harmony.Patch(
                        pressAction,
                        prefix: new HarmonyMethod(typeof(RightStickCursorPatches), nameof(PressActionButton_Prefix)));
                }
                else
                {
                    Monitor.Log("[RStickCursor] pressActionButton not found; interaction cursor targeting disabled.", LogLevel.Warn);
                }

                // Make TOOL USE + held-object placement follow the right-stick cursor. In
                // pressUseToolButton, when a controller button is held (i.e. you're swinging a
                // tool), Android overrides the target to player.GetLocationNextToWhereYoureFacing()
                // (Game1.cs:12317-12327) — the facing tile. That method has only those two callers
                // (both in the tool path), so a postfix that returns the cursor tile while the
                // cursor is active cleanly redirects tool/placement targeting to the cursor (console
                // parity, user-confirmed: tools hit the selected spot, not the facing spot). When
                // the cursor fades it returns the normal facing tile.
                // Resolve by STRING — GetLocationNextToWhereYoureFacing is absent on the PC
                // reference DLL (Android-only), so nameof/direct refs won't compile. Resolves
                // against the Android Character at runtime; null-checked below.
                var locNextTo = AccessTools.Method(
                    typeof(Character), "GetLocationNextToWhereYoureFacing",
                    new[] { typeof(int) });
                if (locNextTo != null)
                {
                    harmony.Patch(
                        locNextTo,
                        postfix: new HarmonyMethod(typeof(RightStickCursorPatches), nameof(GetLocationNextToWhereYoureFacing_Postfix)));
                }
                else
                {
                    Monitor.Log("[RStickCursor] GetLocationNextToWhereYoureFacing not found; tool cursor targeting disabled.", LogLevel.Warn);
                }

                // Diagonal tool hits. Character.GetToolLocation(bool) — which resolves where the
                // tool actually hits — forces ignoreClick=true when isAnyGamePadButtonBeingHeld()
                // (Character.cs:1215), i.e. while you hold the tool button, so it returns the facing
                // CARDINAL tile and ignores lastClick (the cursor tile). That makes diagonal cursor
                // tiles un-hittable with a controller. Prefix drops that held-button term while the
                // cursor is active so the tool targets lastClick (the cursor tile), incl. diagonals.
                var getToolLoc = AccessTools.Method(
                    typeof(Character), nameof(Character.GetToolLocation), new[] { typeof(bool) });
                if (getToolLoc != null)
                {
                    harmony.Patch(
                        getToolLoc,
                        prefix: new HarmonyMethod(typeof(RightStickCursorPatches), nameof(GetToolLocation_Prefix)));
                }
                else
                {
                    Monitor.Log("[RStickCursor] GetToolLocation(bool) not found; diagonal tool targeting disabled.", LogLevel.Warn);
                }

                // Stop the cursor snapping to screen-center after it fades. Game1.UpdateControlInput
                // recenters the cursor when the right stick moves it again after a full fade
                // (timerUntilMouseFade<=0 && !lastCursorMotionWasMouse -> setMousePositionRaw(center),
                // Game1.cs:13328-13331). The flag is false there because setMousePositionRaw itself
                // nulls it (5536) on the same cursor-move call. A postfix re-asserts it true right
                // after the move (only while the right stick is actually driving the cursor), so the
                // recenter never fires and the cursor resumes where it was.
                var setMouseRaw = AccessTools.Method(
                    typeof(Game1), nameof(Game1.setMousePositionRaw), new[] { typeof(int), typeof(int) });
                if (setMouseRaw != null)
                {
                    harmony.Patch(
                        setMouseRaw,
                        postfix: new HarmonyMethod(typeof(RightStickCursorPatches), nameof(SetMousePositionRaw_Postfix)));
                }
                else
                {
                    Monitor.Log("[RStickCursor] setMousePositionRaw not found; cursor recenter fix disabled.", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[RStickCursor] Apply failed: {ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>
        /// Postfix on Game1.setMousePositionRaw: while the right stick is actively driving the
        /// overworld cursor, re-assert lastCursorMotionWasMouse=true (setMousePositionRaw nulls it).
        /// This keeps the engine from recentering the cursor to mid-screen after it fades, and keeps
        /// the cursor treated as the active pointer. Scoped to right-stick motion so unrelated
        /// setMousePositionRaw calls (menus, warps) are untouched.
        /// </summary>
        private static void SetMousePositionRaw_Postfix()
        {
            try
            {
                if (ModEntry.Config?.EnableRightStickCursor != true) return;
                if (Game1.activeClickableMenu != null) return;
                if (Game1.player?.CurrentTool is StardewValley.Tools.Slingshot) return;
                if (GameplayButtonPatches.RawRightStickX == 0f && GameplayButtonPatches.RawRightStickY == 0f) return;

                Game1.lastCursorMotionWasMouse = true;
            }
            catch (Exception ex)
            {
                Monitor.Log($"[RStickCursor] setMousePositionRaw postfix error: {ex.Message}", LogLevel.Trace);
            }
        }

        /// <summary>
        /// Prefix on Character.GetToolLocation(bool): while the right-stick cursor is active, resolve
        /// the tool target from lastClick (the cursor tile) instead of letting a held tool button
        /// force the facing cardinal tile. Replicates the cursor-is-pointer behavior so diagonal
        /// cursor tiles are hittable. Only forces ignoreClick on !wasMouseVisibleThisFrame (the fade
        /// case), matching what the engine does for a real mouse.
        /// </summary>
        private static bool GetToolLocation_Prefix(Character __instance, bool ignoreClick, ref Vector2 __result)
        {
            try
            {
                if (ModEntry.Config?.EnableRightStickCursor != true) return true;
                if (!(__instance is Farmer farmer) || !farmer.IsLocalPlayer) return true;
                if (Game1.activeClickableMenu != null || Game1.eventUp) return true;
                if (farmer.CurrentTool is StardewValley.Tools.Slingshot) return true;

                int fade = 0;
                try { fade = (int)(_timerUntilMouseFade?.GetValue(null) ?? 0); } catch { return true; }
                if (fade <= 0) return true; // cursor faded → original facing-tile behavior

                // Drop the isAnyGamePadButtonBeingHeld() term; keep the genuine fade fallback.
                bool ic = ignoreClick || !Game1.wasMouseVisibleThisFrame;
                __result = farmer.GetToolLocation(farmer.lastClick, ic);
                return false;
            }
            catch (Exception ex)
            {
                Monitor.Log($"[RStickCursor] GetToolLocation prefix error: {ex.Message}", LogLevel.Trace);
                return true;
            }
        }

        /// <summary>
        /// Postfix on Character.GetLocationNextToWhereYoureFacing: while the right-stick cursor is
        /// active, return the cursor tile (pixel position) instead of the facing-adjacent tile, so
        /// tool swings + held-object placement target the cursor (console parity). Scoped to the
        /// local player and the cursor-visible window (timerUntilMouseFade > 0); when the cursor
        /// fades, the normal facing-tile result stands. The engine clamps to tool range downstream
        /// (GetToolLocation) and turns the player toward the result.
        /// </summary>
        private static void GetLocationNextToWhereYoureFacing_Postfix(Character __instance, ref Vector2 __result)
        {
            try
            {
                if (ModEntry.Config?.EnableRightStickCursor != true) return;
                if (!(__instance is Farmer farmer) || !farmer.IsLocalPlayer) return;
                if (Game1.activeClickableMenu != null || Game1.eventUp) return;
                if (farmer.CurrentTool is StardewValley.Tools.Slingshot) return;

                int fade = 0;
                try { fade = (int)(_timerUntilMouseFade?.GetValue(null) ?? 0); } catch { return; }
                if (fade <= 0) return; // cursor faded → keep the facing-tile result

                __result = new Vector2(Game1.getOldMouseX() + Game1.viewport.X, Game1.getOldMouseY() + Game1.viewport.Y);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[RStickCursor] locNextTo postfix error: {ex.Message}", LogLevel.Trace);
            }
        }

        /// <summary>
        /// Prefix on Game1.pressActionButton: while the right-stick cursor is active, tell the game
        /// the cursor is the pointer so interaction targets the cursor tile (Game1.cs:11971) instead
        /// of the facing tile. Scoped to the cursor-visible window (timerUntilMouseFade > 0) so once
        /// the cursor auto-hides, interaction reverts to the facing tile — matching Switch.
        /// </summary>
        private static void PressActionButton_Prefix()
        {
            try
            {
                if (ModEntry.Config?.EnableRightStickCursor != true) return;
                if (Game1.activeClickableMenu != null || Game1.eventUp) return;
                if (Game1.player?.CurrentTool is StardewValley.Tools.Slingshot) return;

                int fade = 0;
                try { fade = (int)(_timerUntilMouseFade?.GetValue(null) ?? 0); } catch { return; }
                if (fade <= 0) return; // cursor faded → let interaction use the facing tile

                Game1.lastCursorMotionWasMouse = true;
            }
            catch (Exception ex)
            {
                Monitor.Log($"[RStickCursor] pressAction prefix error: {ex.Message}", LogLevel.Trace);
            }
        }

        /// <summary>
        /// Call from ModEntry.OnUpdateTicked. When the right stick moves the overworld cursor,
        /// tell the game the cursor is the active pointer by setting lastCursorMotionWasMouse=true.
        ///
        /// Why: Game1.UpdateControlInput moves the cursor from the right stick (13303-13334) but,
        /// unlike the real-mouse branch, leaves lastCursorMotionWasMouse=false. On Android the
        /// cursor-draw gate (Game1.cs:11971/12161) and Character.GetToolLocation(1213-1219) then
        /// skip the cursor and fall back to the facing tile (player.GetGrabTile) — so the cursor
        /// is invisible AND tools target the facing tile. Setting the flag true makes the engine
        /// draw the cursor (wasMouseVisibleThisFrame becomes true) and aim tools/interaction at
        /// the cursor tile. When the stick goes idle and the 4s fade zeroes mouseCursorTransparency,
        /// the gates' "transparency == 0" term reverts targeting to the facing tile on its own.
        ///
        /// Scoped: overworld only (no active menu), cursor enabled, not while a slingshot is the
        /// active tool (#25b aim = left stick). One-tick lag on first flick is imperceptible.
        /// </summary>
        public static void EnforceTick()
        {
            try
            {
                if (ModEntry.Config?.EnableRightStickCursor != true) return;
                if (Game1.activeClickableMenu != null) return;
                if (Game1.player?.CurrentTool is StardewValley.Tools.Slingshot) return;
                if (GameplayButtonPatches.RawRightStickX == 0f && GameplayButtonPatches.RawRightStickY == 0f) return;

                Game1.lastCursorMotionWasMouse = true;
            }
            catch (Exception ex)
            {
                Monitor.Log($"[RStickCursor] enforce error: {ex.Message}", LogLevel.Trace);
            }
        }

        /// <summary>
        /// Resolve the contextual cursor sprite index for the right-stick cursor from the engine's
        /// per-tick tile-hint flags (set by GameLocation.isActionableTile): grab over actionable
        /// objects/tiles/buildings (chests, mailbox, ...), talk over MessageSpeech tiles, look over
        /// Dialogue/Message (inspectable) tiles; plus an un-petted farm animal -> grab. Pure read-only
        /// and cheap — no engine helper calls.
        ///
        /// NPC talk/gift (via Utility.checkForCharacterInteractionAtTile) was DROPPED in v3.9.13: it
        /// never resolved villagers reliably (the engine only sets cursor_talk when the NPC has pending
        /// dialogue) AND, called every render frame, its character iteration + held-item gift probe +
        /// checkForSpecialCharacterIconAtThisTile side effect hung toolbar slot changes and bounced the
        /// placement ghost. Per the user's call (parity isn't worth breaking the toolbar), it's out.
        /// See DONE.md / #79. Any error -> cursor_default.
        /// </summary>
        private static int ResolveContextualCursor()
        {
            try
            {
                GameLocation loc = Game1.currentLocation;
                if (loc == null) return Game1.cursor_default;

                // Actionable tile hint: chest/mailbox/etc = grab, MessageSpeech = talk,
                // Dialogue/Message = look. Flags persist from this tick's updateCursorTileHint.
                if (Game1.isActionAtCurrentCursorTile)
                {
                    return Game1.isSpeechAtCurrentCursorTile ? Game1.cursor_talk
                         : Game1.isInspectionAtCurrentCursorTile ? Game1.cursor_look
                         : Game1.cursor_grab;
                }

                // Un-petted farm animal under the cursor -> grab (mirrors Game1.cs:15653-15671).
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

        /// <summary>
        /// Draw the overworld right-stick cursor ourselves. The Android engine's drawMouseCursor
        /// computes cursor state (transparency, wasMouseVisibleThisFrame) but renders NO sprite in
        /// the overworld — mobile strips the hardware cursor — so it stays invisible even with
        /// lastCursorMotionWasMouse=true (device-confirmed v3.9.5). Mirror the #18 museum self-draw:
        /// paint the pointer at Game1.getMouseX/Y. Gated on timerUntilMouseFade > 0, which the engine
        /// counts down from 4000 after the last stick motion — so the cursor auto-hides ~4s after you
        /// stop moving it, matching console. Call from a Display.RenderedHud handler.
        /// </summary>
        public static void DrawCursor(SpriteBatch b)
        {
            try
            {
                if (ModEntry.Config?.EnableRightStickCursor != true) return;
                if (!Context.IsWorldReady || Game1.activeClickableMenu != null || Game1.eventUp) return;
                if (Game1.player == null) return;
                if (Game1.player.CurrentTool is StardewValley.Tools.Slingshot) return;

                int fade = 0;
                try { fade = (int)(_timerUntilMouseFade?.GetValue(null) ?? 0); } catch { return; }
                if (fade <= 0) return; // engine has faded the cursor out (auto-hide)

                float alpha = Game1.mouseCursorTransparency > 0f ? Game1.mouseCursorTransparency : 1f;
                int tile = ResolveContextualCursor();

                b.Draw(
                    Game1.mouseCursors,
                    new Vector2(Game1.getMouseX(), Game1.getMouseY()),
                    Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, tile, 16, 16),
                    Color.White * alpha,
                    0f,
                    Vector2.Zero,
                    4f,
                    SpriteEffects.None,
                    1f
                );
            }
            catch (Exception ex)
            {
                Monitor.Log($"[RStickCursor] draw failed: {ex.Message}", LogLevel.Trace);
            }
        }

        /// <summary>
        /// Call from ModEntry.OnUpdateTicked. Logs the resolved contextual cursor ONLY when its
        /// semantic state changes (debounced), while the cursor is visible. This replaces the old
        /// per-tick [RStickDiag]+[RStickCtx] flood (~30 INFO lines/sec) that stalled Android's main
        /// thread on synchronous log I/O and dropped input edges (#79 v3.9.11 regression). Debouncing
        /// also means it captures hover-on-target transitions (chest/NPC/forage) instead of only the
        /// empty-ground sweeps the stick-moving gate used to log. Also probes whether an NPC is at the
        /// cursor tile + the raw cursor it would set, to diagnose the talk/gift cases.
        /// </summary>
        public static void DiagnosticTick()
        {
            try
            {
                if (ModEntry.Config?.VerboseLogging != true) return;
                if (Game1.activeClickableMenu != null || Game1.player == null) return;

                int fade = -1;
                try { fade = (int)(_timerUntilMouseFade?.GetValue(null) ?? -1); } catch { /* ignore */ }
                if (fade <= 0) { _lastCtxKey = null; return; } // cursor hidden → nothing to log; reset so its next appearance logs

                int resolved = ResolveContextualCursor();

                string key = $"{Game1.isActionAtCurrentCursorTile}|{Game1.isSpeechAtCurrentCursorTile}|" +
                             $"{Game1.isInspectionAtCurrentCursorTile}|{resolved}";
                if (key == _lastCtxKey) return; // debounce: only log when the semantic state changes
                _lastCtxKey = key;

                Monitor.Log(
                    $"[RStickCtx] tile=({(Game1.viewport.X + Game1.getOldMouseX()) / 64},{(Game1.viewport.Y + Game1.getOldMouseY()) / 64}) " +
                    $"isAction={Game1.isActionAtCurrentCursorTile} isSpeech={Game1.isSpeechAtCurrentCursorTile} " +
                    $"isInspect={Game1.isInspectionAtCurrentCursorTile} resolved={resolved}",
                    LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[RStickCtx] diag error: {ex.Message}", LogLevel.Trace);
            }
        }
    }
}
