using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Developer test aid: presses controller buttons without a controller, so a change can be
    /// checked on a device over adb. Once a second it looks for <c>sim-input.txt</c> in the mod
    /// folder; each line is a button (A, B, X, Y, LB, RB, Start, Back, Up, Down, Left, Right) and an
    /// optional tick count (default 6), or <c>slot N</c> to select toolbar slot N, <c>item N</c> to put
    /// one (O)N in the first empty slot and select it, <c>state</c> to log what is held, or <c>wait N</c>
    /// ticks. The file is deleted once read. GamePad.GetState's postfix ORs the current button into
    /// the raw state before any remapping, exactly where a physical press would arrive. With no file
    /// present nothing happens.
    /// </summary>
    internal static class InputSimulator
    {
        private const string FileName = "sim-input.txt";
        private const int DefaultTicks = 6;

        private static readonly Queue<(string What, int Arg)> Steps = new();
        private static string _button;
        private static int _until;
        private static int _waitUntil;
        private static IMonitor Monitor;
        private static string _path;

        internal static void Init(IModHelper helper, IMonitor monitor)
        {
            Monitor = monitor;
            _path = Path.Combine(helper.DirectoryPath, FileName);
            // While a scripted press is held, log what the use-tool path does with it.
            var harmony = new Harmony(helper.ModRegistry.ModID + ".InputSim");
            harmony.Patch(AccessTools.Method(typeof(Game1), nameof(Game1.pressUseToolButton)),
                postfix: new HarmonyMethod(typeof(InputSimulator), nameof(AfterUseTool)));
            harmony.Patch(AccessTools.Method(typeof(Game1), nameof(Game1.CanPlayerStowItem)),
                postfix: new HarmonyMethod(typeof(InputSimulator), nameof(AfterCanStow)));
        }

        private static void AfterUseTool(bool __result)
        {
            if (_button == null) return;
            Monitor.Log($"[InputSim] pressUseToolButton -> {__result}; holding {Game1.player?.CurrentItem?.Name}, stowed={Game1.player?.netItemStowed.Value}, menu={Game1.activeClickableMenu?.GetType().Name ?? "none"}.", LogLevel.Info);
        }

        private static void AfterCanStow(bool __result)
        {
            if (_button == null) return;
            Monitor.Log($"[InputSim] CanPlayerStowItem -> {__result}.", LogLevel.Info);
        }

        /// <summary>OneSecondUpdateTicked: pick up a new script.</summary>
        internal static void Poll()
        {
            if (_path == null || !File.Exists(_path)) return;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(_path);
                File.Delete(_path);
            }
            catch (IOException ex)
            {
                Monitor.Log($"[InputSim] could not read {FileName}: {ex.Message}", LogLevel.Warn);
                return;
            }
            foreach (string raw in lines)
            {
                string[] parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                int arg = parts.Length > 1 && int.TryParse(parts[1], out int n) ? n : DefaultTicks;
                Steps.Enqueue((parts[0], arg));
            }
            Monitor.Log($"[InputSim] queued {Steps.Count} step(s).", LogLevel.Info);
        }

        /// <summary>UpdateTicked: advance the script one step when the last one is done.</summary>
        internal static void Tick()
        {
            int now = Game1.ticks;
            if (_button != null && now >= _until)
            {
                Monitor.Log($"[InputSim] released {_button}.", LogLevel.Info);
                _button = null;
            }
            if (_button != null || now < _waitUntil || Steps.Count == 0) return;

            (string what, int arg) = Steps.Dequeue();
            if (what.Equals("slot", StringComparison.OrdinalIgnoreCase))
            {
                if (Game1.player != null) Game1.player.CurrentToolIndex = arg;
                Monitor.Log($"[InputSim] slot {arg}: holding {Game1.player?.CurrentItem?.Name ?? "nothing"}, stowed={Game1.player?.netItemStowed.Value}.", LogLevel.Info);
                _waitUntil = now + DefaultTicks;
            }
            else if (what.Equals("item", StringComparison.OrdinalIgnoreCase))
            {
                // "item 128" puts one (O)128 in the first empty slot and selects it.
                Item item = ItemRegistry.Create("(O)" + arg);
                int slot = Game1.player?.Items.IndexOf(null) ?? -1;
                // A full bag: replace the selected slot (a test save is never kept).
                if (slot < 0 && Game1.player != null) slot = Game1.player.CurrentToolIndex;
                if (slot >= 0)
                {
                    Game1.player.Items[slot] = item;
                    Game1.player.CurrentToolIndex = slot;
                }
                Monitor.Log($"[InputSim] item {item.Name} into slot {slot}.", LogLevel.Info);
                _waitUntil = now + DefaultTicks;
            }
            else if (what.Equals("wait", StringComparison.OrdinalIgnoreCase))
            {
                _waitUntil = now + arg;
            }
            else if (what.Equals("state", StringComparison.OrdinalIgnoreCase))
            {
                Monitor.Log($"[InputSim] state: holding {Game1.player?.CurrentItem?.Name ?? "nothing"}, stowed={Game1.player?.netItemStowed.Value}, stowingMode={Game1.options?.stowingMode}.", LogLevel.Info);
            }
            else
            {
                _button = what;
                _until = now + arg;
                _waitUntil = _until + DefaultTicks;
                Monitor.Log($"[InputSim] pressing {what} for {arg} ticks.", LogLevel.Info);
            }
        }

        /// <summary>The raw state with the scripted button held, or the state unchanged.</summary>
        internal static GamePadState Apply(GamePadState state)
        {
            if (_button == null) return state;
            Buttons extra = _button.ToUpperInvariant() switch
            {
                "A" => Buttons.A,
                "B" => Buttons.B,
                "X" => Buttons.X,
                "Y" => Buttons.Y,
                "LB" => Buttons.LeftShoulder,
                "RB" => Buttons.RightShoulder,
                "START" => Buttons.Start,
                "BACK" => Buttons.Back,
                "UP" => Buttons.DPadUp,
                "DOWN" => Buttons.DPadDown,
                "LEFT" => Buttons.DPadLeft,
                "RIGHT" => Buttons.DPadRight,
                _ => 0,
            };
            if (extra == 0) return state;
            Buttons held = extra;
            foreach (Buttons b in Enum.GetValues(typeof(Buttons)))
                if (b != 0 && state.IsButtonDown(b)) held |= b;
            var dpad = new GamePadDPad(
                (held & Buttons.DPadUp) != 0 ? ButtonState.Pressed : ButtonState.Released,
                (held & Buttons.DPadDown) != 0 ? ButtonState.Pressed : ButtonState.Released,
                (held & Buttons.DPadLeft) != 0 ? ButtonState.Pressed : ButtonState.Released,
                (held & Buttons.DPadRight) != 0 ? ButtonState.Pressed : ButtonState.Released);
            return new GamePadState(state.ThumbSticks, state.Triggers, new GamePadButtons(held), dpad);
        }
    }
}
