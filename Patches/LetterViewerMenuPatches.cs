using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// v3.7.16 fix — #39 Adventure Guild Monster Eradication tracking page
    /// (and any other Game1.drawLetterMessage caller).
    ///
    /// Root cause: LetterViewerMenu has three constructor overloads. The
    /// (string, string, bool) overload used by mailbox letters runs a snap
    /// initialization block at lines 176-185 of the decompile:
    ///
    ///     if (Game1.options.SnappyMenus)
    ///     {
    ///         populateClickableComponentList();
    ///         snapToDefaultClickableComponent();
    ///         if (mailMessage != null and mailMessage.Count &lt;= 1)
    ///         {
    ///             backButton.myID = -100;
    ///             forwardButton.myID = -100;
    ///         }
    ///     }
    ///
    /// The (string) overload used by Game1.drawLetterMessage (and therefore
    /// AdventureGuild.showMonsterKillList) is missing this block entirely.
    /// Result: currentlySnappedComponent stays null on entry, the cursor
    /// isn't moved to the forward arrow, the vanilla "breathing" pulse
    /// animation runs, and A does nothing until the first joystick input
    /// lazily triggers IClickableMenu's snappy-nav path.
    ///
    /// Fix: postfix the (string) ctor with the same snap block. Pure data
    /// fix - once the menu state matches what the mailbox overload produces,
    /// all downstream behaviour (cursor, A-to-turn-page, B-to-close) works
    /// via existing vanilla code paths.
    ///
    /// Confirmed by v3.7.15 diagnostic on GR0006 (test-output/SMAPI-latest.txt
    /// lines 1823-1880). See docs/superpowers/specs/2026-05-16-monster-eradication-cursor-design.md.
    /// </summary>
    internal static class LetterViewerMenuPatches
    {
        private static IMonitor Monitor;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                var ctorString = AccessTools.Constructor(
                    typeof(LetterViewerMenu),
                    new[] { typeof(string) });
                if (ctorString != null)
                {
                    harmony.Patch(
                        original: ctorString,
                        postfix: new HarmonyMethod(typeof(LetterViewerMenuPatches), nameof(CtorString_Postfix))
                    );
                    Monitor.Log("[LetterViewerMenu] Patches applied (v3.7.17 fix+diag).", LogLevel.Trace);
                }
                else
                {
                    Monitor.Log("[LetterViewerMenu] LetterViewerMenu(string) ctor not found - fix skipped.", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[LetterViewerMenu] Failed to apply patches: {ex.Message}", LogLevel.Error);
            }
        }

        private static void CtorString_Postfix(LetterViewerMenu __instance)
        {
            try
            {
                bool snappyGate = Game1.options.SnappyMenus;
                if (!snappyGate)
                {
                    Monitor.Log("[LetterViewerMenu] CtorString_Postfix: SnappyMenus=False, skipping snap.", LogLevel.Info);
                    return;
                }

                int snappedBeforeId = __instance.currentlySnappedComponent?.myID ?? -1;
                int componentsBefore = __instance.allClickableComponents?.Count ?? -1;

                __instance.populateClickableComponentList();
                int componentsAfterPopulate = __instance.allClickableComponents?.Count ?? -1;

                __instance.snapToDefaultClickableComponent();
                int snappedAfterId = __instance.currentlySnappedComponent?.myID ?? -1;
                string snappedAfterName = __instance.currentlySnappedComponent?.name ?? "";

                bool singlePage = __instance.mailMessage != null && __instance.mailMessage.Count <= 1;
                if (singlePage)
                {
                    if (__instance.backButton != null) __instance.backButton.myID = -100;
                    if (__instance.forwardButton != null) __instance.forwardButton.myID = -100;
                }

                int forwardId = __instance.forwardButton?.myID ?? -999;
                int backId = __instance.backButton?.myID ?? -999;
                bool forwardVisible = __instance.forwardButton?.visible ?? false;
                bool backVisible = __instance.backButton?.visible ?? false;

                Monitor.Log(
                    $"[LetterViewerMenu] CtorString_Postfix fired: snappy=True "
                    + $"pages={__instance.mailMessage?.Count ?? -1} "
                    + $"snappedBefore=id={snappedBeforeId} "
                    + $"componentsBefore={componentsBefore} componentsAfterPopulate={componentsAfterPopulate} "
                    + $"snappedAfter=id={snappedAfterId},name={snappedAfterName} "
                    + $"forwardButton.myID={forwardId} backButton.myID={backId} "
                    + $"forwardVisible={forwardVisible} backVisible={backVisible} "
                    + $"singlePageBranch={singlePage}",
                    LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LetterViewerMenu] CtorString_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
