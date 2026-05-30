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

        // Toolbar slot dimensions. SlotSize is now only the fallback when the vanilla
        // "Toolbar Slot Size" slider value can't be read; the live size comes from
        // Options.toolbarSlotSize via ResolveSlotSize() so the slider (#27) takes effect.
        private const int SlotSize = 64;          // fallback default if reflection fails
        private const int SlotSpacing = 4;
        private const int MinSlotSize = 32;       // vanilla slider minimum (OptionsPage id 148)
        private const int BackgroundMargin = 32;  // background box extends 16px each side
        private const int HudSafeMarginX = 130;   // reserve for the bottom-right energy/health HUD
                                                  // (~vanilla's 116 right reserve + slack for the
                                                  //  mine health bar); applied both sides to stay centered
        private const int MaxPadding = 160;       // vanilla "Toolbar Padding" slider max (OptionsPage id 134)

        /// <summary>Cached reflection accessor for Android-only Options.toolbarSlotSize field.</summary>
        private static System.Reflection.FieldInfo _toolbarSlotSizeField;

        /// <summary>Cached reflection accessor for Android-only Item._itemSlotSize field.</summary>
        private static System.Reflection.FieldInfo _itemSlotSizeField;

        /// <summary>Saved toolbarSlotSize value, restored in WateringCan postfix.</summary>
        [ThreadStatic] private static object _savedToolbarSlotSize;

        /// <summary>Cached reflection for Toolbar._itemSlotSize (Android-only).</summary>
        private static System.Reflection.FieldInfo _toolbar_itemSlotSizeField;

        /// <summary>Cached reflection for Item.drawInToolbar (Android-only) — gates scaled overlay positioning.</summary>
        private static System.Reflection.FieldInfo _drawInToolbarField;

        /// <summary>Cached reflection for Game1.toolbarPaddingX (Android-only) — drives the docked-edge gap.</summary>
        private static System.Reflection.FieldInfo _toolbarPaddingXField;

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
                _toolbar_itemSlotSizeField = AccessTools.Field(typeof(Toolbar), "_itemSlotSize");
                _drawInToolbarField = AccessTools.Field(typeof(Item), "drawInToolbar");
                _toolbarPaddingXField = AccessTools.Field(typeof(Game1), "toolbarPaddingX");

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

                // #27: honor the vanilla "Toolbar Slot Size" slider (Options.toolbarSlotSize),
                // clamped so all 12 slots still fit the screen width.
                int slotSize = ResolveSlotSize();

                // Calculate toolbar dimensions
                int toolbarWidth = (slotSize * 12) + (SlotSpacing * 11);
                int toolbarHeight = slotSize;

                // Screen edge padding (matches game's UI spacing)
                // Note: toolbar background extends 16px beyond content, so we add that
                int edgePadding = 8;
                int backgroundPadding = 16;

                // #27: optional gap between the toolbar and the screen edge it docks against,
                // driven by the vanilla "Toolbar Padding" slider (Game1.toolbarPaddingX, 0-160).
                // AC's toolbar is centered, so vanilla's horizontal padding is repurposed here.
                int edgeGap = ResolvePadding();

                // Position at bottom center of screen with padding
                int toolbarX = (Game1.uiViewport.Width - toolbarWidth) / 2;
                int toolbarY = Game1.uiViewport.Height - toolbarHeight - backgroundPadding - edgePadding - edgeGap;

                // Check if player is in bottom half - move toolbar to top if so
                bool isAtTop = player.getLocalPosition(Game1.viewport).Y > (Game1.viewport.Height / 2 + 64);
                if (isAtTop)
                {
                    toolbarY = backgroundPadding + edgePadding + 8 + edgeGap; // Extra 8 to align with date box
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
                    int slotX = toolbarX + (i * (slotSize + SlotSpacing));
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
                            slotSize + (borderPadding * 2),
                            slotSize + (borderPadding * 2),
                            Color.White,
                            1f,
                            false
                        );
                    }

                    // Draw slot background
                    b.Draw(
                        Game1.menuTexture,
                        new Rectangle(slotX, slotY, slotSize, slotSize),
                        new Rectangle(128, 128, 64, 64),
                        Color.White
                    );

                    // Draw item on top
                    if (itemIndex < player.Items.Count && player.Items[itemIndex] != null)
                    {
                        DrawSlotItem(b, player.Items[itemIndex], slotX, slotY, slotSize, isSelected);
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
        /// Resolve the live toolbar slot size from the vanilla "Toolbar Slot Size" slider
        /// (Options.toolbarSlotSize, Android-only — reflected), clamped to [MinSlotSize, fit].
        /// AC renders all 12 slots at once (vanilla scrolls), so the raw slider value is capped
        /// so the full row + background fits Game1.uiViewport.Width on the current device.
        /// </summary>
        private static int ResolveSlotSize()
        {
            int desired = SlotSize;
            if (_toolbarSlotSizeField != null)
            {
                try
                {
                    if (_toolbarSlotSizeField.GetValue(Game1.options) is int v && v > 0)
                        desired = v;
                }
                catch { }
            }

            // Cap so the CENTERED 12-slot row clears the bottom-right energy/health HUD.
            // Centered ⇒ background right edge = (uiW + bgWidth)/2; require it left of
            // (uiW - HudSafeMarginX). Reserve the same margin on both sides to stay centered.
            // This is the binding case: when the toolbar flips to the top it is left-aligned and
            // extends less far right, so clearing the energy bar here also clears the top clock.
            int gaps = SlotSpacing * 11;
            int maxFit = (Game1.uiViewport.Width - BackgroundMargin - (2 * HudSafeMarginX) - gaps) / 12;
            if (maxFit < MinSlotSize)
                maxFit = MinSlotSize;

            if (desired < MinSlotSize)
                desired = MinSlotSize;
            else if (desired > maxFit)
                desired = maxFit;

            return desired;
        }

        /// <summary>
        /// Resolve the toolbar's docked-edge gap from the vanilla "Toolbar Padding" slider
        /// (Game1.toolbarPaddingX, Android-only — reflected; range 0-160, default 0). AC's toolbar
        /// is centered, so vanilla's horizontal padding is repurposed as the gap between the toolbar
        /// and the screen edge it docks against (bottom, or top when the farmer is low on screen).
        /// </summary>
        private static int ResolvePadding()
        {
            if (_toolbarPaddingXField == null)
                return 0;
            try
            {
                if (_toolbarPaddingXField.GetValue(null) is int v)
                    return Math.Max(0, Math.Min(MaxPadding, v));
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Draw a single toolbar item scaled to <paramref name="slotSize"/>. drawInMenu centers
        /// the icon at location+(32,32) and the overlay icons (stack count / quality / gauge) read
        /// Item.itemSlotSize + drawInToolbar — both assume a 64px slot. We set them to the live
        /// slot size (mirroring vanilla Toolbar.draw) and offset the location so everything stays
        /// centered as the slot grows. All Android-only members are reflected (PC DLL compile-safe).
        /// </summary>
        private static void DrawSlotItem(SpriteBatch b, Item item, int slotX, int slotY, int slotSize, bool isSelected)
        {
            // Preserve AC's current proportions: at 64px this matches the old 1f / 0.8f scales.
            float iconScale = ((float)slotSize / SlotSize) * (isSelected ? 1f : 0.8f);

            object savedItemSlotSize = null;
            bool slotSet = false;
            if (_itemSlotSizeField != null)
            {
                try
                {
                    savedItemSlotSize = _itemSlotSizeField.GetValue(item);
                    _itemSlotSizeField.SetValue(item, slotSize);
                    slotSet = true;
                }
                catch { }
            }

            if (_drawInToolbarField != null)
            {
                try { _drawInToolbarField.SetValue(item, true); } catch { }
            }

            try
            {
                item.drawInMenu(
                    b,
                    new Vector2(slotX + (slotSize / 2f) - 32f, slotY + (slotSize / 2f) - 32f),
                    iconScale,
                    1f,
                    0.9f,
                    StackDrawType.Draw,
                    Color.White,
                    true
                );
            }
            finally
            {
                if (_drawInToolbarField != null)
                {
                    try { _drawInToolbarField.SetValue(item, false); } catch { }
                }
                if (slotSet)
                {
                    try { _itemSlotSizeField.SetValue(item, savedItemSlotSize); } catch { }
                }
            }
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
