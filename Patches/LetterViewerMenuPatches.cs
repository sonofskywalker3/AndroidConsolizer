using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// v3.7.15 diagnostic — #39 LetterViewerMenu cursor / navigation.
    ///
    /// Four Harmony patches, log only, no behaviour change:
    ///   1. Postfix on LetterViewerMenu.ctor(string) — Adventure Guild
    ///      kill list and other Game1.drawLetterMessage callers.
    ///   2. Postfix on LetterViewerMenu.ctor(string, string, bool) —
    ///      mailbox letters and collection viewer.
    ///   3. Prefix on receiveGamePadButton(Buttons) — log every press
    ///      with current page state.
    ///   4. Postfix on update(GameTime) — snapshot state on change only.
    ///
    /// Per-instance log caps prevent any stuck state from filling the
    /// log. Counts reset in each ctor postfix so kill list + mailbox
    /// tests each get a fresh budget.
    ///
    /// Removed in the v3.7.16 fix commit. See
    /// docs/superpowers/specs/2026-05-16-monster-eradication-cursor-design.md.
    /// </summary>
    internal static class LetterViewerMenuPatches
    {
        private const int MaxButtonLogs = 40;
        private const int MaxUpdateSnapshots = 20;
        private const int FirstPagePrefixLen = 80;

        private static IMonitor Monitor;
        private static int _buttonLogCount;
        private static int _updateSnapshotCount;
        private static string _lastUpdateHash;

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
                }
                else
                {
                    Monitor.Log("[LetterDiag] LetterViewerMenu(string) ctor not found — postfix skipped.", LogLevel.Warn);
                }

                var ctorMail = AccessTools.Constructor(
                    typeof(LetterViewerMenu),
                    new[] { typeof(string), typeof(string), typeof(bool) });
                if (ctorMail != null)
                {
                    harmony.Patch(
                        original: ctorMail,
                        postfix: new HarmonyMethod(typeof(LetterViewerMenuPatches), nameof(CtorMail_Postfix))
                    );
                }
                else
                {
                    Monitor.Log("[LetterDiag] LetterViewerMenu(string,string,bool) ctor not found — postfix skipped.", LogLevel.Warn);
                }

                var receiveGamePad = AccessTools.Method(
                    typeof(LetterViewerMenu),
                    nameof(LetterViewerMenu.receiveGamePadButton),
                    new[] { typeof(Buttons) });
                if (receiveGamePad != null)
                {
                    harmony.Patch(
                        original: receiveGamePad,
                        prefix: new HarmonyMethod(typeof(LetterViewerMenuPatches), nameof(ReceiveGamePadButton_Prefix))
                    );
                }
                else
                {
                    Monitor.Log("[LetterDiag] LetterViewerMenu.receiveGamePadButton not found — prefix skipped.", LogLevel.Warn);
                }

                var update = AccessTools.Method(
                    typeof(LetterViewerMenu),
                    nameof(LetterViewerMenu.update),
                    new[] { typeof(GameTime) });
                if (update != null)
                {
                    harmony.Patch(
                        original: update,
                        postfix: new HarmonyMethod(typeof(LetterViewerMenuPatches), nameof(Update_Postfix))
                    );
                }
                else
                {
                    Monitor.Log("[LetterDiag] LetterViewerMenu.update not found — postfix skipped.", LogLevel.Warn);
                }

                Monitor.Log("[LetterDiag] Patches applied (v3.7.15 diagnostic).", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[LetterDiag] Failed to apply patches: {ex.Message}", LogLevel.Error);
            }
        }

        private static void CtorString_Postfix(LetterViewerMenu __instance)
        {
            LogOpen(__instance, "(string)");
        }

        private static void CtorMail_Postfix(LetterViewerMenu __instance)
        {
            LogOpen(__instance, "(string,string,bool)");
        }

        private static void LogOpen(LetterViewerMenu menu, string overload)
        {
            try
            {
                // Reset per-instance diagnostic state — each menu open is a fresh test.
                _buttonLogCount = 0;
                _updateSnapshotCount = 0;
                _lastUpdateHash = null;

                int pageCount = menu.mailMessage?.Count ?? -1;
                string firstPagePrefix = "";
                if (menu.mailMessage != null && menu.mailMessage.Count > 0 && menu.mailMessage[0] != null)
                {
                    string first = menu.mailMessage[0];
                    firstPagePrefix = first.Length > FirstPagePrefixLen
                        ? first.Substring(0, FirstPagePrefixLen)
                        : first;
                    // Strip newlines / carets so the log stays single-line per snapshot.
                    firstPagePrefix = firstPagePrefix.Replace('\n', ' ').Replace('\r', ' ').Replace('^', ' ');
                }

                bool forwardVisible = menu.forwardButton?.visible ?? false;
                bool backVisible = menu.backButton?.visible ?? false;

                var snapped = menu.currentlySnappedComponent;
                string snappedDesc = snapped != null
                    ? $"id={snapped.myID},name={snapped.name ?? ""},bounds=({snapped.bounds.X},{snapped.bounds.Y},{snapped.bounds.Width},{snapped.bounds.Height})"
                    : "null";

                int mouseX = Game1.getMouseX();
                int mouseY = Game1.getMouseY();
                bool gamepadConnected;
                try
                {
                    gamepadConnected = GamePad.GetState(PlayerIndex.One).IsConnected;
                }
                catch
                {
                    gamepadConnected = false;
                }

                Monitor.Log(
                    $"[LetterDiag] open overload={overload} "
                    + $"mailMessage.Count={pageCount} firstPagePrefix=\"{firstPagePrefix}\" "
                    + $"forwardButton.visible={forwardVisible} backButton.visible={backVisible} "
                    + $"snapped={snappedDesc} "
                    + $"snappy={Game1.options.snappyMenus} gamepad={Game1.options.gamepadControls} "
                    + $"lastMotionMouse={Game1.lastCursorMotionWasMouse} gamepadConnected={gamepadConnected} "
                    + $"mouse=({mouseX},{mouseY})",
                    LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LetterDiag] LogOpen({overload}) error: {ex.Message}", LogLevel.Error);
            }
        }

        private static void ReceiveGamePadButton_Prefix(LetterViewerMenu __instance, Buttons b)
        {
            try
            {
                if (_buttonLogCount >= MaxButtonLogs) return;

                int page = __instance.page;
                int countMinus1 = (__instance.mailMessage?.Count ?? 0) - 1;
                bool forwardVisible = __instance.forwardButton?.visible ?? false;
                bool backVisible = __instance.backButton?.visible ?? false;

                Monitor.Log(
                    $"[LetterDiag] receiveGamePadButton b={b} page={page}/{countMinus1} "
                    + $"forwardVisible={forwardVisible} backVisible={backVisible}",
                    LogLevel.Info);

                _buttonLogCount++;
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LetterDiag] ReceiveGamePadButton_Prefix error: {ex.Message}", LogLevel.Error);
            }
        }

        private static void Update_Postfix(LetterViewerMenu __instance, GameTime time)
        {
            try
            {
                if (_updateSnapshotCount >= MaxUpdateSnapshots) return;

                var snapped = __instance.currentlySnappedComponent;
                int snappedId = snapped?.myID ?? -1;
                string snappedName = snapped?.name ?? "";
                int page = __instance.page;
                int countMinus1 = (__instance.mailMessage?.Count ?? 0) - 1;
                bool forwardVisible = __instance.forwardButton?.visible ?? false;
                bool backVisible = __instance.backButton?.visible ?? false;

                string hash = $"{page}|{snappedId}|{forwardVisible}|{backVisible}|{Game1.lastCursorMotionWasMouse}";
                if (hash == _lastUpdateHash) return;
                _lastUpdateHash = hash;

                string snappedDesc = snapped != null
                    ? $"id={snappedId},name={snappedName}"
                    : "null";

                Monitor.Log(
                    $"[LetterDiag] update page={page}/{countMinus1} "
                    + $"snapped={snappedDesc} "
                    + $"forwardVisible={forwardVisible} backVisible={backVisible} "
                    + $"lastMotionMouse={Game1.lastCursorMotionWasMouse} "
                    + $"mouse=({Game1.getMouseX()},{Game1.getMouseY()})",
                    LogLevel.Info);

                _updateSnapshotCount++;
            }
            catch (Exception ex)
            {
                Monitor?.Log($"[LetterDiag] Update_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
