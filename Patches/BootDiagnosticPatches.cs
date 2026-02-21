using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// ONE-OFF DIAGNOSTIC v7: Why does fadeFromWhiteTimer not count down?
    ///
    /// v6 CONCLUSION: Draw pipeline is healthy (60 FPS, render targets fine,
    /// DrawMenu/PushUIMode/renderScreenBuffer all work). The white screen is
    /// caused by the fade overlay staying opaque.
    ///
    /// v7 TARGETS: Log fadeFromWhiteTimer value and IsDoingStorageMigration
    /// to determine why the timer isn't decrementing on stuck boots.
    /// </summary>
    internal static class BootDiagnosticPatches
    {
        private static IMonitor Monitor;
        private static Stopwatch _bootTimer = Stopwatch.StartNew();

        // Reflection cache
        private static FieldInfo _fadeFromWhiteTimerField;
        private static FieldInfo _isDoingStorageMigrationField;
        private static object _mainActivityInstance;
        private static bool _reflectionInitialized;

        // Tracking
        private static int _titleUpdateCount;
        private static int _lastFadeValue = int.MinValue;
        private static long _lastLogMs = -999;
        private static bool _fadeCompleted;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            Monitor.Log("DIAG v7b: Targeting fadeFromWhiteTimer + IsDoingStorageMigration (isolated reads).", LogLevel.Info);
        }

        public static void ApplyAdditionalPatches(Harmony harmony, IMonitor monitor)
        {
            try
            {
                // 1. Patch TitleMenu.update — the core diagnostic
                var updateMethod = AccessTools.Method(typeof(TitleMenu), "update",
                    new[] { typeof(Microsoft.Xna.Framework.GameTime) });
                if (updateMethod != null)
                {
                    harmony.Patch(updateMethod,
                        prefix: new HarmonyMethod(typeof(BootDiagnosticPatches), nameof(TitleMenu_Update_Prefix)));
                    monitor.Log("DIAG v7: Patched TitleMenu.update prefix.", LogLevel.Trace);
                }
                else
                {
                    monitor.Log("DIAG v7: Could not find TitleMenu.update(GameTime)!", LogLevel.Error);
                }

                // 2. Patch Game1._update to detect if TitleMenu.update is even called
                var gameUpdateMethod = AccessTools.Method(typeof(Game1), "_update",
                    new[] { typeof(Microsoft.Xna.Framework.GameTime) });
                if (gameUpdateMethod != null)
                {
                    harmony.Patch(gameUpdateMethod,
                        prefix: new HarmonyMethod(typeof(BootDiagnosticPatches), nameof(Game1_Update_Prefix)));
                    monitor.Log("DIAG v7: Patched Game1._update prefix.", LogLevel.Trace);
                }

                monitor.Log("DIAG v7: All patches applied.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                monitor.Log($"DIAG v7: Failed to apply patches: {ex}", LogLevel.Error);
            }
        }

        private static void InitReflection()
        {
            if (_reflectionInitialized) return;
            _reflectionInitialized = true;

            try
            {
                // fadeFromWhiteTimer on TitleMenu
                _fadeFromWhiteTimerField = AccessTools.Field(typeof(TitleMenu), "fadeFromWhiteTimer");
                if (_fadeFromWhiteTimerField == null)
                    Monitor.Log("DIAG v7: fadeFromWhiteTimer field NOT FOUND on TitleMenu!", LogLevel.Error);
                else
                    Monitor.Log("DIAG v7: fadeFromWhiteTimer field found.", LogLevel.Trace);

                // IsDoingStorageMigration on MainActivity
                // SMAPI replaces MainActivity with SMAPIGameLoader.SMAPIActivity at runtime.
                // We must get the field from the ACTUAL runtime type, not the declared type.
                var gameAssembly = typeof(Game1).Assembly;
                var mainActivityType = gameAssembly.GetType("StardewValley.MainActivity");

                // Get the instance first (from declared type)
                if (mainActivityType != null)
                {
                    var instanceField = AccessTools.Field(mainActivityType, "instance");
                    if (instanceField != null)
                        _mainActivityInstance = instanceField.GetValue(null);
                    else
                    {
                        var instanceProp = AccessTools.Property(mainActivityType, "instance");
                        if (instanceProp != null)
                            _mainActivityInstance = instanceProp.GetValue(null);
                    }
                }

                // Now get IsDoingStorageMigration from the ACTUAL runtime type
                if (_mainActivityInstance != null)
                {
                    var actualType = _mainActivityInstance.GetType();
                    _isDoingStorageMigrationField = actualType.GetField("IsDoingStorageMigration",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    // If not on actual type, try declared type (in case of inheritance)
                    if (_isDoingStorageMigrationField == null)
                        _isDoingStorageMigrationField = AccessTools.Field(mainActivityType, "IsDoingStorageMigration");

                    Monitor.Log($"DIAG v7b: instance type={actualType.FullName}, " +
                        $"declared type={mainActivityType?.FullName}, " +
                        $"IsDoingStorageMigration field={_isDoingStorageMigrationField != null}",
                        LogLevel.Info);
                }
                else if (mainActivityType != null)
                {
                    Monitor.Log("DIAG v7b: MainActivity type found but instance is null!", LogLevel.Warn);
                }
                else
                {
                    Monitor.Log("DIAG v7b: MainActivity type NOT FOUND in game assembly.", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"DIAG v7: Reflection init error: {ex}", LogLevel.Error);
            }
        }

        // ================================================================
        // Game1._update PREFIX — track if the game update loop runs
        // ================================================================
        private static int _gameUpdateCount;
        private static int _lastGameUpdateLogTick = -999;

        static void Game1_Update_Prefix()
        {
            if (_bootTimer.Elapsed.TotalSeconds > 90) return;
            _gameUpdateCount++;

            // Log once per second (every 60 ticks)
            int tick = Game1.ticks;
            if (tick - _lastGameUpdateLogTick < 60) return;
            _lastGameUpdateLogTick = tick;

            string menuType = Game1.activeClickableMenu?.GetType().Name ?? "null";
            Monitor.Log($"DIAG v7 [+{_bootTimer.ElapsedMilliseconds}ms]: Game1._update #{_gameUpdateCount} " +
                $"gameMode={Game1.gameMode} menu={menuType} ticks={Game1.ticks}",
                LogLevel.Trace);
        }

        // ================================================================
        // TitleMenu.update PREFIX — THE KEY DIAGNOSTIC
        // Log fadeFromWhiteTimer, IsDoingStorageMigration, and other timers
        // ================================================================
        static void TitleMenu_Update_Prefix(TitleMenu __instance,
            Microsoft.Xna.Framework.GameTime time)
        {
            if (_fadeCompleted && _bootTimer.Elapsed.TotalSeconds > 30) return;
            if (_bootTimer.Elapsed.TotalSeconds > 90) return;

            InitReflection();
            _titleUpdateCount++;

            // === Read fadeFromWhiteTimer (ISOLATED try/catch) ===
            int fadeValue = -1;
            try
            {
                if (_fadeFromWhiteTimerField != null)
                    fadeValue = (int)_fadeFromWhiteTimerField.GetValue(__instance);
            }
            catch (Exception ex)
            {
                if (_titleUpdateCount <= 3)
                    Monitor.Log($"DIAG v7b: fadeFromWhiteTimer read error: {ex.Message}", LogLevel.Error);
            }

            // === Read IsDoingStorageMigration (ISOLATED try/catch) ===
            string migrationStatus = "UNKNOWN";
            try
            {
                if (_isDoingStorageMigrationField != null && _mainActivityInstance != null)
                {
                    bool migrating = (bool)_isDoingStorageMigrationField.GetValue(_mainActivityInstance);
                    migrationStatus = migrating ? "TRUE (BLOCKED!)" : "false";
                }
                else if (_isDoingStorageMigrationField == null)
                {
                    migrationStatus = "FIELD_NOT_FOUND";
                }
                else if (_mainActivityInstance == null)
                {
                    migrationStatus = "NO_INSTANCE";
                }
            }
            catch (Exception ex)
            {
                migrationStatus = $"ERROR:{ex.Message}";
                if (_titleUpdateCount <= 3)
                    Monitor.Log($"DIAG v7b: migration read error: {ex.Message}", LogLevel.Error);
            }

            // === Read other timers (ISOLATED try/catch) ===
            int logoFadeTimer = -1;
            int pauseTimer = -1;
            int quitTimer = -1;
            try
            {
                var f1 = AccessTools.Field(typeof(TitleMenu), "logoFadeTimer");
                var f2 = AccessTools.Field(typeof(TitleMenu), "pauseBeforeViewportRiseTimer");
                var f3 = AccessTools.Field(typeof(TitleMenu), "quitTimer");
                if (f1 != null) logoFadeTimer = (int)f1.GetValue(__instance);
                if (f2 != null) pauseTimer = (int)f2.GetValue(__instance);
                if (f3 != null) quitTimer = (int)f3.GetValue(__instance);
            }
            catch { }

            // === Logging logic ===
            try
            {
                long nowMs = _bootTimer.ElapsedMilliseconds;
                bool fadeChanged = fadeValue != _lastFadeValue;
                bool timeToLog = (nowMs - _lastLogMs) >= 1000;
                bool isFirst = _titleUpdateCount <= 3;

                if (isFirst || fadeChanged || timeToLog)
                {
                    _lastFadeValue = fadeValue;
                    _lastLogMs = nowMs;

                    string stuckWarning = "";
                    if (!fadeChanged && fadeValue > 0 && _titleUpdateCount > 10)
                        stuckWarning = " *** FADE TIMER NOT CHANGING - STUCK! ***";

                    Monitor.Log($"DIAG v7b [+{nowMs}ms]: TitleMenu.update #{_titleUpdateCount} " +
                        $"fadeFromWhite={fadeValue} migration={migrationStatus} " +
                        $"logoFade={logoFadeTimer} pauseTimer={pauseTimer} quit={quitTimer} " +
                        $"elapsed={time.ElapsedGameTime.Milliseconds}ms" +
                        stuckWarning,
                        (fadeValue > 0 && !fadeChanged && _titleUpdateCount > 10) ? LogLevel.Error : LogLevel.Warn);

                    if (fadeValue <= 0 && !_fadeCompleted)
                    {
                        _fadeCompleted = true;
                        Monitor.Log($"DIAG v7b: fadeFromWhiteTimer reached 0 at +{nowMs}ms " +
                            $"(after {_titleUpdateCount} updates). Title screen should be visible!",
                            LogLevel.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_titleUpdateCount <= 3)
                    Monitor.Log($"DIAG v7b: logging error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
