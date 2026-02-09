using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Patches for CarpenterMenu (Robin's building menu) to prevent instant close on Android.
    ///
    /// Root cause (identified in v2.7.3): The A button press from Robin's dialogue carries
    /// over as a mouse-down state into the newly opened CarpenterMenu. The menu's
    /// snapToDefaultClickableComponent() snaps to the cancel button (ID 107). When the
    /// A button is released, the call chain is:
    ///   releaseLeftClick() → OnReleaseCancelButton() → exitThisMenu()
    ///
    /// The four standard input methods (receiveLeftClick, receiveKeyPress, receiveGamePadButton)
    /// are NEVER called during this close. Only leftClickHeld fires (from the held A state)
    /// and releaseLeftClick (when A is released).
    ///
    /// Fix: Block releaseLeftClick and leftClickHeld during a grace period after menu open.
    /// exitThisMenu is also blocked as a safety net.
    /// </summary>
    internal static class CarpenterMenuPatches
    {
        private static IMonitor Monitor;

        /// <summary>Tick when the CarpenterMenu was opened. -1 means not tracking.</summary>
        private static int MenuOpenTick = -1;

        /// <summary>Number of ticks to block input after menu opens.</summary>
        private const int GracePeriodTicks = 20;

        /// <summary>When true, all furniture interactions are blocked until the tool button is released.</summary>
        private static bool _suppressFurnitureUntilRelease = false;

        // --- Joystick cursor/panning fields ---
        private static FieldInfo OnFarmField;      // bool — true when showing farm view
        private static FieldInfo FreezeField;      // bool — true during animations/transitions

        private const float StickDeadzone = 0.2f;
        private const float CursorSpeedMax = 16f;  // px/tick at full tilt
        private const float CursorSpeedMin = 2f;   // px/tick at deadzone edge
        private const int PanEdgeMargin = 64;       // px from viewport edge to start panning
        private const int PanSpeed = 16;             // px/tick viewport scroll at edge

        /// <summary>Whether the cursor has been centered for the current farm view session.</summary>
        private static bool _cursorCentered = false;

        /// <summary>Tracked cursor position (sub-pixel precision, UI coordinates).</summary>
        private static float _cursorX, _cursorY;

        /// <summary>When true, Game1.getMouseX/Y and getOldMouseX/Y return our cursor position.</summary>
        private static bool _overridingMousePosition = false;

        // Diagnostic counters — how many times each override fires per tick
        private static int _getMouseXCalls, _getMouseYCalls, _getOldMouseXCalls, _getOldMouseYCalls, _getMousePositionCalls;

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            if (!ModEntry.Config.EnableCarpenterMenuFix)
            {
                Monitor.Log("Carpenter menu fix is disabled in config.", LogLevel.Trace);
                return;
            }

            try
            {
                // Block releaseLeftClick — this is the actual close trigger.
                // The A-button release from dialogue fires releaseLeftClick on the cancel button.
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.releaseLeftClick)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(ReleaseLeftClick_Prefix))
                );

                // Block leftClickHeld — fires every tick while A is still held from dialogue.
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.leftClickHeld)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(LeftClickHeld_Prefix))
                );

                // Safety net: block exitThisMenu on any CarpenterMenu during grace period.
                // Catches the close even if it comes through an unexpected path.
                harmony.Patch(
                    original: AccessTools.Method(typeof(IClickableMenu), nameof(IClickableMenu.exitThisMenu)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(ExitThisMenu_Prefix))
                );

                // Cache reflection fields for farm-view cursor movement
                OnFarmField = AccessTools.Field(typeof(CarpenterMenu), "onFarm");
                FreezeField = AccessTools.Field(typeof(CarpenterMenu), "freeze");

                // Postfix on update — reads left stick and moves cursor when on farm
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.update),
                                                 new[] { typeof(GameTime) }),
                    postfix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(Update_Postfix))
                );

                Monitor.Log("CarpenterMenu patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply CarpenterMenu patches: {ex.Message}", LogLevel.Error);
            }

            // Mouse getter patches — separate try/catch so core patches survive if these fail.
            // On Android, getOldMouseX/Y and getMouseX/Y are PARAMETERLESS (no ui_scale param).
            // SMAPI rewrites calls to add missing optional params, but the actual methods have none.
            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.getOldMouseX)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(GetOldMouseX_Prefix))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.getOldMouseY)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(GetOldMouseY_Prefix))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.getMouseX)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(GetMouseX_Prefix))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.getMouseY)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(GetMouseY_Prefix))
                );
                Monitor.Log("Mouse getter patches applied successfully.", LogLevel.Trace);

                // Also try getMousePosition — may be a separate code path on Android
                harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.getMousePosition)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(GetMousePosition_Prefix))
                );
                Monitor.Log("getMousePosition patch applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply mouse getter patches: {ex.Message}", LogLevel.Error);
            }

            // Furniture debounce — separate try/catch so carpenter patches still work if this fails
            // The rapid-toggle cycle on Android is: canBeRemoved → performRemoveAction → placementAction
            // repeating every ~3 ticks while the tool button is held. We debounce BOTH directions:
            // - After pickup (performRemoveAction): block auto-placement so furniture stays in inventory
            // - After placement (placementAction): block auto-pickup so furniture stays placed
            if (ModEntry.Config.EnableFurnitureDebounce)
            {
                try
                {
                    harmony.Patch(
                        original: AccessTools.Method(typeof(Furniture), "canBeRemoved"),
                        prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(FurnitureCanBeRemoved_Prefix))
                    );
                    harmony.Patch(
                        original: AccessTools.Method(typeof(Furniture), "performRemoveAction"),
                        prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(FurniturePerformRemoveAction_Prefix))
                    );
                    harmony.Patch(
                        original: AccessTools.Method(typeof(Furniture), "placementAction"),
                        prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(FurniturePlacementAction_Prefix))
                    );
                    Monitor.Log("Furniture debounce patches applied successfully.", LogLevel.Trace);
                }
                catch (Exception ex)
                {
                    Monitor.Log($"Failed to apply furniture debounce patch: {ex.Message}", LogLevel.Error);
                }
            }
            else
            {
                Monitor.Log("Furniture debounce is disabled in config.", LogLevel.Trace);
            }
        }

        /// <summary>Called from ModEntry.OnMenuChanged when a CarpenterMenu opens.</summary>
        public static void OnMenuOpened()
        {
            MenuOpenTick = Game1.ticks;
            _cursorCentered = false;
            if (ModEntry.Config.VerboseLogging)
                Monitor.Log($"CarpenterMenu opened at tick {MenuOpenTick}. Grace period: {GracePeriodTicks} ticks.", LogLevel.Debug);
        }

        /// <summary>Called from ModEntry.OnMenuChanged when the CarpenterMenu closes.</summary>
        public static void OnMenuClosed()
        {
            if (MenuOpenTick >= 0)
            {
                int duration = Game1.ticks - MenuOpenTick;
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"CarpenterMenu closed after {duration} ticks (grace was {GracePeriodTicks}).", LogLevel.Debug);
            }
            MenuOpenTick = -1;
            _cursorCentered = false;
            _overridingMousePosition = false;
        }

        /// <summary>Check if we're within the grace period after menu open.</summary>
        private static bool IsInGracePeriod()
        {
            return MenuOpenTick >= 0 && (Game1.ticks - MenuOpenTick) < GracePeriodTicks;
        }

        /// <summary>Prefix for CarpenterMenu.releaseLeftClick — blocks the A-button release from closing the menu.</summary>
        private static bool ReleaseLeftClick_Prefix(CarpenterMenu __instance, int x, int y)
        {
            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return true;

            if (IsInGracePeriod())
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[CarpenterMenu] BLOCKED releaseLeftClick at ({x},{y}) — grace period ({Game1.ticks - MenuOpenTick}/{GracePeriodTicks} ticks)", LogLevel.Debug);
                return false;
            }

            return true;
        }

        /// <summary>Prefix for CarpenterMenu.leftClickHeld — blocks held-click state from dialogue A press.</summary>
        private static bool LeftClickHeld_Prefix(CarpenterMenu __instance, int x, int y)
        {
            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return true;

            if (IsInGracePeriod())
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[CarpenterMenu] BLOCKED leftClickHeld — grace period", LogLevel.Trace);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Prefix for IClickableMenu.exitThisMenu — safety net for CarpenterMenu.
        /// Only acts on CarpenterMenu instances. Blocks exit during grace period regardless
        /// of which code path triggered it.
        /// </summary>
        private static bool ExitThisMenu_Prefix(IClickableMenu __instance, bool playSound)
        {
            if (__instance is not CarpenterMenu)
                return true;

            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return true;

            if (IsInGracePeriod())
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[CarpenterMenu] BLOCKED exitThisMenu — grace period ({Game1.ticks - MenuOpenTick}/{GracePeriodTicks} ticks)", LogLevel.Debug);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Postfix for CarpenterMenu.update — moves building ghost cursor with left stick.
        /// Console-style: stick moves the building ghost around the screen. When the cursor
        /// reaches the viewport edge, the viewport scrolls in that direction.
        /// Sets _overridingMousePosition so our getMouseX/Y prefixes return cursor position.
        /// </summary>
        private static void Update_Postfix(CarpenterMenu __instance)
        {
            // Clear override by default — only set when all conditions are met
            _overridingMousePosition = false;

            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return;

            if (OnFarmField == null || FreezeField == null)
                return;

            // Only active when on the farm view
            if (!(bool)OnFarmField.GetValue(__instance))
            {
                _cursorCentered = false;
                return;
            }

            // Don't move during animations/transitions
            if ((bool)FreezeField.GetValue(__instance))
                return;

            // Don't move during grace period
            if (IsInGracePeriod())
                return;

            // Don't move during screen fades
            if (Game1.IsFading())
                return;

            // Enable mouse position override — stays true until next update postfix clears it.
            // This means draw() calls to getMouseX/Y will return our cursor position.
            _overridingMousePosition = true;

            // Center cursor on first farm-view frame so building ghost starts mid-screen
            if (!_cursorCentered)
            {
                _cursorX = Game1.viewport.Width / 2f;
                _cursorY = Game1.viewport.Height / 2f;
                _cursorCentered = true;
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[CarpenterMenu] Centered cursor to ({(int)_cursorX},{(int)_cursorY})", LogLevel.Debug);
                return;
            }

            var thumbStick = GamePad.GetState(PlayerIndex.One).ThumbSticks.Left;

            float absX = Math.Abs(thumbStick.X);
            float absY = Math.Abs(thumbStick.Y);

            // Both axes below deadzone — keep cursor at current position (override still active)
            if (absX <= StickDeadzone && absY <= StickDeadzone)
                return;

            // Calculate cursor movement per axis
            float deltaX = 0f, deltaY = 0f;

            if (absX > StickDeadzone)
            {
                float t = (absX - StickDeadzone) / (1f - StickDeadzone);
                float speed = CursorSpeedMin + t * (CursorSpeedMax - CursorSpeedMin);
                deltaX = Math.Sign(thumbStick.X) * speed;
            }

            if (absY > StickDeadzone)
            {
                // Invert Y: stick up (positive) = screen up (negative Y)
                float t = (absY - StickDeadzone) / (1f - StickDeadzone);
                float speed = CursorSpeedMin + t * (CursorSpeedMax - CursorSpeedMin);
                deltaY = -Math.Sign(thumbStick.Y) * speed;
            }

            // Move cursor, clamped to viewport bounds
            _cursorX = Math.Max(0, Math.Min(_cursorX + deltaX, Game1.viewport.Width - 1));
            _cursorY = Math.Max(0, Math.Min(_cursorY + deltaY, Game1.viewport.Height - 1));

            int ix = (int)_cursorX;
            int iy = (int)_cursorY;

            // Pan viewport when cursor reaches the edge
            int panX = 0, panY = 0;

            if (ix < PanEdgeMargin)
                panX = -PanSpeed;
            else if (ix > Game1.viewport.Width - PanEdgeMargin)
                panX = PanSpeed;

            if (iy < PanEdgeMargin)
                panY = -PanSpeed;
            else if (iy > Game1.viewport.Height - PanEdgeMargin)
                panY = PanSpeed;

            if (panX != 0 || panY != 0)
            {
                Game1.panScreen(panX, panY);

                // Compensate cursor for viewport movement — keeps cursor at the same
                // world position. Without this, the cursor sticks to the screen edge
                // and panning continues even after the stick changes direction.
                _cursorX -= panX;
                _cursorY -= panY;
                _cursorX = Math.Max(0, Math.Min(_cursorX, Game1.viewport.Width - 1));
                _cursorY = Math.Max(0, Math.Min(_cursorY, Game1.viewport.Height - 1));
            }

            if (ModEntry.Config.VerboseLogging)
            {
                Monitor.Log($"[CarpenterMenu] Cursor: ({(int)_cursorX},{(int)_cursorY}) pan=({panX},{panY})" +
                    $" overrides: getMouseX={_getMouseXCalls} getMouseY={_getMouseYCalls}" +
                    $" getOldMouseX={_getOldMouseXCalls} getOldMouseY={_getOldMouseYCalls}" +
                    $" getMousePosition={_getMousePositionCalls}", LogLevel.Trace);
            }
            _getMouseXCalls = 0;
            _getMouseYCalls = 0;
            _getOldMouseXCalls = 0;
            _getOldMouseYCalls = 0;
            _getMousePositionCalls = 0;
        }

        // --- Mouse getter prefixes (each with diagnostic counter) ---

        private static bool GetMouseX_Prefix(ref int __result)
        {
            if (!_overridingMousePosition)
                return true;
            _getMouseXCalls++;
            __result = (int)_cursorX;
            return false;
        }

        private static bool GetMouseY_Prefix(ref int __result)
        {
            if (!_overridingMousePosition)
                return true;
            _getMouseYCalls++;
            __result = (int)_cursorY;
            return false;
        }

        private static bool GetOldMouseX_Prefix(ref int __result)
        {
            if (!_overridingMousePosition)
                return true;
            _getOldMouseXCalls++;
            __result = (int)_cursorX;
            return false;
        }

        private static bool GetOldMouseY_Prefix(ref int __result)
        {
            if (!_overridingMousePosition)
                return true;
            _getOldMouseYCalls++;
            __result = (int)_cursorY;
            return false;
        }

        private static bool GetMousePosition_Prefix(ref Point __result)
        {
            if (!_overridingMousePosition)
                return true;
            _getMousePositionCalls++;
            __result = new Point((int)_cursorX, (int)_cursorY);
            return false;
        }

        /// <summary>
        /// Called from ModEntry.OnUpdateTicked every tick during gameplay.
        /// Clears the suppress flag once the tool button is released, allowing
        /// the next distinct button press to interact with furniture.
        /// </summary>
        public static void OnFurnitureUpdateTicked()
        {
            if (!_suppressFurnitureUntilRelease)
                return;

            var gps = Game1.input.GetGamePadState();
            bool toolButtonHeld = gps.Buttons.X == ButtonState.Pressed
                || gps.Buttons.Y == ButtonState.Pressed;

            if (!toolButtonHeld)
            {
                _suppressFurnitureUntilRelease = false;
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log("[Furniture] Tool button released — suppress cleared.", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Prefix for Furniture.canBeRemoved — blocks re-pickup while tool button is held
        /// after a previous furniture interaction (placement or pickup).
        /// </summary>
        private static bool FurnitureCanBeRemoved_Prefix(Furniture __instance, ref bool __result)
        {
            if (_suppressFurnitureUntilRelease)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[Furniture] BLOCKED canBeRemoved on '{__instance.Name}' — suppress until release", LogLevel.Debug);
                __result = false;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Prefix for Furniture.performRemoveAction — sets suppress flag after pickup
        /// so the immediate auto-placement is blocked until the button is released.
        /// </summary>
        private static void FurniturePerformRemoveAction_Prefix()
        {
            _suppressFurnitureUntilRelease = true;
        }

        /// <summary>
        /// Prefix for Furniture.placementAction — blocks auto-placement while suppressed,
        /// and sets suppress flag when placement goes through (to block re-pickup).
        /// </summary>
        private static bool FurniturePlacementAction_Prefix(Furniture __instance, ref bool __result)
        {
            if (_suppressFurnitureUntilRelease)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[Furniture] BLOCKED placementAction on '{__instance.Name}' — suppress until release", LogLevel.Debug);
                __result = false;
                return false;
            }

            _suppressFurnitureUntilRelease = true;
            return true;
        }
    }
}
