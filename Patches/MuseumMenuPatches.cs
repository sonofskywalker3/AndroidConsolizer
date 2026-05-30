using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// #18 Museum donation controller support.
    ///
    /// Root cause (decompile-verified): the full console snap chain in the Android
    /// MuseumMenu — ctor cursor snap, receiveKeyPress grid navigation via
    /// LibraryMuseum.findMuseumPieceLocationInDirection, releaseLeftClick ->
    /// inventory.selectItemAt selection, and placeItem placement — is all gated on
    /// Game1.options.SnappyMenus, which is FALSE on Android. Game1's
    /// D-pad/left-stick -> receiveKeyPress dispatch is ALSO SnappyMenus-gated, so with
    /// a controller the D-pad does nothing and A fires a click at a stale, un-moved
    /// cursor. Result: donation requires touch.
    ///
    /// Fix: force SnappyMenus true for the lifetime of the DONATION menu only
    /// (prefix LibraryMuseum.OpenDonationMenu, which is the donate path only —
    /// OpenRearrangeMenu is a separate method and is intentionally NOT patched), then
    /// restore the prior value when the menu closes (driven from ModEntry.OnMenuChanged
    /// when OldMenu is MuseumMenu). The game's own console code then performs all
    /// navigation and placement; this patch writes zero navigation logic.
    ///
    /// We patch a single, plain managed method (OpenDonationMenu) and restore via the
    /// SMAPI MenuChanged event — deliberately NOT patching any input override
    /// (receiveKeyPress / releaseLeftClick) to stay clear of the Android mono-runtime
    /// SIGSEGV landmine documented for GeodeMenu.releaseLeftClick.
    ///
    /// Restore is solely MenuChanged-driven. A donation menu can only be torn down by
    /// a menu transition (which raises MenuChanged) — you cannot return to title with
    /// it open without first closing it — so the flag is reliably restored in normal
    /// play. The only theoretical stuck-true window is a hard teardown that nulls
    /// activeClickableMenu without raising MenuChanged (e.g. a crash), which ends the
    /// session anyway.
    /// </summary>
    internal static class MuseumMenuPatches
    {
        private static IMonitor Monitor;

        // True while we have force-enabled SnappyMenus for an open donation menu.
        // Guards the restore so it runs exactly once and only when we changed it.
        private static bool _weForcedSnappy;

        // The SnappyMenus value as it was before we forced it true.
        private static bool _savedSnappyMenus;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(LibraryMuseum), "OpenDonationMenu"),
                    prefix: new HarmonyMethod(typeof(MuseumMenuPatches), nameof(OpenDonationMenu_Prefix))
                );
                monitor.Log("[MuseumMenu] patch applied (OpenDonationMenu prefix; restore via MenuChanged).", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                monitor.Log($"[MuseumMenu] Failed to apply patch: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Force SnappyMenus true before the donation MuseumMenu is constructed, so the
        /// ctor's `if (Game1.options.SnappyMenus)` snap-setup runs and Game1's
        /// D-pad -> receiveKeyPress dispatch becomes active for the controller.
        /// </summary>
        private static void OpenDonationMenu_Prefix()
        {
            if (ModEntry.Config?.EnableMuseumDonationController != true) return;
            if (_weForcedSnappy) return; // already forced; restore pending on close

            // Use the writable lowercase `snappyMenus` field rather than the
            // `SnappyMenus` property: on the PC DLL the project compiles against the
            // property is getter-only, but the underlying public field is read/write
            // on both PC and Android. The property simply wraps this field, so writing
            // the field has the identical effect the design intends.
            _savedSnappyMenus = Game1.options.snappyMenus;
            Game1.options.snappyMenus = true;
            _weForcedSnappy = true;

            // TRANSIENT DIAGNOSTIC (remove in the follow-up strip-logging patch once
            // root cause is device-confirmed): proves the flag was false and we flipped it.
            try { Monitor.Log($"[MuseumMenu] OpenDonationMenu: forced SnappyMenus true (was {_savedSnappyMenus}).", LogLevel.Info); } catch { }
        }

        /// <summary>
        /// Restore the pre-flip SnappyMenus value. Called from ModEntry.OnMenuChanged
        /// whenever a MuseumMenu closes. Idempotent and a no-op unless we forced the flag
        /// (so it harmlessly runs for rearrange-mode MuseumMenu closes too).
        /// </summary>
        public static void RestoreSnappyOnMenuClose()
        {
            if (!_weForcedSnappy) return;
            Game1.options.snappyMenus = _savedSnappyMenus;
            _weForcedSnappy = false;

            // TRANSIENT DIAGNOSTIC (remove in the follow-up strip-logging patch):
            try { Monitor.Log($"[MuseumMenu] menu closed: restored SnappyMenus to {_savedSnappyMenus}.", LogLevel.Info); } catch { }
        }
    }
}
