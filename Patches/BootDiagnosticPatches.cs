using HarmonyLib;
using StardewModdingAPI;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Boot diagnostic — DISABLED for baseline freeze rate testing.
    /// v7b confirmed fadeFromWhiteTimer stuck at 2000 on frozen boots.
    /// Now testing whether the diagnostic itself contributes to freeze frequency.
    /// </summary>
    internal static class BootDiagnosticPatches
    {
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            // No-op — diagnostics disabled for baseline testing
        }

        public static void ApplyAdditionalPatches(Harmony harmony, IMonitor monitor)
        {
            // No-op — diagnostics disabled for baseline testing
        }
    }
}
