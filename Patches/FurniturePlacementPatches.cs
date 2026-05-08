using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Replaces the multi-tile green/red placement map with a single ghost rectangle
    /// drawn at the furniture's actual landing tile, sized to the furniture's tile
    /// footprint. The engine's <see cref="StardewValley.Object.DrawRedGreenRectangleForPlacing"/>
    /// already does this for tap/touch placement on mobile, gated by weaponControl
    /// values 2/3/4/8. On Android with a controller weaponControl is typically 0, so the
    /// gate fails and the engine falls back to the multi-tile per-top-left-corner map —
    /// which is misleading for multi-tile furniture like beds (a green tile means "the
    /// top-left corner can land here," not "the bed will visibly cover this tile").
    ///
    /// Our prefix forces the single-rectangle path for Furniture instances, returning
    /// true so <see cref="StardewValley.Object.drawPlacementBounds"/> short-circuits
    /// before drawing the multi-tile map.
    /// </summary>
    internal static class FurniturePlacementPatches
    {
        private static IMonitor Monitor;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                // Mobile-only method: not present in the PC StardewValley.dll the project compiles
                // against, so we resolve by string at runtime against the Android assembly.
                var method = AccessTools.Method(typeof(SObject), "DrawRedGreenRectangleForPlacing");
                if (method == null)
                {
                    Monitor.Log("[FurniturePlacement] Object.DrawRedGreenRectangleForPlacing not found on this platform — patch NOT attached. (Expected on PC; OK for Android.)", LogLevel.Warn);
                    return;
                }
                harmony.Patch(
                    original: method,
                    prefix: new HarmonyMethod(typeof(FurniturePlacementPatches), nameof(DrawRedGreenRectangleForPlacing_Prefix))
                );
                Monitor.Log("Furniture placement (single ghost) patch attached.", LogLevel.Trace);
            }
            catch (System.Exception ex)
            {
                Monitor.Log($"Failed to attach furniture placement patch: {ex.Message}", LogLevel.Error);
            }
        }

        private static bool DrawRedGreenRectangleForPlacing_Prefix(
            SObject __instance,
            SpriteBatch spriteBatch,
            GameLocation location,
            ref bool __result)
        {
            if (ModEntry.Config?.EnableConsoleFurniturePlacement != true)
                return true;
            if (!(__instance is Furniture furniture))
                return true;
            if (location == null || spriteBatch == null)
                return true;

            // drawPlacementBounds has already computed and assigned TileLocation to the
            // snapped placement target by the time it calls DrawRedGreenRectangleForPlacing,
            // so reading TileLocation gives us the same tile the engine would place at.
            Vector2 tile = __instance.TileLocation;
            int x = (int)tile.X * 64;
            int y = (int)tile.Y * 64;
            bool canPlace = Utility.playerCanPlaceItemHere(location, __instance, x, y, Game1.player);

            int width = furniture.getTilesWide();
            int height = furniture.getTilesHigh();
            int srcX = canPlace ? 194 : 210;

            for (int i = (int)tile.X; i < (int)tile.X + width; i++)
            {
                for (int j = (int)tile.Y; j < (int)tile.Y + height; j++)
                {
                    spriteBatch.Draw(
                        Game1.mouseCursors,
                        new Vector2(i * 64 - Game1.viewport.X, j * 64 - Game1.viewport.Y),
                        new Microsoft.Xna.Framework.Rectangle(srcX, 388, 16, 16),
                        Color.White,
                        0f,
                        Vector2.Zero,
                        4f,
                        SpriteEffects.None,
                        0.01f
                    );
                }
            }

            // Translucent furniture sprite over the colored squares — matches the console
            // ghost (and the carpenter building ghost) so the user sees what they're about
            // to place, with the validity highlight showing through.
            try
            {
                furniture.draw(spriteBatch, (int)tile.X, (int)tile.Y, 0.5f);
            }
            catch
            {
                // Failsafe: never let a draw exception break placement preview rendering.
            }

            Game1.isCheckingNonMousePlacement = false;
            __result = true;
            return false;
        }
    }
}
