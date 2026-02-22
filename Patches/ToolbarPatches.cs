using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace AndroidConsolizer.Patches
{
    /// <summary>Harmony patches to replace Android's scrolling toolbar with a fixed 12-slot console-style toolbar.</summary>
    internal static class ToolbarPatches
    {
        private static IMonitor Monitor;

        // Toolbar slot dimensions (scaled)
        private const int SlotSize = 64;
        private const int SlotSpacing = 4;

        /// <summary>Cached reflection accessor for Android-only Options.toolbarSlotSize field.</summary>
        private static System.Reflection.FieldInfo _toolbarSlotSizeField;

        /// <summary>Cached reflection accessor for Android-only Item._itemSlotSize field.</summary>
        private static System.Reflection.FieldInfo _itemSlotSizeField;

        /// <summary>Saved toolbarSlotSize value, restored in WateringCan postfix.</summary>
        [ThreadStatic] private static object _savedToolbarSlotSize;

        /// <summary>Cached reflection for Toolbar._toolbarPaddingX (Android-only).</summary>
        private static System.Reflection.FieldInfo _toolbar_toolbarPaddingXField;

        /// <summary>Cached reflection for Game1.toolbarPaddingX (Android-only).</summary>
        private static System.Reflection.FieldInfo _game1_toolbarPaddingXField;

        /// <summary>Cached reflection for Game1.maxItemSlotSize (Android-only).</summary>
        private static System.Reflection.FieldInfo _maxItemSlotSizeField;

        /// <summary>Cached reflection for Toolbar._itemSlotSize (Android-only).</summary>
        private static System.Reflection.FieldInfo _toolbar_itemSlotSizeField;

        /// <summary>Cached reflection for Toolbar.resetToolbar() (Android-only).</summary>
        private static System.Reflection.MethodInfo _resetToolbarMethod;

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                // Completely replace Toolbar.draw with our own implementation
                harmony.Patch(
                    original: AccessTools.Method(typeof(Toolbar), nameof(Toolbar.draw), new Type[] { typeof(SpriteBatch) }),
                    prefix: new HarmonyMethod(typeof(ToolbarPatches), nameof(Toolbar_Draw_Prefix))
                );

                // Cache reflection accessors for Android-only fields
                _toolbarSlotSizeField = AccessTools.Field(typeof(StardewValley.Options), "toolbarSlotSize");
                _itemSlotSizeField = AccessTools.Field(typeof(Item), "_itemSlotSize");
                _toolbar_toolbarPaddingXField = AccessTools.Field(typeof(Toolbar), "_toolbarPaddingX");
                _game1_toolbarPaddingXField = AccessTools.Field(typeof(Game1), "toolbarPaddingX");
                _maxItemSlotSizeField = AccessTools.Field(typeof(Game1), "maxItemSlotSize");
                _toolbar_itemSlotSizeField = AccessTools.Field(typeof(Toolbar), "_itemSlotSize");
                _resetToolbarMethod = AccessTools.Method(typeof(Toolbar), "resetToolbar");

                // Patch WateringCan.drawInMenu to fix water gauge position in ALL contexts.
                // The gauge formula uses toolbarSlotSize (a user preference, e.g. 200) which
                // produces a large downward offset. We temporarily set it to the item's actual
                // slot size so the gauge renders inside the icon regardless of context.
                if (_toolbarSlotSizeField != null)
                {
                    harmony.Patch(
                        original: AccessTools.Method(typeof(WateringCan), nameof(WateringCan.drawInMenu),
                            new Type[] { typeof(SpriteBatch), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(StackDrawType), typeof(Color), typeof(bool) }),
                        prefix: new HarmonyMethod(typeof(ToolbarPatches), nameof(WateringCan_drawInMenu_Prefix)),
                        postfix: new HarmonyMethod(typeof(ToolbarPatches), nameof(WateringCan_drawInMenu_Postfix))
                    );
                }

                Monitor.Log("Toolbar patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply Toolbar patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Prefix that completely replaces the toolbar drawing with our own 12-slot version.
        /// </summary>
        /// <returns>False to skip the original method entirely.</returns>
        private static bool Toolbar_Draw_Prefix(Toolbar __instance, SpriteBatch b)
        {
            try
            {
                // If feature is disabled, let original run
                if (!(ModEntry.Config?.EnableConsoleToolbar ?? false))
                    return true;

                // Our toolbar is horizontal (bottom of screen), not vertical. But
                // DialogueBox.GetWidth() reads Toolbar.Instance.itemSlotSize and
                // subtracts (toolbarPaddingX + itemSlotSize + 28) * 2 from viewport
                // width for vertical toolbar space. With itemSlotSize=200 on small
                // screens, portrait dialogue gets ~1px for text. Set to 0 so the
                // game knows our toolbar takes no horizontal/vertical side space.
                if (_toolbar_itemSlotSizeField != null)
                {
                    try { _toolbar_itemSlotSizeField.SetValue(__instance, 0); }
                    catch { }
                }

                var player = Game1.player;
                if (player == null)
                    return true;

                // Don't draw during events or when game says not to
                if (Game1.activeClickableMenu != null)
                    return false;

                // Calculate current row info
                int currentRow = FarmerPatches.CurrentToolbarRow;
                int rowStart = currentRow * 12;

                // Calculate toolbar dimensions
                int toolbarWidth = (SlotSize * 12) + (SlotSpacing * 11);
                int toolbarHeight = SlotSize;

                // Screen edge padding (matches game's UI spacing)
                // Note: toolbar background extends 16px beyond content, so we add that
                int edgePadding = 8;
                int backgroundPadding = 16;

                // Position at bottom center of screen with padding
                int toolbarX = (Game1.uiViewport.Width - toolbarWidth) / 2;
                int toolbarY = Game1.uiViewport.Height - toolbarHeight - backgroundPadding - edgePadding;

                // Check if player is in bottom half - move toolbar to top if so
                bool isAtTop = player.getLocalPosition(Game1.viewport).Y > (Game1.viewport.Height / 2 + 64);
                if (isAtTop)
                {
                    toolbarY = backgroundPadding + edgePadding + 8; // Extra 8 to align with date box
                    // Shift left to avoid date/time display in top right, with padding
                    toolbarX = backgroundPadding + edgePadding;
                }

                // Draw toolbar background
                IClickableMenu.drawTextureBox(
                    b,
                    Game1.menuTexture,
                    new Rectangle(0, 256, 60, 60),
                    toolbarX - 16,
                    toolbarY - 16,
                    toolbarWidth + 32,
                    toolbarHeight + 32,
                    Color.White,
                    1f,
                    false
                );

                // Draw each slot
                for (int i = 0; i < 12; i++)
                {
                    int itemIndex = rowStart + i;
                    int slotX = toolbarX + (i * (SlotSize + SlotSpacing));
                    int slotY = toolbarY;
                    bool isSelected = player.CurrentToolIndex == itemIndex;

                    // Draw selection highlight FIRST (behind item), slightly larger than slot
                    if (isSelected)
                    {
                        int borderPadding = 4;
                        IClickableMenu.drawTextureBox(
                            b,
                            Game1.menuTexture,
                            new Rectangle(0, 256, 60, 60),
                            slotX - borderPadding,
                            slotY - borderPadding,
                            SlotSize + (borderPadding * 2),
                            SlotSize + (borderPadding * 2),
                            Color.White,
                            1f,
                            false
                        );
                    }

                    // Draw slot background
                    b.Draw(
                        Game1.menuTexture,
                        new Rectangle(slotX, slotY, SlotSize, SlotSize),
                        new Rectangle(128, 128, 64, 64),
                        Color.White
                    );

                    // Draw item on top
                    if (itemIndex < player.Items.Count && player.Items[itemIndex] != null)
                    {
                        var item = player.Items[itemIndex];
                        item.drawInMenu(
                            b,
                            new Vector2(slotX, slotY),
                            isSelected ? 1f : 0.8f,
                            1f,
                            0.9f,
                            StackDrawType.Draw,
                            Color.White,
                            true
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"Error in custom toolbar draw: {ex.Message}", LogLevel.Error);
                return true; // Fall back to original on error
            }

            return false; // Skip original Toolbar.draw
        }

        /// <summary>
        /// Prefix for WateringCan.drawInMenu — temporarily sets toolbarSlotSize to the
        /// item's actual slot size so the water gauge offset formula produces the correct
        /// result for the current drawing context (toolbar, inventory, or chest).
        /// </summary>
        private static void WateringCan_drawInMenu_Prefix(WateringCan __instance)
        {
            try
            {
                _savedToolbarSlotSize = _toolbarSlotSizeField.GetValue(Game1.options);
                // Read item's slot size via reflection (Android-only field, defaults to 64 when -1)
                int slotSize = 64;
                if (_itemSlotSizeField != null)
                {
                    object raw = _itemSlotSizeField.GetValue(__instance);
                    if (raw is int val && val > 0)
                        slotSize = val;
                }
                _toolbarSlotSizeField.SetValue(Game1.options, slotSize);
            }
            catch { }
        }

        /// <summary>Postfix for WateringCan.drawInMenu — restores original toolbarSlotSize.</summary>
        private static void WateringCan_drawInMenu_Postfix()
        {
            try
            {
                if (_savedToolbarSlotSize != null)
                {
                    _toolbarSlotSizeField.SetValue(Game1.options, _savedToolbarSlotSize);
                    _savedToolbarSlotSize = null;
                }
            }
            catch { }
        }
    }
}
