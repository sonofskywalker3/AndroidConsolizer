using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Console parity for Marnie's animal sell/move flow (AnimalQueryMenu).
    ///
    /// Root cause of "can't sell animals with the controller" (Nexus, NightMareBalon):
    /// the Android AnimalQueryMenu ships its OWN mobile receiveGamePadButton handler that
    /// drives a private `_selectedButton` enum (None/Move/Sell/AllowReproduction/Cancel/Tick)
    /// instead of the standard snap system the console build uses. Two defects make selling
    /// impossible-feeling with a controller:
    ///   1. The menu opens with _selectedButton = None, so nothing is highlighted.
    ///   2. OnClickSell() opens the "Sell for Xg?" confirm dialog but leaves _selectedButton
    ///      at Sell. In the confirm branch, A does `if (_selectedButton == Tick) OnClickYes()
    ///      else OnClickNo()` — so navigate-to-Sell + A (opens confirm) + A (expecting to
    ///      confirm) lands on OnClickNo() and CANCELS. Pressing A then A never sells; the user
    ///      would have to discover that a stray DPad-left/right is required to reach "Yes".
    ///
    /// Fix (reflection-only — all of these members are Android-runtime-only and absent from
    /// the PC DLL AC compiles against, per the documented Android-vs-PC pattern):
    ///   - On menu open, set _selectedButton = Move so a button is highlighted immediately.
    ///   - After OnClickSell, set _selectedButton = Tick so the confirm dialog opens with
    ///     "Yes" highlighted and a second A confirms the sale (the user already deliberately
    ///     navigated to Sell and pressed A to open the confirm). DPad-left/right still moves to
    ///     "No" to back out.
    /// Both are gated on the controller being active so pure-touch UX is unchanged.
    /// </summary>
    internal static class AnimalQueryMenuPatches
    {
        private static IMonitor Monitor;

        // AnimalQueryMenu.Button enum ordinals (decompile: None,Move,Sell,AllowReproduction,Cancel,Tick).
        private const int Button_Move = 1;
        private const int Button_Tick = 5;

        // Android-only members — resolved off the runtime type via reflection.
        private static FieldInfo _selectedButtonField;
        private static FieldInfo _confirmingSellField;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                _selectedButtonField = AccessTools.Field(typeof(AnimalQueryMenu), "_selectedButton");
                _confirmingSellField = AccessTools.Field(typeof(AnimalQueryMenu), "confirmingSell");

                if (_selectedButtonField == null)
                {
                    // PC DLL build (no mobile handler) — nothing to patch. Safe no-op.
                    Monitor.Log("AnimalQueryMenu has no _selectedButton field (non-Android runtime?) — skipping patches.", LogLevel.Trace);
                    return;
                }

                var ctor = AccessTools.Constructor(typeof(AnimalQueryMenu), new[] { typeof(FarmAnimal) });
                if (ctor != null)
                {
                    harmony.Patch(
                        original: ctor,
                        postfix: new HarmonyMethod(typeof(AnimalQueryMenuPatches), nameof(Ctor_Postfix))
                    );
                }

                var onClickSell = AccessTools.Method(typeof(AnimalQueryMenu), "OnClickSell");
                if (onClickSell != null)
                {
                    harmony.Patch(
                        original: onClickSell,
                        postfix: new HarmonyMethod(typeof(AnimalQueryMenuPatches), nameof(OnClickSell_Postfix))
                    );
                }

                var receiveGamePad = AccessTools.Method(typeof(AnimalQueryMenu), nameof(AnimalQueryMenu.receiveGamePadButton));
                if (receiveGamePad != null)
                {
                    harmony.Patch(
                        original: receiveGamePad,
                        prefix: new HarmonyMethod(typeof(AnimalQueryMenuPatches), nameof(ReceiveGamePadButton_Prefix))
                    );
                }

                Monitor.Log("AnimalQueryMenu patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply AnimalQueryMenu patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>True when the mod's console animal-menu handling should act.</summary>
        private static bool Active()
            => (ModEntry.Config?.EnableConsoleAnimalMenu ?? true) && Game1.options.gamepadControls;

        private static void SetSelectedButton(AnimalQueryMenu menu, int value)
        {
            // The field type is the private nested Button enum — convert the ordinal to it.
            _selectedButtonField.SetValue(menu, Enum.ToObject(_selectedButtonField.FieldType, value));
        }

        /// <summary>Give the menu an initial highlight so the controller has a visible selection
        /// the moment it opens (vanilla mobile leaves _selectedButton = None).</summary>
        private static void Ctor_Postfix(AnimalQueryMenu __instance)
        {
            try
            {
                if (!Active()) return;
                SetSelectedButton(__instance, Button_Move);
            }
            catch (Exception ex)
            {
                Monitor.Log($"AnimalQueryMenu Ctor_Postfix error: {ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>After Sell opens the confirm dialog, default the highlight to "Yes" so a
        /// second A confirms the sale (vanilla mobile leaves it at Sell, so A → OnClickNo →
        /// cancel, which reads as "can't sell").</summary>
        private static void OnClickSell_Postfix(AnimalQueryMenu __instance)
        {
            try
            {
                if (!Active()) return;
                SetSelectedButton(__instance, Button_Tick);
            }
            catch (Exception ex)
            {
                Monitor.Log($"AnimalQueryMenu OnClickSell_Postfix error: {ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>Diagnostic only (VerboseLogging): record what reaches the menu and the
        /// selection state, so a single device test confirms the controller path is live and
        /// the highlight defaults are taking effect. Never alters behavior.</summary>
        private static void ReceiveGamePadButton_Prefix(AnimalQueryMenu __instance, Buttons b)
        {
            try
            {
                if (!(ModEntry.Config?.VerboseLogging ?? false)) return;
                object sel = _selectedButtonField?.GetValue(__instance);
                object confirming = _confirmingSellField?.GetValue(__instance);
                Monitor.Log($"[AnimalQuery] button={b} selected={sel} confirmingSell={confirming}", LogLevel.Debug);
            }
            catch { /* diagnostic must never break the menu */ }
        }
    }
}
