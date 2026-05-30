using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Netcode;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using xTile.Tiles;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// #47 TRANSIENT DIAGNOSTIC (strip once root cause is confirmed on device).
    ///
    /// Public bug: the Community Center "missed rewards" chest never appears on Android after a
    /// room is completed with uncollected bundle rewards. Reading the Android decompile, the entire
    /// feature funnels through ONE map tile:
    ///   checkForMissedRewards() (state) -> showMissedRewardsChestEvent.Fire(flag)
    ///   -> doShowMissedRewardsChest(true) -> setMapTile(22,10,"Buildings","indoors2","MissedRewards")
    /// No tile = no sprite AND no interaction. So the break is in one of two links, which code alone
    /// can't tell apart:
    ///   Q1 STATE  — after checkForMissedRewards runs, did missedRewardsChestVisible go TRUE and did
    ///               missedRewardsChest populate? (true + items => state is fine, the bug is rendering;
    ///               false while rewards are still pending => the area-complete/condition logic is the bug)
    ///   Q2 RENDER — when doShowMissedRewardsChest(true) runs, does the (22,10) "Buildings" tile actually
    ///               land, and does the CC map on Android even HAVE the "indoors2" tilesheet setMapTile
    ///               references? (a missing tilesheet would make setMapTile a silent no-op)
    ///
    /// Log-only postfixes on private, NON-input methods (safe to patch — these are location-logic
    /// methods, not releaseLeftClick-class input overrides that SIGSEGV the Android runtime). All CC
    /// net fields are reflected for PC-DLL compile-safety; map/tilesheet reads use the shared xTile API.
    /// </summary>
    internal static class MissedRewardsDiagnosticPatches
    {
        private const string TAG = "[MissedRewards/diag]";
        private const int ChestTileX = 22;
        private const int ChestTileY = 10;

        private static IMonitor Monitor;
        private static FieldInfo _visibleField;  // CommunityCenter.missedRewardsChestVisible (NetBool)
        private static FieldInfo _chestField;    // CommunityCenter.missedRewardsChest (NetRef<Chest>)

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            _visibleField = AccessTools.Field(typeof(CommunityCenter), "missedRewardsChestVisible");
            _chestField = AccessTools.Field(typeof(CommunityCenter), "missedRewardsChest");

            try
            {
                var check = AccessTools.Method(typeof(CommunityCenter), "checkForMissedRewards");
                if (check != null)
                {
                    harmony.Patch(check,
                        postfix: new HarmonyMethod(typeof(MissedRewardsDiagnosticPatches), nameof(CheckForMissedRewards_Postfix)));
                }
                else
                {
                    monitor.Log($"{TAG} checkForMissedRewards not found — hook skipped.", LogLevel.Warn);
                }

                var show = AccessTools.Method(typeof(CommunityCenter), "doShowMissedRewardsChest", new Type[] { typeof(bool) });
                if (show != null)
                {
                    harmony.Patch(show,
                        postfix: new HarmonyMethod(typeof(MissedRewardsDiagnosticPatches), nameof(DoShowMissedRewardsChest_Postfix)));
                }
                else
                {
                    monitor.Log($"{TAG} doShowMissedRewardsChest(bool) not found — hook skipped.", LogLevel.Warn);
                }

                monitor.Log($"{TAG} instrumentation applied (checkForMissedRewards + doShowMissedRewardsChest).", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                monitor.Log($"{TAG} failed to apply instrumentation: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Q1: did the state machine decide the chest should be visible, and did it fill the chest?
        /// Also logs the raw pending-reward indices for context.
        /// </summary>
        private static void CheckForMissedRewards_Postfix(CommunityCenter __instance)
        {
            try
            {
                bool visible = false;
                if (_visibleField?.GetValue(__instance) is NetBool nb)
                    visible = nb.Value;

                int chestItems = -1;
                if (_chestField?.GetValue(__instance) is NetRef<Chest> nr && nr.Value != null)
                    chestItems = nr.Value.Items.Count;

                Monitor.Log(
                    $"{TAG} checkForMissedRewards ran: missedRewardsChestVisible={visible} chestItems={chestItems} | {DescribePendingRewards()}",
                    LogLevel.Info);
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"{TAG} check postfix failed: {ex.Message}", LogLevel.Warn); } catch { }
            }
        }

        /// <summary>
        /// Q2: when asked to show the chest, did the (22,10) Buildings tile actually land, and does the
        /// map carry the "indoors2" tilesheet that setMapTile references? Runs as a postfix so it reads
        /// the tile state AFTER setMapTile/removeMapTile have run.
        /// </summary>
        private static void DoShowMissedRewardsChest_Postfix(CommunityCenter __instance, bool isVisible)
        {
            try
            {
                var map = __instance.Map;
                string tileInfo = "<no map>";
                string sheets = "<no map>";
                bool hasIndoors2 = false;

                if (map != null)
                {
                    var layer = map.GetLayer("Buildings");
                    if (layer == null)
                    {
                        tileInfo = "no Buildings layer";
                    }
                    else
                    {
                        Tile t = layer.Tiles[ChestTileX, ChestTileY];
                        tileInfo = (t == null)
                            ? "tile=NULL (not placed)"
                            : $"tile index={t.TileIndex} sheet={t.TileSheet?.Id}";
                    }

                    var sb = new StringBuilder();
                    foreach (TileSheet ts in map.TileSheets)
                    {
                        sb.Append(ts.Id).Append(' ');
                        if (string.Equals(ts.Id, "indoors2", StringComparison.OrdinalIgnoreCase))
                            hasIndoors2 = true;
                    }
                    sheets = sb.ToString().Trim();
                }

                Monitor.Log(
                    $"{TAG} doShowMissedRewardsChest(isVisible={isVisible}) → (22,10) {tileInfo} | hasIndoors2={hasIndoors2} | sheets=[{sheets}]",
                    LogLevel.Info);
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"{TAG} show postfix failed: {ex.Message}", LogLevel.Warn); } catch { }
            }
        }

        /// <summary>List the bundle indices whose reward is still pending (mirrors checkForMissedRewards' source).</summary>
        private static string DescribePendingRewards()
        {
            try
            {
                var bundleRewards = Game1.netWorldState?.Value?.BundleRewards;
                if (bundleRewards == null)
                    return "pendingRewards=<null>";

                var sb = new StringBuilder("pendingRewards=[");
                foreach (int key in bundleRewards.Keys)
                {
                    if (bundleRewards[key])
                        sb.Append(key).Append(' ');
                }
                return sb.ToString().TrimEnd() + "]";
            }
            catch (Exception ex)
            {
                return $"pendingRewards=<read failed: {ex.Message}>";
            }
        }
    }
}
