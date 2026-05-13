using System;
using System.Collections.Generic;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace AndroidConsolizer.Patches
{
    /// <summary>Harmony patches for Farmer to fix Android trigger toolbar navigation.</summary>
    internal static class FarmerPatches
    {
        private static IMonitor Monitor;

        /// <summary>The current toolbar row, synchronized from ModEntry.</summary>
        internal static int CurrentToolbarRow = 0;

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                // Patch the CurrentToolIndex property setter to intercept Android's broken trigger handling
                var setter = AccessTools.PropertySetter(typeof(Farmer), nameof(Farmer.CurrentToolIndex));
                if (setter == null)
                {
                    Monitor.Log("Could not find Farmer.CurrentToolIndex setter!", LogLevel.Error);
                    return;
                }

                harmony.Patch(
                    original: setter,
                    prefix: new HarmonyMethod(typeof(FarmerPatches), nameof(CurrentToolIndex_Prefix))
                );

                // Patch Farmer.addItemToInventory(Item, List<Item>) to steer new items into the
                // active toolbar row when there's space. Vanilla scans 0..maxItems for the first
                // null, so pickups always pile into row 0 regardless of the player's active row.
                var addItem = AccessTools.Method(
                    typeof(Farmer),
                    nameof(Farmer.addItemToInventory),
                    new[] { typeof(Item), typeof(List<Item>) }
                );
                if (addItem == null)
                {
                    Monitor.Log("Farmer.addItemToInventory(Item, List<Item>) not found — pickup-to-active-row not attached.", LogLevel.Warn);
                }
                else
                {
                    harmony.Patch(
                        original: addItem,
                        postfix: new HarmonyMethod(typeof(FarmerPatches), nameof(AddItemToInventory_Postfix))
                    );
                }

                Monitor.Log("Farmer patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply Farmer patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Prefix for Farmer.CurrentToolIndex setter.
        /// Intercepts and fixes Android's native trigger handler which is hardcoded to cycle 0-9.
        /// </summary>
        /// <param name="__instance">The Farmer instance.</param>
        /// <param name="value">The new tool index value (passed by ref so we can modify it).</param>
        /// <returns>True to continue with the (possibly modified) value.</returns>
        private static bool CurrentToolIndex_Prefix(Farmer __instance, ref int value)
        {
            try
            {
                // Only apply during gameplay for main player
                if (Game1.activeClickableMenu != null || !Context.IsPlayerFree)
                    return true;
                if (__instance != Game1.player)
                    return true;
                if (!(ModEntry.Config?.EnableConsoleToolbar ?? false))
                    return true;

                int oldValue = __instance.CurrentToolIndex;
                int currentRow = CurrentToolbarRow;
                int rowStart = currentRow * 12;
                int rowEnd = rowStart + 11;

                // FIX #1: Handle negative values (LT at slot 0 goes to -1)
                if (value < 0)
                {
                    int newValue = rowEnd; // Wrap to end of current row
                    if (ModEntry.Config?.VerboseLogging ?? false)
                    {
                        Monitor.Log($"Setter intercept: Negative wrap fix {value} -> {newValue}", LogLevel.Debug);
                    }
                    value = newValue;
                }
                // FIX #2: If value is 0-9 but we're on row 1+, remap to current row
                // Android's native trigger handler only cycles 0-9, ignoring the expanded toolbar
                else if (currentRow > 0 && value >= 0 && value <= 9)
                {
                    int newValue = rowStart + value;
                    if (ModEntry.Config?.VerboseLogging ?? false)
                    {
                        Monitor.Log($"Setter intercept (row {currentRow}): Remapping {value} -> {newValue}", LogLevel.Debug);
                    }
                    value = newValue;
                }
                // FIX #3: On row 0, fix wrap patterns (Android cycles 0-9 but we have 12 slots)
                else if (currentRow == 0)
                {
                    int oldPos = oldValue % 12;

                    // RT at slot 9: native goes 9->0, should go 9->10
                    if (oldPos == 9 && value == 0)
                    {
                        if (ModEntry.Config?.VerboseLogging ?? false)
                        {
                            Monitor.Log($"Setter intercept (row 0): RT wrap fix {value} -> 10", LogLevel.Debug);
                        }
                        value = 10;
                    }
                    // LT at slot 0: native goes 0->9, should go 0->11
                    else if (oldPos == 0 && value == 9)
                    {
                        if (ModEntry.Config?.VerboseLogging ?? false)
                        {
                            Monitor.Log($"Setter intercept (row 0): LT wrap fix {value} -> 11", LogLevel.Debug);
                        }
                        value = 11;
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error in CurrentToolIndex prefix: {ex.Message}", LogLevel.Error);
            }

            return true; // Continue with (possibly modified) value
        }

        /// <summary>
        /// Postfix for Farmer.addItemToInventory(Item, List&lt;Item&gt;) — steer non-furniture
        /// pickups (forage, drops, gifts, shop purchases, etc.) into the active toolbar row
        /// when there's an open slot. Vanilla scans from index 0, so without this patch every
        /// pickup piles into row 0 regardless of which row the player is currently using.
        ///
        /// Unlike the furniture path (CarpenterMenuPatches.RemoveQueuedFurniture_Postfix), this
        /// NEVER touches CurrentToolIndex or the visible row — the player keeps whatever tool
        /// they have selected. When the active row is full, the item stays wherever vanilla
        /// placed it (no row switch).
        ///
        /// Stack-merge case: if the item was fully merged into an existing stack, the item
        /// reference won't be findable in player.Items and the postfix no-ops.
        /// </summary>
        private static void AddItemToInventory_Postfix(Farmer __instance, Item item)
        {
            try
            {
                if (ModEntry.Config?.EnablePickupToActiveRow != true) return;
                if (__instance == null || __instance != Game1.player) return;
                if (item == null) return;
                var items = __instance.Items;
                if (items == null) return;
                int landedAt = items.IndexOf(item);
                if (landedAt < 0) return; // stack-merged or rejected
                int row = CurrentToolbarRow;
                int rowStart = row * 12;
                int rowEnd = rowStart + 12;
                if (landedAt >= rowStart && landedAt < rowEnd) return; // already in active row
                if (rowEnd > items.Count) return; // safety: row beyond inventory bounds
                for (int i = rowStart; i < rowEnd; i++)
                {
                    if (items[i] == null)
                    {
                        items[i] = item;
                        items[landedAt] = null;
                        return; // CurrentToolIndex intentionally untouched
                    }
                }
                // Active row full → leave item wherever vanilla put it.
            }
            catch (Exception ex)
            {
                Monitor?.Log($"AddItemToInventory_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
