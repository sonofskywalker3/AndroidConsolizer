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
    /// DIAGNOSTIC for #71 — Nexus report (2026-05-28): "collected the Dwarf
    /// Language Translation Manual with the controller, got a book, not a skill."
    ///
    /// Root-cause hypothesis (from decompile): the Dwarvish Translation Guide is
    /// (O)326. The museum-reward ItemGrabMenu has a special grab branch
    /// (decompile ItemGrabMenu.cs:830-841) that, when (O)326 is grabbed, sets
    /// Farmer.canUnderstandDwarves = true, plays "fireball", and DISCARDS the item
    /// (heldItem = null) — it's consumed into the "skill", never entering inventory.
    /// On Android the grab is X (A only toggles the tooltip) via
    /// receiveGamePadButtonGrabbingItems. The bug is presumably that the controller
    /// grab path (with AC's X/Y menu swap, or an Android touch-sim leftClick) misses
    /// that branch, so (O)326 lands in the bag and the flag never flips.
    ///
    /// canUnderstandDwarves is backed by the "HasDwarvishTranslationGuide" mail flag
    /// (Farmer.cs:1278-1288), so patching its setter is the definitive "did the flag
    /// flip" signal regardless of which grab path ran.
    ///
    /// Observation only — NO behaviour change. Logs at Info so they land in the
    /// device log for this one test session. REMOVE before shipping the #71 fix.
    /// </summary>
    internal static class BookRewardDiagnosticPatches
    {
        private static IMonitor Monitor;

        // ItemGrabMenu._selectedItemIndex is private; reflected for observation.
        private static FieldInfo _selectedItemIndexField;

        private const string GuideQualifiedId = "(O)326";

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                _selectedItemIndexField = AccessTools.Field(typeof(ItemGrabMenu), "_selectedItemIndex");

                // Resolve by string — canUnderstandDwarves / receiveGamePadButtonGrabbingItems
                // shapes can differ between the PC DLL we compile against and the Android
                // runtime; null-check and degrade silently if absent.
                MethodInfo setter = AccessTools.PropertySetter(typeof(Farmer), "canUnderstandDwarves");
                if (setter != null)
                    harmony.Patch(setter, postfix: new HarmonyMethod(typeof(BookRewardDiagnosticPatches), nameof(CanUnderstandDwarves_Set_Postfix)));

                MethodInfo grab = AccessTools.Method(typeof(ItemGrabMenu), "receiveGamePadButtonGrabbingItems");
                if (grab != null)
                    harmony.Patch(grab, prefix: new HarmonyMethod(typeof(BookRewardDiagnosticPatches), nameof(GrabbingItems_Prefix)));

                MethodInfo leftClick = AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.receiveLeftClick));
                if (leftClick != null)
                    harmony.Patch(leftClick, prefix: new HarmonyMethod(typeof(BookRewardDiagnosticPatches), nameof(ReceiveLeftClick_Prefix)));

                monitor.Log($"[#71-diag] BookReward diagnostic attached. setter={(setter != null ? "OK" : "NULL")}, "
                    + $"grabbingItems={(grab != null ? "OK" : "NULL")}, leftClick={(leftClick != null ? "OK" : "NULL")}, "
                    + $"selIdx={(_selectedItemIndexField != null ? "OK" : "NULL")}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                monitor.Log($"[#71-diag] failed to attach: {ex.Message}", LogLevel.Error);
            }
        }

        private static void CanUnderstandDwarves_Set_Postfix(bool value)
        {
            try
            {
                Monitor.Log($"[#71-diag] *** canUnderstandDwarves SETTER called, value={value} *** stack:\n{Environment.StackTrace}", LogLevel.Info);
            }
            catch { }
        }

        private static void GrabbingItems_Prefix(ItemGrabMenu __instance, Buttons b)
        {
            try
            {
                int sel = _selectedItemIndexField != null ? (int)_selectedItemIndexField.GetValue(__instance) : -99;
                string selItem = "?";
                bool rewardHasGuide = false;

                var grab = __instance.ItemsToGrabMenu?.actualInventory;
                if (grab != null)
                {
                    for (int i = 0; i < grab.Count; i++)
                    {
                        if (grab[i] != null && grab[i].QualifiedItemId == GuideQualifiedId)
                            rewardHasGuide = true;
                    }
                    if (sel >= 0 && sel < grab.Count && grab[sel] != null)
                        selItem = grab[sel].QualifiedItemId;
                }

                Monitor.Log($"[#71-diag] GrabbingItems button={b} selIdx={sel} selItem={selItem} "
                    + $"reward-has-(O)326={rewardHasGuide} canUnderstandDwarves={Game1.player.canUnderstandDwarves}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"[#71-diag] GrabbingItems log error: {ex.Message}", LogLevel.Warn); } catch { }
            }
        }

        private static void ReceiveLeftClick_Prefix(ItemGrabMenu __instance, int x, int y)
        {
            try
            {
                var grab = __instance.ItemsToGrabMenu?.actualInventory;
                if (grab == null) return;

                bool rewardHasGuide = false;
                for (int i = 0; i < grab.Count; i++)
                {
                    if (grab[i] != null && grab[i].QualifiedItemId == GuideQualifiedId)
                        rewardHasGuide = true;
                }

                if (rewardHasGuide)
                    Monitor.Log($"[#71-diag] ItemGrabMenu.receiveLeftClick ({x},{y}) while (O)326 reward present. "
                        + $"canUnderstandDwarves={Game1.player.canUnderstandDwarves}", LogLevel.Info);
            }
            catch { }
        }
    }
}
