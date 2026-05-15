using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Harmony patch for DialogueBox question boxes (NPC questions, Yes/No prompts,
    /// event dialogue questions).
    ///
    /// Vanilla Android leaves DialogueBox.selectedResponse at -1 ("nothing selected")
    /// when a question box opens. receiveGamePadButton's wrap-around math then sends the
    /// first "up" press from -1 to responses.Length - 1 (the bottom option) and the first
    /// "down" press from -1 to 0 (the top option), so the initial selection feels backwards.
    ///
    /// Fix: a postfix on setUpQuestions() pre-selects the top option (selectedResponse = 0)
    /// under gamepad control. setUpQuestions() runs in both paths that build a question box
    /// — the DialogueBox(string, Response[]) constructor and checkDialogue() — so one patch
    /// point covers all cases. Touch/mouse users keep vanilla behavior.
    /// </summary>
    internal static class DialogueBoxPatches
    {
        private static IMonitor Monitor;

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
                    Monitor.Log("DialogueBox patches applied.", LogLevel.Trace);
                }
                else
                {
                    Monitor.Log("DialogueBoxPatches: 'setUpQuestions' method not found — patch skipped.", LogLevel.Warn);
                }
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
    }
}
