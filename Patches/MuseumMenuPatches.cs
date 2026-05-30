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
    /// IMPORTANT (device-verified v3.7.58): the underlying `snappyMenus` FIELD is
    /// already true on G Cloud, yet donation still required touch. The menu gates on
    /// the `SnappyMenus` PROPERTY, whose getter is
    ///   `snappyMenus && gamepadControls && mouseLeft != Pressed && mouseRight != Pressed`.
    /// On Android the "Donate" dialogue is confirmed with an A-press that synthesizes a
    /// touch leftClick, so at the instant the MuseumMenu ctor evaluates the property the
    /// left button reads Pressed → property false → the ctor SKIPS its one-time snap
    /// initialization, leaving the menu with no snap state for its whole lifetime.
    /// Writing the field (which was already true) therefore changes nothing.
    ///
    /// Fix: force the SnappyMenus PROPERTY true for the lifetime of the DONATION menu
    /// (Harmony postfix on Options.get_SnappyMenus, gated by our donation flag). The flag
    /// is set in the OpenDonationMenu prefix BEFORE the menu is constructed, so the ctor's
    /// property read returns true and snap init runs; every in-menu receiveKeyPress check
    /// passes too. We also keep forcing the field true (belt-and-suspenders for devices
    /// where the field itself is false, e.g. the Game1 D-pad->receiveKeyPress dispatch
    /// which reads the lowercase field). OpenRearrangeMenu is intentionally NOT patched.
    /// Restore is driven from ModEntry.OnMenuChanged when OldMenu is MuseumMenu (the
    /// getter override auto-reverts when the flag clears). This patch writes zero
    /// navigation logic — the game's own console code does it once snappy is engaged.
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
                // Force the SnappyMenus PROPERTY true while the donation menu is open.
                // The property getter ANDs the field with gamepadControls + "no mouse
                // button pressed"; the latter is false at ctor time on Android (A-press
                // synthesizes a touch click), which is why the field-only flip failed.
                harmony.Patch(
                    original: AccessTools.PropertyGetter(typeof(Options), nameof(Options.SnappyMenus)),
                    postfix: new HarmonyMethod(typeof(MuseumMenuPatches), nameof(SnappyMenusGetter_Postfix))
                );
                monitor.Log("[MuseumMenu] patch applied (OpenDonationMenu prefix + SnappyMenus getter override; restore via MenuChanged).", LogLevel.Trace);
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

            // TRANSIENT DIAGNOSTIC (remove once the property-getter fix is device-confirmed):
            // log the RAW property conditions so we can see which one (gamepadControls vs a
            // pressed mouse button) was making the SnappyMenus property false at ctor time.
            try
            {
                var ms = Game1.input.GetMouseState();
                Monitor.Log(
                    $"[MuseumMenu/diag] field snappyMenus(was)={_savedSnappyMenus}, gamepadControls={Game1.options.gamepadControls}, "
                    + $"mouseL={ms.LeftButton}, mouseR={ms.RightButton} → getter now FORCED true while donating.",
                    LogLevel.Info);
            }
            catch { }
        }

        /// <summary>
        /// While a donation menu is open, force the SnappyMenus property to true regardless
        /// of the getter's gamepadControls / mouse-button conditions. Auto-reverts when the
        /// donation flag clears on menu close (so it is a no-op the rest of the time, and a
        /// no-op for rearrange-mode menus where we never set the flag).
        /// </summary>
        private static void SnappyMenusGetter_Postfix(ref bool __result)
        {
            if (_weForcedSnappy)
                __result = true;
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
