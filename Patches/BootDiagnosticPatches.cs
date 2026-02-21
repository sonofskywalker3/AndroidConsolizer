using System;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Fix for intermittent white screen freeze on SMAPI Android boot.
    ///
    /// Root cause: SMAPIActivity doesn't extend MainActivity, but gets assigned to
    /// MainActivity.instance. When TitleMenu.update() reads IsDoingStorageMigration
    /// via ldfld, it reads garbage memory at the wrong field offset. Non-zero garbage
    /// blocks fadeFromWhiteTimer from decrementing, causing a permanent white screen.
    ///
    /// Fix: Postfix on TitleMenu.update detects when fadeFromWhiteTimer is stuck
    /// and force-decrements it. This is a mod-level workaround until the SMAPI
    /// loader's IL rewriter is patched to replace the field access with constant false.
    /// </summary>
    internal static class BootDiagnosticPatches
    {
        private static IMonitor Monitor;
        private static FieldInfo _fadeField;
        private static FieldInfo _pauseField;
        private static FieldInfo _logoFadeField;
        private static bool _resolved;
        private static bool _done;
        private static int _lastFadeValue = int.MinValue;
        private static int _stuckFrames;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
        }

        public static void ApplyAdditionalPatches(Harmony harmony, IMonitor monitor)
        {
            try
            {
                var updateMethod = AccessTools.Method(typeof(TitleMenu), "update",
                    new[] { typeof(Microsoft.Xna.Framework.GameTime) });
                if (updateMethod != null)
                {
                    harmony.Patch(updateMethod,
                        postfix: new HarmonyMethod(typeof(BootDiagnosticPatches),
                            nameof(TitleMenu_Update_Postfix)));
                }
            }
            catch (Exception ex)
            {
                monitor.Log($"Boot freeze fix: failed to patch TitleMenu.update: {ex.Message}", LogLevel.Error);
            }
        }

        static void TitleMenu_Update_Postfix(TitleMenu __instance,
            Microsoft.Xna.Framework.GameTime time)
        {
            if (_done) return;

            if (!_resolved)
            {
                _resolved = true;
                _fadeField = AccessTools.Field(typeof(TitleMenu), "fadeFromWhiteTimer");
                _pauseField = AccessTools.Field(typeof(TitleMenu), "pauseBeforeViewportRiseTimer");
                _logoFadeField = AccessTools.Field(typeof(TitleMenu), "logoFadeTimer");
            }

            if (_fadeField == null) return;

            try
            {
                int fadeValue = (int)_fadeField.GetValue(__instance);

                if (fadeValue <= 0)
                {
                    _done = true;
                    return;
                }

                // fadeFromWhiteTimer legitimately stays at 2000 while logoFadeTimer
                // counts down from 5000 (cascading if-else in TitleMenu.update).
                // Only start stuck detection after logoFadeTimer has finished.
                if (_logoFadeField != null)
                {
                    int logoFade = (int)_logoFadeField.GetValue(__instance);
                    if (logoFade > 0)
                        return;
                }

                if (fadeValue == _lastFadeValue)
                {
                    _stuckFrames++;
                    if (_stuckFrames >= 5)
                    {
                        // Timer is stuck — force-decrement it
                        int elapsed = time.ElapsedGameTime.Milliseconds;
                        int newValue = fadeValue - elapsed;
                        _fadeField.SetValue(__instance, newValue);

                        if (_stuckFrames == 5)
                            Monitor.Log("Boot freeze fix: fadeFromWhiteTimer stuck, forcing decrement.", LogLevel.Warn);

                        if (newValue <= 0 && _pauseField != null)
                        {
                            _pauseField.SetValue(__instance, 3500);
                            _done = true;
                            Monitor.Log("Boot freeze fix: fade complete, boot should proceed normally.", LogLevel.Info);
                        }
                    }
                }
                else
                {
                    _stuckFrames = 0;
                }

                _lastFadeValue = fadeValue;
            }
            catch
            {
                _done = true;
            }
        }
    }
}
