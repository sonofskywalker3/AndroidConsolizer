using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// #27 TRANSIENT DIAGNOSTIC (strip once root cause is confirmed on device).
    ///
    /// Goal: the public Nexus bug #1050718 is "Can't resize the toolbar." Code-reading the
    /// Android decompile already shows WHY the slider appears to do nothing: AC's
    /// ToolbarPatches.Toolbar_Draw_Prefix completely replaces Toolbar.draw with a HARDCODED
    /// SlotSize = 64 and never reads Options.toolbarSlotSize. The vanilla "Toolbar Slot Size"
    /// slider (OptionsPage option id 148, range 32-200) still updates
    /// toolbarSlotSize / Game1.maxItemSlotSize / Toolbar.Instance.itemSlotSize and calls
    /// resetToolbar(), but AC ignores all of it.
    ///
    /// This diagnostic confirms the runtime facts the FIX design needs, which code alone can't tell us:
    ///   Q1. Does the slider input actually reach Options.changeSliderOption(148) on G Cloud,
    ///       or is "can't resize" a dead-input problem rather than an AC-ignores-it problem?
    ///   Q2. What value range does it produce, and what is the DEFAULT toolbarSlotSize on this device?
    ///   Q3. Screen width vs. AC's fixed 12-slot width — what scale range can 12 slots actually fit
    ///       (12 slots at 200px would be ~2476px wide, so the fix will need a cap).
    ///
    /// Log-only postfixes on SAFE methods (Options.changeSliderOption, Toolbar.draw) — no input
    /// overrides, no behaviour change. All platform-differing members are read via reflection so
    /// the project still compiles against the PC DLL (toolbarSlotSize, maxItemSlotSize,
    /// Toolbar.itemSlotSize and changeSliderOption are Android-specific shapes).
    /// </summary>
    internal static class ToolbarSizeDiagnosticPatches
    {
        private const int TOOLBAR_SLOT_SIZE_OPTION = 148; // OptionsPage "Toolbar Slot Size" slider id
        private const int TOOLBAR_PADDING_OPTION = 134;    // padding slider — also affects layout
        private const int AC_SLOT_SIZE = 64;               // ToolbarPatches.SlotSize (hardcoded)
        private const int AC_SLOT_SPACING = 4;             // ToolbarPatches.SlotSpacing

        private static IMonitor Monitor;

        // Android-only / platform-differing members — reflected to stay PC-DLL compile-safe.
        private static FieldInfo _toolbarSlotSizeField;   // Options.toolbarSlotSize (instance)
        private static FieldInfo _maxItemSlotSizeField;   // Game1.maxItemSlotSize (static)

        // Dedup for the per-frame draw snapshot (only emit when the observed state changes).
        private static string _lastDrawSnapshot = "";

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            _toolbarSlotSizeField = AccessTools.Field(typeof(StardewValley.Options), "toolbarSlotSize");
            _maxItemSlotSizeField = AccessTools.Field(typeof(Game1), "maxItemSlotSize");

            try
            {
                var changeSlider = AccessTools.Method(typeof(StardewValley.Options), "changeSliderOption",
                    new Type[] { typeof(int), typeof(int) });
                if (changeSlider != null)
                {
                    harmony.Patch(
                        original: changeSlider,
                        postfix: new HarmonyMethod(typeof(ToolbarSizeDiagnosticPatches), nameof(ChangeSliderOption_Postfix))
                    );
                }
                else
                {
                    monitor.Log("[ToolbarSize/diag] Options.changeSliderOption(int,int) not found — slider hook skipped.", LogLevel.Warn);
                }

                harmony.Patch(
                    original: AccessTools.Method(typeof(Toolbar), nameof(Toolbar.draw), new Type[] { typeof(SpriteBatch) }),
                    postfix: new HarmonyMethod(typeof(ToolbarSizeDiagnosticPatches), nameof(Draw_Snapshot_Postfix))
                );

                monitor.Log("[ToolbarSize/diag] instrumentation applied (changeSliderOption + Toolbar.draw snapshot).", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                monitor.Log($"[ToolbarSize/diag] Failed to apply instrumentation: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Read an int reflected field with a sentinel fallback so logging never throws.</summary>
        private static int ReadInt(FieldInfo field, object target)
        {
            try
            {
                if (field == null) return -1;
                object raw = field.GetValue(target);
                return raw is int i ? i : -1;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Q1/Q2: fires whenever ANY options slider is dragged. We log only the toolbar-relevant
        /// ids (148 slot size, 134 padding). Seeing this line at all proves the slider input
        /// reaches the field on Android; the values prove what it stored.
        /// </summary>
        private static void ChangeSliderOption_Postfix(int which, int value)
        {
            if (which != TOOLBAR_SLOT_SIZE_OPTION && which != TOOLBAR_PADDING_OPTION) return;
            try
            {
                int slotSize = ReadInt(_toolbarSlotSizeField, Game1.options);
                int maxSlot = ReadInt(_maxItemSlotSizeField, null);
                int uiW = Game1.uiViewport.Width;
                string label = which == TOOLBAR_SLOT_SIZE_OPTION ? "SLOT_SIZE(148)" : "PADDING(134)";
                Monitor.Log(
                    $"[ToolbarSize/diag] changeSliderOption {label} value={value} → toolbarSlotSize={slotSize} "
                    + $"maxItemSlotSize={maxSlot} | uiViewport.W={uiW} | AC ignores this (draws fixed {AC_SLOT_SIZE}px).",
                    LogLevel.Info);
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"[ToolbarSize/diag] changeSliderOption log failed: {ex.Message}", LogLevel.Warn); } catch { }
            }
        }

        /// <summary>
        /// Q2/Q3 baseline: one deduped snapshot of the overworld toolbar-size state. Captures the
        /// DEFAULT toolbarSlotSize for this device, the screen width, AC's fixed 12-slot footprint,
        /// and what a vanilla-honoured 12-slot row would span at the current slider value (to show
        /// whether it would overflow the screen). Runs as a postfix so it fires even though
        /// ToolbarPatches' prefix skips the original draw.
        /// </summary>
        private static void Draw_Snapshot_Postfix()
        {
            try
            {
                int slotSize = ReadInt(_toolbarSlotSizeField, Game1.options);
                int maxSlot = ReadInt(_maxItemSlotSizeField, null);
                int uiW = Game1.uiViewport.Width;
                int uiH = Game1.uiViewport.Height;
                bool consoleToolbar = ModEntry.Config?.EnableConsoleToolbar ?? false;

                // AC's actual fixed footprint (ToolbarPatches): 12 slots + 11 gaps + 32 bg.
                int acWidth = (AC_SLOT_SIZE * 12) + (AC_SLOT_SPACING * 11) + 32;
                // What 12 slots WOULD span if AC honoured the slider value.
                int scaledWidth = slotSize > 0 ? (slotSize * 12) + (AC_SLOT_SPACING * 11) + 32 : -1;
                bool scaledOverflows = scaledWidth > uiW;

                string snap = $"ui=({uiW}x{uiH}) toolbarSlotSize={slotSize} maxItemSlotSize={maxSlot} "
                    + $"consoleToolbar={consoleToolbar} | ACfixedWidth={acWidth} (fits={acWidth <= uiW}) "
                    + $"| scaled12@{slotSize}={scaledWidth} (overflows={scaledOverflows})";
                if (snap == _lastDrawSnapshot) return;
                _lastDrawSnapshot = snap;
                Monitor.Log($"[ToolbarSize/diag] toolbar draw: {snap}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                try { Monitor.Log($"[ToolbarSize/diag] draw snapshot failed: {ex.Message}", LogLevel.Warn); } catch { }
            }
        }
    }
}
