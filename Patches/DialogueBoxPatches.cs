using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Harmony patches for DialogueBox question boxes (NPC questions, Yes/No prompts,
    /// event dialogue questions).
    ///
    /// 1) setUpQuestions postfix — pre-select the top option under gamepad control.
    ///    Vanilla Android leaves DialogueBox.selectedResponse at -1 ("nothing selected")
    ///    when a question box opens, so the first up/down press feels backwards.
    ///
    /// 2) draw postfix (#78) — replace vanilla Android's yellow highlight on the selected
    ///    response with the console look: plain box (no yellow tint), full-strength text,
    ///    a red outline matching the tool-hit box, and the regular finger cursor at the
    ///    selected box's bottom-right corner. Controller only; touch/mouse keeps vanilla.
    ///    See docs/superpowers/specs/2026-06-04-dialogue-cursor-78-design.md.
    /// </summary>
    internal static class DialogueBoxPatches
    {
        private static IMonitor Monitor;

        // Red outline thickness in pixels: 1 source-pixel x the menu's 4x box scale,
        // matching the tool-hit-location box border (Farmer.cs:6368, mouseCursors tile 29).
        private const int OUTLINE_THICKNESS = 4;
        // mouseCursors standard tiles: 29 = tool-hit red target box; 44 = the menu/inventory
        // snap "finger" cursor (the same one GeodeMenu and the Museum draw — NOT tile 0, which is
        // the plain mouse pointer).
        private const int ToolHitTileIndex = 29;
        private const int FingerTileIndex = 44;
        // Finger sits this fraction of the box width left of the right edge, on the bottom line.
        private const float FingerRightInset = 0.2f;

        // Sampled-once exact red from the tool-hit sprite (cached). Falls back to pure red.
        private static Color? _toolHitRed;

        // Throttle for the #78 draw diagnostic (VerboseLogging only).
        private static int _lastDiagTick = -1000;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                // setUpQuestions is private on DialogueBox — AccessTools resolves non-public methods.
                var setUpQuestions = AccessTools.Method(typeof(DialogueBox), "setUpQuestions");
                if (setUpQuestions != null)
                {
                    harmony.Patch(
                        original: setUpQuestions,
                        postfix: new HarmonyMethod(typeof(DialogueBoxPatches), nameof(SetUpQuestions_Postfix))
                    );
                }
                else
                {
                    Monitor.Log("DialogueBoxPatches: 'setUpQuestions' method not found — patch skipped.", LogLevel.Warn);
                }

                var draw = AccessTools.Method(typeof(DialogueBox), nameof(DialogueBox.draw), new[] { typeof(SpriteBatch) });
                if (draw != null)
                {
                    harmony.Patch(
                        original: draw,
                        postfix: new HarmonyMethod(typeof(DialogueBoxPatches), nameof(Draw_Postfix))
                    );
                }
                else
                {
                    Monitor.Log("DialogueBoxPatches: 'draw' method not found — cursor patch skipped.", LogLevel.Warn);
                }

                Monitor.Log("DialogueBox patches applied.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply DialogueBox patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Postfix on DialogueBox.setUpQuestions — after the game's setup runs (which leaves
        /// selectedResponse at -1), pre-select the top option so something is visibly
        /// highlighted and the game's up/down navigation behaves intuitively. Gated on the
        /// same condition the game's own setUpForGamePadMode() uses, so touch/mouse users
        /// keep vanilla behavior.
        /// </summary>
        private static void SetUpQuestions_Postfix(DialogueBox __instance)
        {
            try
            {
                if (!Game1.options.gamepadControls || Game1.lastCursorMotionWasMouse)
                    return;
                if (__instance.responses == null || __instance.responses.Length == 0)
                    return;

                __instance.selectedResponse = 0;
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[DialogueBox] SetUpQuestions_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// #78 — Postfix on DialogueBox.draw. Runs after vanilla has drawn the question box
        /// (including the selected option with its yellow highlight box + 0.6-alpha text).
        /// For the selected response only, overlays the console look. Everything else
        /// (question text, unselected options, portraits, transitions) stays vanilla.
        ///
        /// Layout math (the `num` Y accumulation, box rect) mirrors DialogueBox.draw exactly
        /// (DialogueBox.cs:983-998) so the cover box lands on the yellow box pixel-for-pixel.
        /// </summary>
        private static void Draw_Postfix(DialogueBox __instance, SpriteBatch b)
        {
            try
            {
                if (ModEntry.Config == null || !ModEntry.Config.EnableConsoleDialogueCursor)
                    return;
                // NOTE: deliberately NOT gating on `gamepadControls && !lastCursorMotionWasMouse`
                // here. On Android the controller's confirm is delivered as a synthesized touch,
                // so those input-type flags read as touch even on a controller (same trap as #18) —
                // gating on them made this no-op on the G Cloud. AC is controller-only scope, so the
                // config toggle is the gate. Diagnostic below logs the real flag values to confirm.
                if (!__instance.isQuestion || __instance.transitioning)
                    return;

                var responses = __instance.responses;
                if (responses == null || responses.Length == 0)
                    return;

                int sel = __instance.selectedResponse;
                if (sel < 0 || sel >= responses.Length)
                    return;

                // Throttled diagnostic — confirms the gamepad-gate hypothesis and that the postfix
                // now reaches the draw. Remove once #78 is device-verified.
                if (ModEntry.Config.VerboseLogging && Math.Abs(Game1.ticks - _lastDiagTick) > 30)
                {
                    _lastDiagTick = Game1.ticks;
                    Monitor?.Log(
                        $"[#78] Draw_Postfix reached: sel={sel}/{responses.Length} " +
                        $"gamepadControls={Game1.options.gamepadControls} " +
                        $"lastCursorMotionWasMouse={Game1.lastCursorMotionWasMouse}",
                        LogLevel.Debug);
                }

                string currentString = __instance.getCurrentString();
                if (currentString == null)
                    return;
                // Vanilla only draws the responses once the question text has finished typing.
                if (__instance.characterIndexInDialogue < currentString.Length - 1)
                    return;

                int x = __instance.x;
                int y = __instance.y;
                int width = __instance.width;
                int height = __instance.height;
                int heightForQuestions = __instance.heightForQuestions;

                // Top reference for the responses block, then walk down to the selected one.
                int num = y - (heightForQuestions - height)
                          + SpriteText.getHeightOfString(currentString, width - 48) + 48;
                for (int i = 0; i < sel; i++)
                    num += SpriteText.getHeightOfString(responses[i].responseText, width - 80) + 16 + 32;

                // Selected box rect — matches vanilla's selected/yellow rect exactly.
                int boxX = x + 12;
                int boxY = num - 16;
                int boxW = width - 32;
                int boxH = SpriteText.getHeightOfString(responses[sel].responseText, width - 80) + 32;

                // 1) Cover the yellow highlight box with the plain box — kills the yellow tint
                //    and the faded text underneath (opaque 9-slice over an identical rect).
                IClickableMenu.drawTextureBox(
                    b, Game1.mouseCursors, new Rectangle(256, 256, 10, 10),
                    boxX, boxY, boxW, boxH, Color.White, 4f, drawShadow: false);

                // 2) Redraw the selected response text at full alpha (vanilla draws it at 0.6).
                SpriteText.drawString(
                    b, responses[sel].responseText,
                    To4(x + 40), To4(num + 4 - ((responses.Length > 2) ? 4 : 0)),
                    999999, width - 80, 999999, 1f);

                // 3) Red outline matching the tool-hit box border.
                DrawOutline(b, new Rectangle(boxX, boxY, boxW, boxH), OUTLINE_THICKNESS, GetToolHitRed());

                // 4) Menu/inventory finger cursor (tile 44) along the box bottom line, ~20% in
                //    from the right edge.
                b.Draw(
                    Game1.mouseCursors,
                    new Vector2(boxX + boxW - 16 - (int)(boxW * FingerRightInset), boxY + boxH - 16),
                    Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, FingerTileIndex, 16, 16),
                    Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.9f);
            }
            catch (Exception ex)
            {
                if (ModEntry.Config?.VerboseLogging == true)
                    Monitor?.Log($"[DialogueBox] Draw_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Round an int to the nearest multiple of 4 — inlined from Android's Utility.To4
        /// (Utility.cs:5105), which is absent on the PC reference DLL the mod compiles against.
        /// Mirrors the text alignment vanilla's DialogueBox.draw applies to the selected response.
        /// </summary>
        private static int To4(int v)
        {
            int num = v % 4;
            int num2 = v - num;
            if (num > 2)
                num2 += 4;
            return num2;
        }

        /// <summary>Draw a hollow rectangle as four tinted Game1.staminaRect (1x1 white) bars.</summary>
        private static void DrawOutline(SpriteBatch b, Rectangle r, int t, Color color)
        {
            b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, r.Width, t), color);                  // top
            b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y + r.Height - t, r.Width, t), color);   // bottom
            b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, t, r.Height), color);                 // left
            b.Draw(Game1.staminaRect, new Rectangle(r.X + r.Width - t, r.Y, t, r.Height), color);   // right
        }

        /// <summary>
        /// The exact orange-red of the tool-hit-location box (mouseCursors tile 29), sampled
        /// once and cached. Picks the most-red opaque pixel in the tile. Falls back to pure
        /// red if GetData is unavailable on the runtime.
        /// </summary>
        private static Color GetToolHitRed()
        {
            if (_toolHitRed.HasValue)
                return _toolHitRed.Value;

            // Muted orange-red fallback (not pure 255,0,0 — that reads too bright as a solid bar).
            Color result = new Color(200, 50, 35);
            try
            {
                var sheet = Game1.mouseCursors;
                if (sheet != null)
                {
                    Rectangle src = Game1.getSourceRectForStandardTileSheet(sheet, ToolHitTileIndex, 16, 16);
                    var buffer = new Color[src.Width * src.Height];
                    sheet.GetData(0, src, buffer, 0, buffer.Length);
                    // Average the red-dominant opaque pixels — the box's representative tint,
                    // which includes the sprite's shading (so it's muted, not the hottest pixel).
                    long rSum = 0, gSum = 0, bSum = 0;
                    int count = 0;
                    foreach (var c in buffer)
                    {
                        if (c.A < 200 || c.R <= c.G || c.R <= c.B)
                            continue;
                        rSum += c.R; gSum += c.G; bSum += c.B; count++;
                    }
                    if (count > 0)
                        result = new Color((int)(rSum / count), (int)(gSum / count), (int)(bSum / count));
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[DialogueBox] tool-hit red sample failed, using fallback red: {ex.Message}", LogLevel.Trace);
            }

            _toolHitRed = result;
            return result;
        }
    }
}
