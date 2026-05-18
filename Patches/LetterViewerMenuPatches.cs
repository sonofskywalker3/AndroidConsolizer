using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// v3.7.19 — #39 Adventure Guild kill list + multipage mail.
    ///
    /// Snap-only fix (no cursor sprite). Console-parity navigation: the
    /// selection state on the arrow itself is the indicator; no
    /// `Game1.mouseCursors` blit on top. The v3.7.18 Draw_Postfix that
    /// rendered a cursor sprite was useful as a debug aid (confirmed snap
    /// position visually) but isn't shipped. See memory note
    /// "feedback_console_ux_no_cursor".
    ///
    /// Two patches:
    ///
    ///   1. CtorString_Postfix (carried from v3.7.16). The
    ///      `LetterViewerMenu(string)` overload used by
    ///      `Game1.drawLetterMessage` (kill list and other callers) is
    ///      missing the snap block its sibling `(string, string, bool)`
    ///      overload runs at decompile lines 176-185. Mirror that block here.
    ///
    ///   2. Update_Postfix re-snap. Vanilla `snapToDefaultClickableComponent`
    ///      (decompile line 524-538) unconditionally picks `forwardButton`
    ///      (id 102) for non-interactable letters regardless of visibility.
    ///      On the last page `forwardButton.visible = false`, so on every
    ///      flip onto the final page the snapped component becomes an
    ///      invisible button — A targets nothing visible. Symmetric problem
    ///      on page 0. Fix: detect snapped-but-invisible nav buttons in
    ///      Update_Postfix and swap to the other valid target
    ///      (accept-quest > item-grab > opposite arrow).
    ///
    /// Diagnostic logging is left in place — one line per ctor, page change,
    /// and snap change, capped at 30 per menu instance. If a regression
    /// shows up on a later device, the log already says what snap is doing.
    /// </summary>
    internal static class LetterViewerMenuPatches
    {
        private const int LogCapPerInstance = 30;

        private static IMonitor Monitor;
        private static WeakReference<LetterViewerMenu> _lastInstance = new WeakReference<LetterViewerMenu>(null);
        private static int _lastPage = int.MinValue;
        private static int _lastSnappedId = int.MinValue;
        private static int _logCount;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                var ctorString = AccessTools.Constructor(typeof(LetterViewerMenu), new[] { typeof(string) });
                if (ctorString != null)
                {
                    harmony.Patch(
                        original: ctorString,
                        postfix: new HarmonyMethod(typeof(LetterViewerMenuPatches), nameof(CtorString_Postfix)));
                }
                else
                {
                    Monitor.Log("[LetterViewerMenu] (string) ctor not found - snap fix skipped.", LogLevel.Warn);
                }

                var update = AccessTools.Method(
                    typeof(LetterViewerMenu),
                    nameof(LetterViewerMenu.update),
                    new[] { typeof(GameTime) });
                if (update != null)
                {
                    harmony.Patch(
                        original: update,
                        postfix: new HarmonyMethod(typeof(LetterViewerMenuPatches), nameof(Update_Postfix)));
                }
                else
                {
                    Monitor.Log("[LetterViewerMenu] update method not found - page-change re-snap disabled.", LogLevel.Warn);
                }

                Monitor.Log("[LetterViewerMenu] Patches applied (v3.7.19 snap-only).", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[LetterViewerMenu] Failed to apply patches: {ex.Message}", LogLevel.Error);
            }
        }

        private static void ResetIfNewInstance(LetterViewerMenu instance)
        {
            if (!_lastInstance.TryGetTarget(out var prev) || !ReferenceEquals(prev, instance))
            {
                _lastInstance.SetTarget(instance);
                _lastPage = int.MinValue;
                _lastSnappedId = int.MinValue;
                _logCount = 0;
            }
        }

        private static void LogDiag(string msg)
        {
            if (_logCount >= LogCapPerInstance) return;
            _logCount++;
            string suffix = (_logCount == LogCapPerInstance) ? " [LetterDiag cap reached]" : "";
            Monitor.Log(msg + suffix, LogLevel.Info);
        }

        private static void CtorString_Postfix(LetterViewerMenu __instance)
        {
            try
            {
                ResetIfNewInstance(__instance);

                bool snappy = Game1.options.SnappyMenus;
                if (!snappy)
                {
                    LogDiag("[LetterViewerMenu] CtorString_Postfix: SnappyMenus=False, skipping snap.");
                    return;
                }

                __instance.populateClickableComponentList();
                __instance.snapToDefaultClickableComponent();

                bool singlePage = __instance.mailMessage != null && __instance.mailMessage.Count <= 1;
                if (singlePage)
                {
                    if (__instance.backButton != null) __instance.backButton.myID = -100;
                    if (__instance.forwardButton != null) __instance.forwardButton.myID = -100;
                }

                int snappedId = __instance.currentlySnappedComponent?.myID ?? -999;
                bool forwardVis = __instance.forwardButton?.visible ?? false;
                bool backVis = __instance.backButton?.visible ?? false;
                int pages = __instance.mailMessage?.Count ?? -1;

                _lastPage = __instance.page;
                _lastSnappedId = snappedId;

                LogDiag(
                    $"[LetterViewerMenu] CtorString_Postfix: pages={pages} page={__instance.page} "
                    + $"snapped=id={snappedId} forwardVis={forwardVis} backVis={backVis} singlePage={singlePage}");
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LetterViewerMenu] CtorString_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }

        private static void Update_Postfix(LetterViewerMenu __instance, GameTime time)
        {
            try
            {
                if (!Game1.options.SnappyMenus) return;

                ResetIfNewInstance(__instance);

                int page = __instance.page;
                bool forwardVis = __instance.forwardButton?.visible ?? false;
                bool backVis = __instance.backButton?.visible ?? false;
                var snappedBefore = __instance.currentlySnappedComponent;
                int snappedBeforeId = snappedBefore?.myID ?? -999;

                ClickableComponent newSnap = null;
                string reason = null;

                if (ReferenceEquals(snappedBefore, __instance.forwardButton) && !forwardVis)
                {
                    if (__instance.HasQuestOrSpecialOrder
                        && __instance.ShouldShowInteractable()
                        && __instance.acceptQuestButton != null)
                    {
                        newSnap = __instance.acceptQuestButton;
                        reason = "forwardInvisible->acceptQuest";
                    }
                    else if (__instance.itemsToGrab != null
                             && __instance.itemsToGrab.Count > 0
                             && __instance.ShouldShowInteractable())
                    {
                        newSnap = __instance.itemsToGrab[0];
                        reason = "forwardInvisible->itemGrab";
                    }
                    else if (backVis && __instance.backButton != null)
                    {
                        newSnap = __instance.backButton;
                        reason = "forwardInvisible->back";
                    }
                }
                else if (ReferenceEquals(snappedBefore, __instance.backButton) && !backVis && forwardVis)
                {
                    newSnap = __instance.forwardButton;
                    reason = "backInvisible->forward";
                }

                if (newSnap != null)
                {
                    __instance.currentlySnappedComponent = newSnap;
                    __instance.snapCursorToCurrentSnappedComponent();
                }

                int snappedAfterId = __instance.currentlySnappedComponent?.myID ?? -999;

                if (page != _lastPage)
                {
                    LogDiag(
                        $"[LetterViewerMenu] page change: {_lastPage}->{page} "
                        + $"forwardVis={forwardVis} backVis={backVis} "
                        + $"snappedBefore=id={snappedBeforeId} snappedAfter=id={snappedAfterId} "
                        + $"reSnapReason={reason ?? "none"}");
                    _lastPage = page;
                    _lastSnappedId = snappedAfterId;
                }
                else if (snappedAfterId != _lastSnappedId)
                {
                    LogDiag(
                        $"[LetterViewerMenu] snap change (no page change): {_lastSnappedId}->{snappedAfterId} "
                        + $"page={page} forwardVis={forwardVis} backVis={backVis} "
                        + $"reSnapReason={reason ?? "external"}");
                    _lastSnappedId = snappedAfterId;
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LetterViewerMenu] Update_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
