using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// #69 — Hold A to craft continuously (console parity), mirroring hold-to-buy/sell
    /// (<see cref="ShopMenuPatches"/>) and the hold-Y single-stack transfer
    /// (<see cref="InventoryManagementPatches"/>). Android's CraftingPage already crafts
    /// one per A press and has a quantity slider, but the slider is only adjustable by
    /// touch — there's no controller way to rapid-craft. We poll A in a CraftingPage.update
    /// postfix and re-fire the craft at a fixed cadence while held.
    ///
    /// Safe by construction: each repeat calls the page's own receiveGamePadButton(A),
    /// which only crafts when showCraftButton is true. After every craft CraftSelectedRecipe
    /// re-checks ingredients + inventory space and clears showCraftButton when exhausted
    /// (decompile CraftingPage.cs:612-644), so the hold self-limits and can't over-craft.
    /// GameMenu.update forwards to pages[currentTab].update (GameMenu.cs:378), so this
    /// covers both the crafting tab and the standalone cooking menu.
    /// </summary>
    internal static class CraftingPagePatches
    {
        private static IMonitor Monitor;

        // CraftingPage.showCraftButton is private — gate the repeat on it so we only
        // re-fire when a craft would actually happen (avoids the nav fall-through that
        // receiveGamePadButton runs when the craft button is inactive).
        private static FieldInfo _showCraftButtonField;

        // Match the hold-Y single-stack transfer cadence the user already likes
        // (InventoryManagementPatches: delay 15, rate 8).
        private const int HoldDelay = 15;   // ~250ms at 60fps before auto-repeat starts
        private const int RepeatRate = 8;   // ~133ms between crafts (~7.5/sec)

        private static int _aHeldTicks;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                _showCraftButtonField = AccessTools.Field(typeof(CraftingPage), "showCraftButton");
                harmony.Patch(
                    original: AccessTools.Method(typeof(CraftingPage), nameof(CraftingPage.update), new[] { typeof(GameTime) }),
                    postfix: new HarmonyMethod(typeof(CraftingPagePatches), nameof(Update_Postfix))
                );
                monitor.Log($"Hold-to-craft patch attached (showCraftButton={(_showCraftButtonField != null ? "OK" : "NULL")}).", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                monitor.Log($"Failed to attach hold-to-craft patch: {ex.Message}", LogLevel.Error);
            }
        }

        private static void Update_Postfix(CraftingPage __instance)
        {
            if (ModEntry.Config?.EnableHoldToCraft != true)
            {
                _aHeldTicks = 0;
                return;
            }

            // GamePad.GetState already reflects AC's A/B menu swap (applied in a GetState
            // postfix), so Buttons.A here is the same logical button that triggers the
            // page's craft action — consistent with the existing hold-Y / hold-buy polls.
            bool aDown = GamePad.GetState(PlayerIndex.One).Buttons.A == ButtonState.Pressed;
            if (!aDown)
            {
                _aHeldTicks = 0;
                return;
            }

            _aHeldTicks++;

            // The first craft is the game's own edge-triggered A press; we only add the
            // auto-repeat after the initial hold delay, then every RepeatRate ticks.
            if (_aHeldTicks <= HoldDelay || (_aHeldTicks - HoldDelay) % RepeatRate != 0)
                return;

            // Only re-fire when the craft button is live (ingredients + inventory space).
            // receiveGamePadButton(A) also self-gates on showCraftButton, so this is belt-
            // and-suspenders that additionally avoids the harmless nav fall-through.
            bool active = _showCraftButtonField == null
                || (_showCraftButtonField.GetValue(__instance) as bool? ?? false);
            if (!active)
                return;

            try
            {
                __instance.receiveGamePadButton(Buttons.A);
            }
            catch (Exception ex)
            {
                if (ModEntry.Config?.VerboseLogging == true)
                    Monitor?.Log($"[HoldToCraft] repeat craft error: {ex.Message}", LogLevel.Warn);
            }
        }
    }
}
