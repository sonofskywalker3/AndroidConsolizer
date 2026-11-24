using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// One stow or unstow per press of the use-tool button.
    ///
    /// Android repeats the use-tool button while it is held: Game1.UpdateControlInput sets
    /// useToolButtonPressed on every tick X stays down (Android Game1.cs ~13641, the hold-to-repeat
    /// for tools), so Game1.pressUseToolButton runs every tick. PC calls it only on the press. The
    /// stow code toggles on each call (tick 1 stows and sets _didInitiateItemStow, tick 2 sees an
    /// empty hand and that flag and takes the item back out), so a press of any length left the item
    /// where it was. Once a call has changed the stowed state, the rest of that hold is swallowed;
    /// letting go of the button ends it. A hold that never stows or unstows (a tool swing) is untouched.
    /// </summary>
    internal static class ItemStowPatches
    {
        private static bool _heldSinceToggle;

        internal static void Apply(Harmony harmony, IMonitor monitor)
        {
            harmony.Patch(
                AccessTools.Method(typeof(Game1), nameof(Game1.pressUseToolButton)),
                prefix: new HarmonyMethod(typeof(ItemStowPatches), nameof(Before)),
                postfix: new HarmonyMethod(typeof(ItemStowPatches), nameof(After)));
            monitor.Log("Item stow patch applied (one toggle per use-tool press).", LogLevel.Trace);
        }

        /// <summary>UpdateTicked: the hold ends when the use-tool button is let go.</summary>
        internal static void Tick()
        {
            if (_heldSinceToggle && !UseToolHeld()) _heldSinceToggle = false;
        }

        private static bool UseToolHeld()
            => Game1.input != null && Game1.input.GetGamePadState().IsButtonDown(Buttons.X);

        private static bool Before(ref bool __result, out bool __state)
        {
            __state = Game1.player?.netItemStowed.Value ?? false;
            if (!_heldSinceToggle) return true;
            __result = true;   // the repeat of a press that already stowed or unstowed
            return false;
        }

        private static void After(bool __state)
        {
            if (Game1.player != null && Game1.player.netItemStowed.Value != __state && UseToolHeld())
                _heldSinceToggle = true;
        }
    }
}
