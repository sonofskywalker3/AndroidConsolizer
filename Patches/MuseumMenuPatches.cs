using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// #18 Museum donation controller support.
    ///
    /// Root cause (device-verified, v3.7.58→v3.7.60 on G Cloud — earlier theories were
    /// wrong and are recorded here so we don't relitigate them):
    ///   • The console snap chain (ctor snap, receiveKeyPress grid nav via
    ///     LibraryMuseum.findMuseumPieceLocationInDirection, releaseLeftClick->selectItemAt,
    ///     placeItem) is gated on Game1.options.SnappyMenus. We first assumed the field was
    ///     false on Android — but the v3.7.58 log showed snappyMenus FIELD was already True
    ///     on G Cloud, and v3.7.59 showed the SnappyMenus PROPERTY was already true too
    ///     (field=True, gamepadControls=True, mouse buttons Released). So gating was NOT the
    ///     blocker on this device.
    ///   • The v3.7.60 instrumentation proved the mechanics ALL WORK: D-pad walks the snap
    ///     (component IDs 0→12→13…), the game cursor (Game1.getMouseX/Y, which selectItemAt
    ///     and placeItem actually read) tracks it, selection fires (sel -1→15), and
    ///     placement fires. The ONLY thing missing is a VISIBLE cursor.
    ///   • IClickableMenu.drawMouse only renders when
    ///     `mostRecentlyUsedControlType == ControlType.GAMEPAD`. On Android the controller's
    ///     confirm is delivered as a synthesized touch, so the most-recent control type reads
    ///     TOUCH and the built-in cursor is suppressed — the player flies blind. Same class
    ///     of problem as the GeodeMenu / JunimoNote donation page.
    ///
    /// Fix (two parts, both scoped to the DONATION menu only — OpenRearrangeMenu untouched):
    ///   1. Engage snappy nav: force snappyMenus true (field + a postfix on the SnappyMenus
    ///      property getter), set in the OpenDonationMenu prefix BEFORE construction so the
    ///      ctor's snap init runs. This is belt-and-suspenders for devices where the field is
    ///      genuinely false; on G Cloud it is a confirmed no-op but harmless. Restored on
    ///      close via ModEntry.OnMenuChanged (the getter override auto-reverts when the flag
    ///      clears).
    ///   2. Draw the cursor ourselves: a MuseumMenu.draw postfix renders the snappy cursor
    ///      (tile 44) at Game1.getMouseX/Y, bypassing drawMouse's control-type gate, so the
    ///      player can see what is selected / where the piece will land.
    ///
    /// We patch only plain/safe methods (OpenDonationMenu, the Options getter, MuseumMenu.draw)
    /// — deliberately NOT any input override (receiveKeyPress / releaseLeftClick) to stay clear
    /// of the Android mono-runtime SIGSEGV landmine documented for GeodeMenu.releaseLeftClick.
    /// Restore is solely MenuChanged-driven; the only theoretical stuck-true window is a hard
    /// teardown that nulls activeClickableMenu without raising MenuChanged (e.g. a crash),
    /// which ends the session anyway.
    /// </summary>
    internal static class MuseumMenuPatches
    {
        private static IMonitor Monitor;

        // True while we have force-enabled SnappyMenus for an open donation menu.
        // Guards the restore so it runs exactly once and only when we changed it.
        private static bool _weForcedSnappy;

        // The SnappyMenus value as it was before we forced it true.
        private static bool _savedSnappyMenus;

        // MuseumMenu.rearrangeMode and .reOrganizing are Android-only fields — absent from the
        // PC DLL the project compiles against, so they MUST be accessed via reflection.
        private static FieldInfo _rearrangeModeField;
        private static FieldInfo _reOrganizingField;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            _rearrangeModeField = AccessTools.Field(typeof(MuseumMenu), "rearrangeMode");
            _reOrganizingField = AccessTools.Field(typeof(MuseumMenu), "reOrganizing");
            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(LibraryMuseum), "OpenDonationMenu"),
                    prefix: new HarmonyMethod(typeof(MuseumMenuPatches), nameof(OpenDonationMenu_Prefix))
                );
                // #18 rearrange: same machinery, different entry point. Engaging snappy +
                // cursor here lets us observe/handle the rearrange (move/swap) flow too.
                harmony.Patch(
                    original: AccessTools.Method(typeof(LibraryMuseum), "OpenRearrangeMenu"),
                    prefix: new HarmonyMethod(typeof(MuseumMenuPatches), nameof(OpenRearrangeMenu_Prefix))
                );
                // Force the SnappyMenus PROPERTY true while the donation menu is open.
                // The property getter ANDs the field with gamepadControls + "no mouse
                // button pressed"; the latter is false at ctor time on Android (A-press
                // synthesizes a touch click), which is why the field-only flip failed.
                harmony.Patch(
                    original: AccessTools.PropertyGetter(typeof(Options), nameof(Options.SnappyMenus)),
                    postfix: new HarmonyMethod(typeof(MuseumMenuPatches), nameof(SnappyMenusGetter_Postfix))
                );
                // Draw the snap cursor ourselves — Android's drawMouse suppresses it here
                // (control type reads TOUCH because the controller confirm is a synthesized
                // touch), so the player can't see what is selected / targeted.
                harmony.Patch(
                    original: AccessTools.Method(typeof(MuseumMenu), nameof(MuseumMenu.draw), new Type[] { typeof(SpriteBatch) }),
                    postfix: new HarmonyMethod(typeof(MuseumMenuPatches), nameof(Draw_Postfix))
                );
                // Rearrange: enable museum-grid navigation. In rearrange mode there is no
                // donatable-inventory phase, but vanilla's receiveKeyPress only grid-navigates
                // when heldItem != null || reOrganizing — and reOrganizing is never set true.
                // Set it in a ctor postfix so the D-pad navigates the placed pieces instead of
                // the hidden inventory slots.
                harmony.Patch(
                    original: AccessTools.Constructor(typeof(MuseumMenu), new Type[] { typeof(InventoryMenu.highlightThisItem) }),
                    postfix: new HarmonyMethod(typeof(MuseumMenuPatches), nameof(RearrangeCtor_Postfix))
                );
                monitor.Log("[MuseumMenu] patch applied (OpenDonationMenu/OpenRearrangeMenu prefix + SnappyMenus getter + cursor draw + rearrange grid-nav; restore via MenuChanged).", LogLevel.Trace);
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
        private static void OpenDonationMenu_Prefix() => EngageSnappyForMuseum();

        /// <summary>
        /// Same engage as donation, for the rearrange (move/swap existing pieces) menu.
        /// Rearrange's in-menu navigation differs from donation (no donatable inventory phase):
        /// the RearrangeCtor_Postfix sets reOrganizing=true to switch on museum-grid nav.
        /// </summary>
        private static void OpenRearrangeMenu_Prefix() => EngageSnappyForMuseum();

        /// <summary>Save + force snappyMenus true before a museum menu is constructed, and arm restore.</summary>
        private static void EngageSnappyForMuseum()
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
        /// For a rearrange-mode MuseumMenu, set reOrganizing=true so receiveKeyPress takes the
        /// museum-grid navigation branch (findMuseumPieceLocationInDirection over placed pieces)
        /// instead of snapping through the hidden inventory slots. reOrganizing is read ONLY by
        /// receiveKeyPress's nav gate (nothing else), so this is a surgical change. The first
        /// directional press then jumps the cursor from the inventory onto a free grid tile
        /// (vanilla getFreeDonationSpot path); subsequent presses walk the pieces, A picks
        /// up / swaps.
        /// </summary>
        private static void RearrangeCtor_Postfix(MuseumMenu __instance)
        {
            if (ModEntry.Config?.EnableMuseumDonationController != true) return;
            if (!_weForcedSnappy) return; // our museum menu only
            if (_rearrangeModeField == null || _reOrganizingField == null) return;
            try
            {
                bool rearrange = (bool)_rearrangeModeField.GetValue(__instance);
                if (!rearrange) return; // donation menu is unaffected
                _reOrganizingField.SetValue(__instance, true);
                Monitor.Log("[MuseumMenu] rearrange: set reOrganizing=true to enable museum-grid navigation.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"[MuseumMenu] rearrange ctor postfix failed: {ex.Message}", LogLevel.Warn); } catch { }
            }
        }

        /// <summary>
        /// Draw the snap cursor for the donation menu. Android's IClickableMenu.drawMouse
        /// only renders when mostRecentlyUsedControlType == GAMEPAD, which is false here
        /// (the controller confirm arrives as a synthesized touch), so the cursor is invisible
        /// even though navigation/selection/placement work. We draw tile 44 (the snappy hand)
        /// at the live cursor position (Game1.getMouseX/Y), which tracks the snap.
        /// </summary>
        private static void Draw_Postfix(MuseumMenu __instance, SpriteBatch b)
        {
            if (!_weForcedSnappy) return; // only our donation menu (no-op for rearrange)
            if (ModEntry.Config?.EnableMuseumDonationController != true) return;
            try
            {
                // Mirror the menu's own content-draw gate: skip during the fade-to-black
                // transitions and the exiting state so we don't paint a cursor on black.
                if ((__instance.fadeTimer > 0 && __instance.fadeIntoBlack) || __instance.state == 3)
                    return;

                b.Draw(
                    Game1.mouseCursors,
                    new Vector2(Game1.getMouseX(), Game1.getMouseY()),
                    Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 44, 16, 16),
                    Color.White,
                    0f,
                    Vector2.Zero,
                    4f + Game1.dialogueButtonScale / 150f,
                    SpriteEffects.None,
                    1f
                );
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"[MuseumMenu] cursor draw failed: {ex.Message}", LogLevel.Warn); } catch { }
            }
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
        }
    }
}
