using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
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
        private static FieldInfo CurrentBuildingField; // Building — the building being placed
        // Mode detection: PC 1.6 uses CarpentryAction enum field "Action"; Android uses bool "moving"/"demolishing"
        private static FieldInfo ActionField;        // PC: CarpentryAction enum (null on Android)
        private static FieldInfo MovingField;        // Android: bool (null on PC)
        private static FieldInfo DemolishingField;   // Android: bool (null on PC)
        private static FieldInfo BuildingToMoveField; // Building being moved (both platforms)
        private static FieldInfo DrawAtXField;          // int — ghost draw X in unzoomed coords
        private static FieldInfo DrawAtYField;          // int — ghost draw Y in unzoomed coords
        private static FieldInfo PriceField;           // int — current blueprint price
        private static FieldInfo SelectedBottomButtonField; // BottomButton enum — which button is selected
        private static MethodInfo DoesFarmerHaveEnoughResourcesToBuildMethod; // affordability check

        private const float StickDeadzone = 0.2f;
        private const float CursorSpeedMax = 16f;  // px/tick at full tilt
        private const float CursorSpeedMin = 2f;   // px/tick at deadzone edge
        private const int PanEdgeMargin = 64;       // px from viewport edge to start panning
        private const int PanSpeed = 16;             // px/tick viewport scroll at edge

        /// <summary>Whether the cursor has been centered for the current farm view session.</summary>
        private static bool _cursorCentered = false;

        /// <summary>Tracked cursor position (sub-pixel precision, UI coordinates).</summary>
        private static float _cursorX, _cursorY;

        /// <summary>When true, cursor is visible and A button triggers clicks at cursor position.</summary>
        private static bool _cursorActive = false;

        /// <summary>Previous frame's A button state for edge detection.</summary>
        private static bool _prevAPressed = true;

        /// <summary>When true, GetMouseState postfix returns cursor position. Only active during receiveLeftClick.</summary>
        private static bool _overridingMousePosition = false;

        /// <summary>Current building's tile height, cached from Update_Postfix for use in GetMouseState_Postfix.</summary>
        private static int _buildingTileHeight = 0;

        /// <summary>Current building's tile width, cached from Update_Postfix for use in GetMouseState_Postfix.</summary>
        private static int _buildingTileWidth = 0;

        /// <summary>True after the first A press has positioned the ghost at the cursor.
        /// The next A press (without cursor movement) will let receiveGamePadButton(A) through to build.</summary>
        private static bool _ghostPlaced = false;

        /// <summary>Set by ReceiveGamePadButton_Prefix when it lets A through for building.
        /// Tells Update_Postfix to skip the receiveLeftClick call on this frame.</summary>
        private static bool _buildPressHandled = false;

        /// <summary>The building currently selected for demolition (OUR tracking, not the game's).
        /// Null means no selection. The game never sees a selection until we send both
        /// select + confirm as a pair.</summary>
        private static Building _demolishSelectedBuilding = null;

        /// <summary>When true, touch-sim clicks are blocked on the shop screen until A is released.
        /// Set when we intercept an unaffordable Build A press to prevent the touch-sim
        /// receiveLeftClick/releaseLeftClick at (-39,-39) from hitting cancelButton.</summary>
        private static bool _blockShopClicks = false;

        // --- Appearance button (building skin picker) ---
        private static FieldInfo AppearanceButtonField;
        private static FieldInfo OnBottomButtonsField;

        // --- Shop screen cursor navigation (#34) ---
        // Row 0: [back, appearance (if visible), forward]
        // Row 1: [move, build (skipped if unaffordable), demolish, paint]
        private static int _shopRow = 1;  // 0=arrows+appearance, 1=bottom buttons
        private static int _shopCol = 0;  // column within row
        private static FieldInfo BackButtonField;
        private static FieldInfo ForwardButtonField;
        private static FieldInfo MoveButtonField;
        private static FieldInfo BuildButtonField;
        private static FieldInfo DemolishButtonField;
        private static FieldInfo PaintButtonField;
        private static MethodInfo OnTapLeftArrowMethod;
        private static MethodInfo OnTapRightArrowMethod;
        private static MethodInfo OnReleaseMoveMethod;
        private static MethodInfo OnReleaseBuildMethod;
        private static MethodInfo OnReleaseDemolishMethod;
        private static MethodInfo OnReleasePaintMethod;

        // --- BuildingSkinMenu cursor ---
        // Layout:  Row 0: [Prev (col 0), Next (col 1)]
        //          Row 1: [OK (col 0, visually under Next)]
        private static int _skinRow = 1;   // 0=arrows, 1=OK
        private static int _skinCol = 0;   // row 0: 0=prev, 1=next; row 1: 0=ok
        private static int _lastSkinMenuHash = 0;
        private static FieldInfo SkinLeftButtonField;
        private static FieldInfo SkinRightButtonField;
        private static FieldInfo SkinOkButtonField;

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

                // Two-press A button: first press blocks receiveGamePadButton(A) so our
                // postfix can position the ghost via receiveLeftClick. Second press lets
                // receiveGamePadButton(A) through to trigger the actual build at the
                // ghost's stored position.
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(ReceiveGamePadButton_Prefix))
                );


                // Cache reflection fields for farm-view cursor movement
                OnFarmField = AccessTools.Field(typeof(CarpenterMenu), "onFarm");
                FreezeField = AccessTools.Field(typeof(CarpenterMenu), "freeze");
                CurrentBuildingField = AccessTools.Field(typeof(CarpenterMenu), "currentBuilding");
                // Mode detection — try PC field first, fall back to Android booleans
                ActionField = AccessTools.Field(typeof(CarpenterMenu), "Action");
                MovingField = AccessTools.Field(typeof(CarpenterMenu), "moving");
                DemolishingField = AccessTools.Field(typeof(CarpenterMenu), "demolishing");
                BuildingToMoveField = AccessTools.Field(typeof(CarpenterMenu), "buildingToMove");
                DrawAtXField = AccessTools.Field(typeof(CarpenterMenu), "_drawAtX");
                DrawAtYField = AccessTools.Field(typeof(CarpenterMenu), "_drawAtY");
                PriceField = AccessTools.Field(typeof(CarpenterMenu), "price");
                SelectedBottomButtonField = AccessTools.Field(typeof(CarpenterMenu), "_selectedBottomButton");
                DoesFarmerHaveEnoughResourcesToBuildMethod = AccessTools.Method(typeof(CarpenterMenu), "DoesFarmerHaveEnoughResourcesToBuild");
                AppearanceButtonField = AccessTools.Field(typeof(CarpenterMenu), "appearanceButton");
                OnBottomButtonsField = AccessTools.Field(typeof(CarpenterMenu), "_onBottomButtons");
                BackButtonField = AccessTools.Field(typeof(CarpenterMenu), "backButton");
                ForwardButtonField = AccessTools.Field(typeof(CarpenterMenu), "forwardButton");
                MoveButtonField = AccessTools.Field(typeof(CarpenterMenu), "moveButton");
                BuildButtonField = AccessTools.Field(typeof(CarpenterMenu), "buildButton");
                DemolishButtonField = AccessTools.Field(typeof(CarpenterMenu), "demolishButton");
                PaintButtonField = AccessTools.Field(typeof(CarpenterMenu), "paintButton");
                OnTapLeftArrowMethod = AccessTools.Method(typeof(CarpenterMenu), "OnTapButtonLeftArrow");
                OnTapRightArrowMethod = AccessTools.Method(typeof(CarpenterMenu), "OnTapButtonRightArrow");
                OnReleaseMoveMethod = AccessTools.Method(typeof(CarpenterMenu), "OnReleaseMoveButton");
                OnReleaseBuildMethod = AccessTools.Method(typeof(CarpenterMenu), "OnReleaseBuildButton");
                OnReleaseDemolishMethod = AccessTools.Method(typeof(CarpenterMenu), "OnReleaseDemolishButton");
                OnReleasePaintMethod = AccessTools.Method(typeof(CarpenterMenu), "OnReleasePaintButton");
                Monitor.Log($"CarpenterMenu fields: Action={ActionField != null}, moving={MovingField != null}, demolishing={DemolishingField != null}, price={PriceField != null}, selectedBtn={SelectedBottomButtonField != null}, hasResources={DoesFarmerHaveEnoughResourcesToBuildMethod != null}, tapArrows={OnTapLeftArrowMethod != null}", LogLevel.Trace);

                // Block touch-sim receiveLeftClick after unaffordable Build intercept (#14e fix)
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.receiveLeftClick)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(ReceiveLeftClick_Prefix))
                );

                // Postfix on update — reads left stick and moves cursor when on farm
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.update),
                                                 new[] { typeof(GameTime) }),
                    postfix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(Update_Postfix))
                );

                // Postfix on draw — renders visible cursor at joystick position
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.draw),
                                                 new[] { typeof(SpriteBatch) }),
                    postfix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(Draw_Postfix))
                );

                Monitor.Log("CarpenterMenu patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply CarpenterMenu patches: {ex.Message}", LogLevel.Error);
            }

            // BuildingSkinMenu cursor patches (#34)
            try
            {
                var skinMenuType = AccessTools.TypeByName("StardewValley.Menus.BuildingSkinMenu");
                if (skinMenuType != null)
                {
                    SkinLeftButtonField = AccessTools.Field(skinMenuType, "PreviousSkinButton");
                    SkinRightButtonField = AccessTools.Field(skinMenuType, "NextSkinButton");
                    SkinOkButtonField = AccessTools.Field(skinMenuType, "OkButton");

                    harmony.Patch(
                        original: AccessTools.Method(skinMenuType, "receiveGamePadButton"),
                        prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(SkinMenu_ReceiveGamePadButton_Prefix))
                    );
                    harmony.Patch(
                        original: AccessTools.Method(skinMenuType, "draw", new[] { typeof(SpriteBatch) }),
                        postfix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(SkinMenu_Draw_Postfix))
                    );
                    Monitor.Log($"BuildingSkinMenu patches applied. leftBtn={SkinLeftButtonField != null} rightBtn={SkinRightButtonField != null} okBtn={SkinOkButtonField != null}", LogLevel.Trace);
                }
                else
                {
                    Monitor.Log("BuildingSkinMenu type not found — skin picker cursor disabled.", LogLevel.Trace);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply BuildingSkinMenu patches: {ex.Message}", LogLevel.Error);
            }

            // GetMouseState patches — receiveLeftClick reads mouse position internally instead of
            // using its x,y parameters. We override GetMouseState momentarily during the call so
            // the game reads our cursor position for building placement.
            try
            {
                var inputType = Game1.input.GetType();
                var getMouseStateMethod = AccessTools.Method(inputType, "GetMouseState");
                if (getMouseStateMethod != null)
                {
                    harmony.Patch(
                        original: getMouseStateMethod,
                        postfix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(GetMouseState_Postfix))
                    );
                    Monitor.Log($"Patched {inputType.Name}.GetMouseState postfix.", LogLevel.Trace);
                }

                var xnaMouseGetState = AccessTools.Method(typeof(Mouse), nameof(Mouse.GetState));
                if (xnaMouseGetState != null)
                {
                    harmony.Patch(
                        original: xnaMouseGetState,
                        postfix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(GetMouseState_Postfix))
                    );
                    Monitor.Log("Patched Mouse.GetState postfix.", LogLevel.Trace);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply mouse state patches: {ex.Message}", LogLevel.Error);
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
            _prevAPressed = true;
            _ghostPlaced = false;
            _buildPressHandled = false;
            _demolishSelectedBuilding = null;
            _shopRow = 1;
            _shopCol = 0;
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
            _cursorActive = false;
            _prevAPressed = true;
            _ghostPlaced = false;
            _buildPressHandled = false;
            _demolishSelectedBuilding = null;
            _blockShopClicks = false;
            _overridingMousePosition = false;
            _shopRow = 1;
            _shopCol = 0;
        }

        /// <summary>Check if we're within the grace period after menu open.</summary>
        private static bool IsInGracePeriod()
        {
            return MenuOpenTick >= 0 && (Game1.ticks - MenuOpenTick) < GracePeriodTicks;
        }

        /// <summary>Check if the menu is in a mode where A should always fire receiveLeftClick
        /// (move, demolish) rather than using the two-press build system.</summary>
        private static bool IsClickThroughMode(CarpenterMenu instance)
        {
            // PC 1.6: CarpentryAction enum field "Action" (Demolish=1, Move=2)
            if (ActionField != null)
            {
                int action = (int)ActionField.GetValue(instance);
                return action == 1 || action == 2;
            }
            // Android: separate bool fields "moving" and "demolishing"
            if (MovingField != null && (bool)MovingField.GetValue(instance))
                return true;
            if (DemolishingField != null && (bool)DemolishingField.GetValue(instance))
                return true;
            return false;
        }

        /// <summary>Find the building at the current cursor position, or null if empty space.</summary>
        private static Building GetBuildingAtCursor()
        {
            var farm = Game1.getFarm();
            if (farm == null) return null;

            // Convert cursor (game render coords, viewport-relative) to world tile coords.
            // _cursorX is in the same space as viewport dimensions — no zoom conversion needed.
            int worldX = Game1.viewport.X + (int)_cursorX;
            int worldY = Game1.viewport.Y + (int)_cursorY;
            int tileX = worldX / 64;
            int tileY = worldY / 64;

            foreach (var building in farm.buildings)
            {
                if (tileX >= building.tileX.Value && tileX < building.tileX.Value + building.tilesWide.Value &&
                    tileY >= building.tileY.Value && tileY < building.tileY.Value + building.tilesHigh.Value)
                {
                    return building;
                }
            }
            return null;
        }

        // ====================================================================
        // Shop screen cursor navigation helpers (#34)
        // ====================================================================

        /// <summary>Check if the appearance button is visible for the current blueprint.</summary>
        private static bool IsAppearanceVisible(CarpenterMenu menu)
        {
            if (AppearanceButtonField == null) return false;
            var appBtn = AppearanceButtonField.GetValue(menu) as ClickableTextureComponent;
            return appBtn != null && appBtn.visible;
        }

        /// <summary>Check if the current blueprint's Build is affordable.</summary>
        private static bool IsBuildAffordable(CarpenterMenu menu)
        {
            int price = PriceField != null ? (int)PriceField.GetValue(menu) : 0;
            bool canAfford = price >= 0 && Game1.player.Money >= price;
            if (!canAfford) return false;
            if (DoesFarmerHaveEnoughResourcesToBuildMethod != null)
                return (bool)DoesFarmerHaveEnoughResourcesToBuildMethod.Invoke(menu, null);
            return true;
        }

        /// <summary>Get the list of components in row 0: [back, appearance (if visible), forward].</summary>
        private static List<ClickableComponent> GetRow0Components(CarpenterMenu menu)
        {
            var list = new List<ClickableComponent>();
            var back = BackButtonField?.GetValue(menu) as ClickableComponent;
            if (back != null) list.Add(back);
            if (IsAppearanceVisible(menu))
            {
                var app = AppearanceButtonField?.GetValue(menu) as ClickableComponent;
                if (app != null) list.Add(app);
            }
            var forward = ForwardButtonField?.GetValue(menu) as ClickableComponent;
            if (forward != null) list.Add(forward);
            return list;
        }

        /// <summary>Get the list of navigable bottom buttons (skips unaffordable Build).</summary>
        private static List<ClickableComponent> GetNavigableBottomButtons(CarpenterMenu menu)
        {
            var list = new List<ClickableComponent>();
            var move = MoveButtonField?.GetValue(menu) as ClickableComponent;
            var build = BuildButtonField?.GetValue(menu) as ClickableComponent;
            var demolish = DemolishButtonField?.GetValue(menu) as ClickableComponent;
            var paint = PaintButtonField?.GetValue(menu) as ClickableComponent;
            if (move?.visible == true) list.Add(move);
            if (build?.visible == true && IsBuildAffordable(menu)) list.Add(build);
            if (demolish?.visible == true) list.Add(demolish);
            if (paint?.visible == true) list.Add(paint);
            return list;
        }

        /// <summary>Get the component currently focused by the shop cursor.</summary>
        private static ClickableComponent GetShopCurrentComponent(CarpenterMenu menu)
        {
            if (_shopRow == 0)
            {
                var row = GetRow0Components(menu);
                return _shopCol < row.Count ? row[_shopCol] : null;
            }
            if (_shopRow == 1)
            {
                var btns = GetNavigableBottomButtons(menu);
                return _shopCol < btns.Count ? btns[_shopCol] : null;
            }
            return null;
        }

        /// <summary>Ensure the shop cursor is on a valid component after state changes.</summary>
        private static void ValidateShopCursor(CarpenterMenu menu)
        {
            if (_shopRow == 0)
            {
                var row = GetRow0Components(menu);
                if (row.Count == 0) _shopCol = 0;
                else if (_shopCol >= row.Count) _shopCol = row.Count - 1;
            }
            else if (_shopRow == 1)
            {
                var btns = GetNavigableBottomButtons(menu);
                if (btns.Count == 0) _shopCol = 0;
                else if (_shopCol >= btns.Count) _shopCol = btns.Count - 1;
            }
        }

        /// <summary>Cycle buildings while preserving cursor on the same logical component (back/forward arrow).</summary>
        private static void CycleBuilding(CarpenterMenu menu, bool forward)
        {
            // Capture which logical component we're on before cycling
            bool wasOnForward = false;
            if (_shopRow == 0)
            {
                var oldRow = GetRow0Components(menu);
                if (oldRow.Count > 0 && _shopCol == oldRow.Count - 1)
                    wasOnForward = true;
                // wasOnBack is always _shopCol == 0, which is preserved automatically
            }

            if (forward)
                OnTapRightArrowMethod?.Invoke(menu, null);
            else
                OnTapLeftArrowMethod?.Invoke(menu, null);

            // Restore cursor to same logical component
            if (_shopRow == 0 && wasOnForward)
            {
                var newRow = GetRow0Components(menu);
                if (newRow.Count > 0)
                    _shopCol = newRow.Count - 1; // forward arrow is always last
            }
            else
            {
                ValidateShopCursor(menu);
            }
        }

        /// <summary>Handle all gamepad input on the shop screen (not on farm).</summary>
        private static bool HandleShopInput(CarpenterMenu menu, Buttons b)
        {
            // If a child menu is open (e.g. BuildingSkinMenu), forward input to it
            var childMenu = menu.GetChildMenu();
            if (childMenu != null)
            {
                childMenu.receiveGamePadButton(b);
                return false;
            }

            // B: always pass through (exit menu)
            if (b == Buttons.B)
                return true;

            // LT/RT/LB/RB: cycle buildings
            if (b == Buttons.LeftTrigger || b == Buttons.LeftShoulder)
            {
                CycleBuilding(menu, false);
                return false;
            }
            if (b == Buttons.RightTrigger || b == Buttons.RightShoulder)
            {
                CycleBuilding(menu, true);
                return false;
            }

            // D-pad / thumbstick navigation
            bool isLeft = b == Buttons.DPadLeft || b == Buttons.LeftThumbstickLeft;
            bool isRight = b == Buttons.DPadRight || b == Buttons.LeftThumbstickRight;
            bool isUp = b == Buttons.DPadUp || b == Buttons.LeftThumbstickUp;
            bool isDown = b == Buttons.DPadDown || b == Buttons.LeftThumbstickDown;

            if (isLeft || isRight || isUp || isDown)
            {
                HandleShopNav(menu, isLeft, isRight, isUp, isDown);
                return false;
            }

            // A: activate current component
            if (b == Buttons.A)
                return HandleShopAPress(menu);

            return true;
        }

        /// <summary>Handle D-pad/thumbstick navigation on the shop screen.</summary>
        private static void HandleShopNav(CarpenterMenu menu, bool left, bool right, bool up, bool down)
        {
            if (up)
            {
                if (_shopRow == 1)
                {
                    _shopRow = 0;
                    // If appearance is visible, land on it first
                    if (IsAppearanceVisible(menu))
                    {
                        var row = GetRow0Components(menu);
                        var app = AppearanceButtonField?.GetValue(menu) as ClickableComponent;
                        int appIdx = row.IndexOf(app);
                        if (appIdx >= 0) _shopCol = appIdx;
                    }
                }
                // Row 0: already at top
            }
            else if (down)
            {
                if (_shopRow == 0)
                {
                    // If on an arrow and appearance is visible, go to appearance first
                    var row = GetRow0Components(menu);
                    var currentComp = _shopCol < row.Count ? row[_shopCol] : null;
                    var app = AppearanceButtonField?.GetValue(menu) as ClickableComponent;
                    if (IsAppearanceVisible(menu) && currentComp != app)
                    {
                        // On back or forward arrow → stop at appearance
                        int appIdx = row.IndexOf(app);
                        if (appIdx >= 0) _shopCol = appIdx;
                    }
                    else
                    {
                        // On appearance (or no appearance) → go to buttons
                        _shopRow = 1;
                    }
                }
                // Row 1: already at bottom
            }
            else if (left)
            {
                if (_shopRow == 0)
                {
                    if (_shopCol > 0)
                        _shopCol--;
                    else
                    {
                        // At back arrow — cycle to previous building (stays on back arrow)
                        CycleBuilding(menu, false);
                    }
                }
                else if (_shopRow == 1)
                {
                    if (_shopCol > 0) _shopCol--;
                }
            }
            else if (right)
            {
                if (_shopRow == 0)
                {
                    var row = GetRow0Components(menu);
                    if (_shopCol < row.Count - 1)
                        _shopCol++;
                    else
                    {
                        // At forward arrow — cycle to next building (stays on forward arrow)
                        CycleBuilding(menu, true);
                    }
                }
                else if (_shopRow == 1)
                {
                    var btns = GetNavigableBottomButtons(menu);
                    if (_shopCol < btns.Count - 1) _shopCol++;
                }
            }

            ValidateShopCursor(menu);
            Game1.playSound("smallSelect");
        }

        /// <summary>Handle A press on the shop screen. Returns false to block game handler, true to let through.</summary>
        private static bool HandleShopAPress(CarpenterMenu menu)
        {
            if (_shopRow == 0)
            {
                // Row 0: back, appearance (if visible), forward
                var row = GetRow0Components(menu);
                if (_shopCol >= row.Count) return false;
                var comp = row[_shopCol];

                var back = BackButtonField?.GetValue(menu) as ClickableComponent;
                var forward = ForwardButtonField?.GetValue(menu) as ClickableComponent;

                if (comp == back)
                {
                    CycleBuilding(menu, false);
                }
                else if (comp == forward)
                {
                    CycleBuilding(menu, true);
                }
                else
                {
                    // Appearance button — open BuildingSkinMenu
                    var appBtn = AppearanceButtonField?.GetValue(menu) as ClickableTextureComponent;
                    if (appBtn != null && appBtn.visible)
                        menu.receiveLeftClick(appBtn.bounds.Center.X, appBtn.bounds.Center.Y);
                }
                return false;
            }
            else if (_shopRow == 1)
            {
                // Bottom buttons: activate the selected one
                var btns = GetNavigableBottomButtons(menu);
                if (_shopCol >= btns.Count) return false;
                var btn = btns[_shopCol];

                var buildBtn = BuildButtonField?.GetValue(menu) as ClickableComponent;
                var moveBtn = MoveButtonField?.GetValue(menu) as ClickableComponent;
                var demolishBtn = DemolishButtonField?.GetValue(menu) as ClickableComponent;
                var paintBtn = PaintButtonField?.GetValue(menu) as ClickableComponent;

                if (btn == buildBtn)
                {
                    _blockShopClicks = true;
                    OnReleaseBuildMethod?.Invoke(menu, null);
                }
                else if (btn == moveBtn)
                {
                    _blockShopClicks = true;
                    OnReleaseMoveMethod?.Invoke(menu, null);
                }
                else if (btn == demolishBtn)
                {
                    _blockShopClicks = true;
                    OnReleaseDemolishMethod?.Invoke(menu, null);
                }
                else if (btn == paintBtn)
                {
                    _blockShopClicks = true;
                    OnReleasePaintMethod?.Invoke(menu, null);
                }

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[CarpenterMenu] Shop A: activated {btn?.name ?? "?"}", LogLevel.Debug);
                return false;
            }

            return false;
        }

        /// <summary>Prefix for receiveLeftClick — blocks touch-sim clicks after unaffordable Build intercept (#14e).</summary>
        private static bool ReceiveLeftClick_Prefix(CarpenterMenu __instance, int x, int y)
        {
            // Block touch-sim clicks after we intercepted an unaffordable Build A press.
            // The A button triggers touch-sim at (-39,-39) which hits cancelButton's off-screen bounds.
            if (!_cursorActive && _blockShopClicks)
            {
                // Clear the flag once A is released, but still block this click
                // (the release itself generates the touch-sim we're trying to block)
                var gps = GamePad.GetState(PlayerIndex.One);
                if (gps.Buttons.A != ButtonState.Pressed)
                    _blockShopClicks = false;
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[CarpenterMenu] Blocked touch-sim receiveLeftClick({x},{y}) after unaffordable Build intercept", LogLevel.Debug);
                return false;
            }

            // Block touch-sim clicks while BuildingSkinMenu is open as child.
            // Our SkinMenu prefix handles A presses via receiveLeftClick on the child;
            // the Android touch-sim then fires receiveLeftClick on CarpenterMenu which
            // closes/disrupts the child menu.
            if (__instance.GetChildMenu() != null)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[CarpenterMenu] Blocked touch-sim receiveLeftClick({x},{y}) — child menu (BuildingSkinMenu) is open", LogLevel.Debug);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Prefix for CarpenterMenu.receiveGamePadButton — handles both shop screen
        /// cursor navigation (#34) and farm view two-press A system.
        /// </summary>
        private static bool ReceiveGamePadButton_Prefix(CarpenterMenu __instance, Buttons b)
        {
            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return true;

            bool onFarm = OnFarmField != null && (bool)OnFarmField.GetValue(__instance);

            // === SHOP SCREEN: full cursor navigation (#34) ===
            if (!onFarm)
                return HandleShopInput(__instance, b);

            // === FARM VIEW: joystick cursor + two-press A ===
            if (!_cursorActive)
                return true;

            if (b == Buttons.A)
            {
                if (IsClickThroughMode(__instance))
                {
                    // Move mode: if no building is selected yet, let A through for
                    // initial selection. receiveLeftClick can't select buildings.
                    bool isMovingVal = MovingField != null && (bool)MovingField.GetValue(__instance);
                    if (isMovingVal)
                    {
                        var btm = BuildingToMoveField?.GetValue(__instance) as Building;
                        if (btm == null)
                        {
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log("[CarpenterMenu] Move: no building selected → let A through for selection", LogLevel.Debug);
                            _buildPressHandled = true;
                            return true;
                        }
                    }

                    // Demolish mode: first A selects (game highlights building),
                    // second A on same building confirms. If cursor moved off the
                    // building, send B to cancel the selection instead of A.
                    bool isDemolishingVal = DemolishingField != null && (bool)DemolishingField.GetValue(__instance);
                    if (isDemolishingVal)
                    {
                        if (_demolishSelectedBuilding == null)
                        {
                            if (_buildPressHandled)
                                return false;
                            _buildPressHandled = true;
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log("[CarpenterMenu] Demolish: no selection → letting A through", LogLevel.Debug);
                            return true;
                        }

                        if (_ghostPlaced || GetBuildingAtCursor() == _demolishSelectedBuilding)
                        {
                            _demolishSelectedBuilding = null;
                            _ghostPlaced = false;
                            _buildPressHandled = true;
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log("[CarpenterMenu] Demolish: same building → CONFIRM", LogLevel.Debug);
                            return true;
                        }

                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"[CarpenterMenu] Demolish: off building → receiveLeftClick({(int)_cursorX},{(int)_cursorY}) to deselect", LogLevel.Debug);
                        _demolishSelectedBuilding = null;
                        _ghostPlaced = false;
                        __instance.receiveLeftClick((int)_cursorX, (int)_cursorY);
                        return false;
                    }

                    // Move (building selected): two-press guard
                    if (_ghostPlaced)
                    {
                        _ghostPlaced = false;
                        _buildPressHandled = true;
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log("[CarpenterMenu] Move: cursor unchanged → letting A through to CONFIRM", LogLevel.Debug);
                        return true;
                    }
                    else
                    {
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log("[CarpenterMenu] Move: BLOCKED A → positioning first", LogLevel.Trace);
                        return false;
                    }
                }

                // Build/upgrade/paint mode: single-press. Ghost already tracks cursor
                // via _drawAtX/_drawAtY set every frame in Update_Postfix.
                // Let A through — OnClickOK() → tryToBuild() reads _drawAtX/_drawAtY.
                _buildPressHandled = true;
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[CarpenterMenu] BUILD: letting A through for tryToBuild at ({(int)_cursorX},{(int)_cursorY}) zoom={Game1.options.zoomLevel}", LogLevel.Debug);
                return true;
            }

            return true;
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

            // #14e: Block touch-sim after unaffordable Build intercept (until A released)
            if (!_cursorActive && _blockShopClicks)
            {
                var gps = GamePad.GetState(PlayerIndex.One);
                if (gps.Buttons.A != ButtonState.Pressed)
                    _blockShopClicks = false;
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[CarpenterMenu] Blocked touch-sim releaseLeftClick({x},{y}) after unaffordable Build intercept", LogLevel.Debug);
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
        /// Postfix for CarpenterMenu.update — moves joystick cursor with left stick.
        /// Stick moves a visible cursor around the screen. When the cursor reaches the
        /// viewport edge, the viewport scrolls. A button fires receiveLeftClick at cursor
        /// position to snap the building ghost there (same as a touch tap).
        /// </summary>
        private static void Update_Postfix(CarpenterMenu __instance)
        {
            // Don't clear _overridingMousePosition here — in move/demolish mode it stays
            // active across frames so the game always reads our cursor position. It's
            // cleared in each early return and managed per-mode below.

            // Clear cursor by default — only set when all conditions are met
            _cursorActive = false;

            if (!ModEntry.Config.EnableCarpenterMenuFix)
            {
                _overridingMousePosition = false;
                return;
            }

            if (OnFarmField == null || FreezeField == null)
            {
                _overridingMousePosition = false;
                return;
            }

            // Only active when on the farm view
            if (!(bool)OnFarmField.GetValue(__instance))
            {
                _cursorCentered = false;
                _overridingMousePosition = false;
                _demolishSelectedBuilding = null;

                // Suppress red highlight box — set _selectedBottomButton to None (0)
                if (SelectedBottomButtonField != null)
                {
                    var enumType = SelectedBottomButtonField.FieldType;
                    SelectedBottomButtonField.SetValue(__instance, Enum.ToObject(enumType, 0));
                }
                if (OnBottomButtonsField != null)
                    OnBottomButtonsField.SetValue(__instance, false);

                // Shop cursor hover — call performHoverAction at current component
                var shopComp = GetShopCurrentComponent(__instance);
                if (shopComp != null)
                    __instance.performHoverAction(shopComp.bounds.Center.X, shopComp.bounds.Center.Y);

                return;
            }

            // Don't move during animations/transitions
            if ((bool)FreezeField.GetValue(__instance))
            {
                _overridingMousePosition = false;
                return;
            }

            // Don't move during grace period
            if (IsInGracePeriod())
            {
                _overridingMousePosition = false;
                return;
            }

            // Don't move during screen fades
            if (Game1.IsFading())
            {
                _overridingMousePosition = false;
                return;
            }

            // Enable cursor — draw postfix will render it, A button will click at its position
            _cursorActive = true;

            // Read building dimensions for ghost centering in GetMouseState_Postfix.
            // In build mode: offset by currentBuilding dimensions (ghost anchor is top-left).
            // In move mode: offset by buildingToMove dimensions (only after selecting a building).
            // In demolish mode: no offset (just clicking on buildings).
            bool isMoving = false, isDemolishing = false;
            if (ActionField != null)
            {
                int action = (int)ActionField.GetValue(__instance);
                isMoving = action == 2;
                isDemolishing = action == 1;
            }
            else
            {
                isMoving = MovingField != null && (bool)MovingField.GetValue(__instance);
                isDemolishing = DemolishingField != null && (bool)DemolishingField.GetValue(__instance);
            }

            if (isMoving)
            {
                var btm = BuildingToMoveField?.GetValue(__instance) as Building;
                _buildingTileWidth = btm?.tilesWide.Value ?? 0;
                _buildingTileHeight = btm?.tilesHigh.Value ?? 0;
            }
            else if (isDemolishing)
            {
                _buildingTileWidth = 0;
                _buildingTileHeight = 0;
            }
            else // Build (None/Upgrade)
            {
                var building = CurrentBuildingField?.GetValue(__instance) as Building;
                _buildingTileWidth = building?.tilesWide.Value ?? 0;
                _buildingTileHeight = building?.tilesHigh.Value ?? 0;
            }

            // Continuous GetMouseState override for all modes on farm view.
            // The game reads mouse position for ghost rendering and placement in every mode.
            // With override always active, the ghost tracks the joystick cursor in real time.
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

            // Move cursor if stick is above deadzone — also resets ghost placement
            if (absX > StickDeadzone || absY > StickDeadzone)
            {
                // Cursor is moving — ghost needs to be repositioned before building
                _ghostPlaced = false;
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

                _cursorX = Math.Max(0, Math.Min(_cursorX + deltaX, Game1.viewport.Width - 1));
                _cursorY = Math.Max(0, Math.Min(_cursorY + deltaY, Game1.viewport.Height - 1));

                int ix = (int)_cursorX;
                int iy = (int)_cursorY;

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
                    Monitor.Log($"[CarpenterMenu] Cursor: ({(int)_cursorX},{(int)_cursorY}) pan=({panX},{panY})", LogLevel.Trace);
            }

            // Set _drawAtX/_drawAtY directly so the ghost tracks the cursor in real time.
            // Without this, _drawAtX only updates from leftClickHeld → TestToPan (touch drag).
            // _cursorX is in game render pixels (viewport-relative), same space as _drawAtX.
            // No zoom conversion needed — both are in the same coordinate space.
            if (DrawAtXField != null && DrawAtYField != null)
            {
                int drawX = (int)_cursorX - _buildingTileWidth * 32;
                int drawY = (int)_cursorY - _buildingTileHeight * 32;
                DrawAtXField.SetValue(__instance, drawX);
                DrawAtYField.SetValue(__instance, drawY);
            }

            // A button handling:
            // Build/upgrade/paint: prefix lets A through → OnClickOK() → tryToBuild() reads _drawAtX/_drawAtY.
            // Move: prefix lets A through for selection, blocks for ghost positioning.
            // Demolish: prefix manages selection/confirm/deselect cycle.
            var gps = Game1.input.GetGamePadState();
            bool aPressed = gps.Buttons.A == ButtonState.Pressed;
            if (aPressed && !_prevAPressed)
            {
                if (_buildPressHandled)
                {
                    // Prefix let receiveGamePadButton(A) through — action was handled by game.
                    _buildPressHandled = false;

                    // For move/demolish: set _ghostPlaced so next A can confirm.
                    // For build: not needed (single-press, game already placed it).
                    if (isMoving || isDemolishing)
                        _ghostPlaced = true;

                    if (ModEntry.Config.VerboseLogging)
                    {
                        var btm = BuildingToMoveField?.GetValue(__instance) as Building;
                        string mode = isMoving ? "MOVE" : isDemolishing ? "DEMOLISH" : "BUILD";
                        Monitor.Log($"[CarpenterMenu] {mode} action handled at ({(int)_cursorX},{(int)_cursorY}) zoom={Game1.options.zoomLevel} buildingToMove={btm?.buildingType.Value ?? "none"}", LogLevel.Debug);
                    }

                    // In demolish mode, detect which building was just selected
                    if (isDemolishing && _demolishSelectedBuilding == null)
                    {
                        _demolishSelectedBuilding = GetBuildingAtCursor();
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"[CarpenterMenu] Demolish: selected building = {_demolishSelectedBuilding?.buildingType.Value ?? "none"}", LogLevel.Debug);
                    }
                }
                else if (isMoving && !isDemolishing)
                {
                    // Move mode: prefix blocked A for ghost positioning.
                    // Fire receiveLeftClick at cursor position for building selection.
                    __instance.receiveLeftClick((int)_cursorX, (int)_cursorY);
                    _ghostPlaced = true;
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"[CarpenterMenu] MOVE ghost positioned at ({(int)_cursorX},{(int)_cursorY}) zoom={Game1.options.zoomLevel}", LogLevel.Debug);
                }
                // else: demolish A handled entirely by prefix (selection/confirm/deselect)
            }
            _prevAPressed = aPressed;
        }

        /// <summary>
        /// Harmony postfix for GetMouseState. Active continuously on farm view
        /// (when _overridingMousePosition is true). Replaces X/Y with cursor position
        /// so the ghost tracks the joystick cursor and placement uses our coordinates.
        /// </summary>
        private static void GetMouseState_Postfix(ref MouseState __result)
        {
            // Global touch simulation: report LeftButton pressed for one tick.
            // Used by cutscene skip to simulate a screen tap when Start is pressed.
            if (GameplayButtonPatches.SimulateTouchClickOnTick == Game1.ticks)
            {
                __result = new MouseState(
                    __result.X, __result.Y,
                    __result.ScrollWheelValue,
                    ButtonState.Pressed,
                    __result.MiddleButton,
                    __result.RightButton,
                    __result.XButton1,
                    __result.XButton2
                );
            }

            if (!_overridingMousePosition)
                return;

            // Convert game-render coordinates to screen pixels for GetMouseState.
            // _cursorX is in game render pixels (viewport-relative). GetMouseState returns
            // screen pixels. The game divides by zoom: _drawAtX = mouseX / zoom.
            // We want _drawAtX = _cursorX - halfBuilding, so:
            //   mouseX / zoom = _cursorX - halfBuilding
            //   mouseX = (_cursorX - halfBuilding) * zoom
            float zoom = Game1.options.zoomLevel;
            int mouseX = (int)((_cursorX - _buildingTileWidth * 32) * zoom);
            int mouseY = (int)((_cursorY - _buildingTileHeight * 32) * zoom);
            __result = new MouseState(
                mouseX, mouseY,
                __result.ScrollWheelValue,
                __result.LeftButton,
                __result.MiddleButton,
                __result.RightButton,
                __result.XButton1,
                __result.XButton2
            );
        }

        /// <summary>
        /// Postfix for CarpenterMenu.draw — draws cursor on both shop screen and farm view.
        /// Shop screen: finger cursor at current nav component + grey out unaffordable Build.
        /// Farm view: arrow cursor at joystick position.
        /// </summary>
        private static void Draw_Postfix(CarpenterMenu __instance, SpriteBatch b)
        {
            bool onFarm = OnFarmField != null && (bool)OnFarmField.GetValue(__instance);

            // Shop screen cursor
            if (!onFarm)
            {
                var component = GetShopCurrentComponent(__instance);
                if (component != null)
                {
                    var center = component.bounds.Center;
                    b.Draw(
                        Game1.mouseCursors,
                        new Vector2(center.X, center.Y),
                        Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 44, 16, 16),
                        Color.White,
                        0f,
                        Vector2.Zero,
                        4f + Game1.dialogueButtonScale / 150f,
                        SpriteEffects.None,
                        1f
                    );
                }

                // Grey out Build button if player can't afford
                var buildBtn = BuildButtonField?.GetValue(__instance) as ClickableComponent;
                if (buildBtn?.visible == true)
                {
                    int price = PriceField != null ? (int)PriceField.GetValue(__instance) : 0;
                    bool canAfford = price >= 0 && Game1.player.Money >= price;
                    bool hasResources = true;
                    if (canAfford && DoesFarmerHaveEnoughResourcesToBuildMethod != null)
                        hasResources = (bool)DoesFarmerHaveEnoughResourcesToBuildMethod.Invoke(__instance, null);

                    if (!canAfford || !hasResources)
                        b.Draw(Game1.staminaRect, buildBtn.bounds, Color.Black * 0.4f);
                }

                return;
            }

            // Farm view cursor — draw in world space (no UI transform) to match
            // the building ghost which is rendered via StartWorldDrawInUI.
            if (!_cursorActive)
                return;

            Game1.StartWorldDrawInUI(b);
            b.Draw(
                Game1.mouseCursors,
                new Vector2(_cursorX, _cursorY),
                Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 0, 16, 16),
                Color.White,
                0f,
                Vector2.Zero,
                4f + Game1.dialogueButtonScale / 150f,
                SpriteEffects.None,
                1f
            );
            Game1.EndWorldDrawInUI(b);
        }

        // ====================================================================
        // BuildingSkinMenu cursor patches (#34)
        // ====================================================================

        /// <summary>Prefix for BuildingSkinMenu.receiveGamePadButton — 2-row cursor navigation.
        /// Layout:  Row 0: [Prev (col 0), Next (col 1)]
        ///          Row 1: [OK (col 0, visually under Next)]</summary>
        private static bool SkinMenu_ReceiveGamePadButton_Prefix(IClickableMenu __instance, Buttons button)
        {
            // Reset nav when a new skin menu opens
            int hash = __instance.GetHashCode();
            if (hash != _lastSkinMenuHash)
            {
                _lastSkinMenuHash = hash;
                _skinRow = 1;  // default to OK
                _skinCol = 0;
            }

            // B, LT/RT/LB/RB: let game handle
            if (button == Buttons.B || button == Buttons.LeftTrigger || button == Buttons.RightTrigger
                || button == Buttons.LeftShoulder || button == Buttons.RightShoulder)
                return true;

            bool isLeft = button == Buttons.DPadLeft || button == Buttons.LeftThumbstickLeft;
            bool isRight = button == Buttons.DPadRight || button == Buttons.LeftThumbstickRight;
            bool isUp = button == Buttons.DPadUp || button == Buttons.LeftThumbstickUp;
            bool isDown = button == Buttons.DPadDown || button == Buttons.LeftThumbstickDown;

            if (isUp)
            {
                if (_skinRow == 1)
                {
                    _skinRow = 0;
                    _skinCol = 1; // OK is under Next arrow
                    Game1.playSound("smallSelect");
                }
                return false;
            }

            if (isDown)
            {
                if (_skinRow == 0)
                {
                    _skinRow = 1;
                    _skinCol = 0;
                    Game1.playSound("smallSelect");
                }
                return false;
            }

            if (isLeft)
            {
                if (_skinRow == 0)
                {
                    if (_skinCol > 0)
                    {
                        _skinCol--;
                        Game1.playSound("smallSelect");
                    }
                    else
                    {
                        // At prev button edge — cycle to previous skin
                        var leftBtn = SkinLeftButtonField?.GetValue(__instance) as ClickableComponent;
                        if (leftBtn != null)
                            __instance.receiveLeftClick(leftBtn.bounds.Center.X, leftBtn.bounds.Center.Y);
                    }
                }
                else // row 1 (OK)
                {
                    // Left from OK → go to prev arrow
                    _skinRow = 0;
                    _skinCol = 0;
                    Game1.playSound("smallSelect");
                }
                return false;
            }

            if (isRight)
            {
                if (_skinRow == 0)
                {
                    if (_skinCol < 1)
                    {
                        _skinCol++;
                        Game1.playSound("smallSelect");
                    }
                    else
                    {
                        // At next button edge — cycle to next skin
                        var rightBtn = SkinRightButtonField?.GetValue(__instance) as ClickableComponent;
                        if (rightBtn != null)
                            __instance.receiveLeftClick(rightBtn.bounds.Center.X, rightBtn.bounds.Center.Y);
                    }
                }
                else // row 1 (OK)
                {
                    // Right from OK → go to next arrow
                    _skinRow = 0;
                    _skinCol = 1;
                    Game1.playSound("smallSelect");
                }
                return false;
            }

            if (button == Buttons.A)
            {
                ClickableComponent btn = null;
                if (_skinRow == 0)
                    btn = _skinCol == 0
                        ? SkinLeftButtonField?.GetValue(__instance) as ClickableComponent
                        : SkinRightButtonField?.GetValue(__instance) as ClickableComponent;
                else
                    btn = SkinOkButtonField?.GetValue(__instance) as ClickableComponent;

                if (btn != null)
                    __instance.receiveLeftClick(btn.bounds.Center.X, btn.bounds.Center.Y);
                return false;
            }

            return true;
        }

        /// <summary>Postfix for BuildingSkinMenu.draw — draws finger cursor at current component.</summary>
        private static void SkinMenu_Draw_Postfix(IClickableMenu __instance, SpriteBatch b)
        {
            ClickableComponent btn = null;
            if (_skinRow == 0)
                btn = _skinCol == 0
                    ? SkinLeftButtonField?.GetValue(__instance) as ClickableComponent
                    : SkinRightButtonField?.GetValue(__instance) as ClickableComponent;
            else
                btn = SkinOkButtonField?.GetValue(__instance) as ClickableComponent;

            if (btn == null) return;

            var center = btn.bounds.Center;
            b.Draw(
                Game1.mouseCursors,
                new Vector2(center.X, center.Y),
                Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 44, 16, 16),
                Color.White,
                0f,
                Vector2.Zero,
                4f + Game1.dialogueButtonScale / 150f,
                SpriteEffects.None,
                1f
            );
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
