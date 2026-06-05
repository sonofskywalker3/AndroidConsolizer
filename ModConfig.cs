using StardewModdingAPI;

namespace AndroidConsolizer
{
    /// <summary>The mod configuration model.</summary>
    public class ModConfig
    {
        /*********
        ** Controller Settings
        *********/
        /// <summary>Whether to remap buttons based on controller layout and control style. When disabled, all buttons pass through unmodified.</summary>
        public bool EnableButtonRemapping { get; set; } = true;

        /// <summary>
        /// Physical button layout of your controller.
        /// Switch/Odin: A=right, B=bottom, X=top, Y=left.
        /// Xbox/PlayStation: A=bottom, B=right, X=left, Y=top.
        /// </summary>
        public ControllerLayout ControllerLayout { get; set; } = ControllerLayout.Switch;

        /// <summary>
        /// Which console's control scheme you want to use.
        /// Switch: Right=confirm, Bottom=cancel.
        /// Xbox/PS: Bottom=confirm, Right=cancel.
        /// </summary>
        public ControlStyle ControlStyle { get; set; } = ControlStyle.Switch;

        /*********
        ** Feature Toggles
        *********/
        /// <summary>Console-style chest controls: sort (X), fill stacks (Y), sidebar navigation, color picker, A/Y item transfer.</summary>
        public bool EnableConsoleChests { get; set; } = true;

        /// <summary>Console-style shop controls: A button purchases, quantity selector, sell tab, right stick scroll.</summary>
        public bool EnableConsoleShops { get; set; } = true;

        /// <summary>Console-style toolbar: 12-slot fixed toolbar with LB/RB row switching and LT/RT slot movement.</summary>
        public bool EnableConsoleToolbar { get; set; } = true;

        /// <summary>
        /// Steer new inventory items toward the active toolbar row.
        /// Furniture: place into the active row if it has space; auto-select after pickup
        /// (matches vanilla). If the active row is full, furniture lands wherever the game
        /// puts it AND the visible row + selection follow it (so you can immediately re-place).
        /// Non-furniture (forage, drops, gifts, shop purchases): land in the active row if
        /// it has space; otherwise fall through to vanilla placement. Selection and visible
        /// row are never disrupted for non-furniture, so you keep using your current tool.
        /// </summary>
        public bool EnablePickupToActiveRow { get; set; } = true;

        /// <summary>Console-style inventory: A picks up/places items, Y picks up one, fishing rod bait/tackle via Y.</summary>
        public bool EnableConsoleInventory { get; set; } = true;

        /// <summary>Console-style shipping bin: A ships full stack, Y ships one item.</summary>
        public bool EnableConsoleShipping { get; set; } = true;

        /// <summary>
        /// Console right-stick cursor: the right thumbstick moves an on-screen mouse cursor that
        /// aims tools/interaction at the cursor tile (Switch parity), auto-hiding after ~4s of no
        /// input (reverting tools to the facing tile). When false, the right stick is zeroed in the
        /// overworld (no cursor drift) — the pre-v4.0 behaviour.
        /// </summary>
        public bool EnableRightStickCursor { get; set; } = true;

        /*********
        ** Standalone Features
        *********/
        /// <summary>When true, tap Start opens the game menu (vanilla behaviour) and holding
        /// Start for ~500ms opens the Quest Log/Journal. When false, Start is left alone.</summary>
        public bool EnableJournalButton { get; set; } = true;

        /// <summary>Whether Start button can skip cutscenes (press twice to skip).</summary>
        public bool EnableCutsceneSkip { get; set; } = true;

        /// <summary>Whether to enable the carpenter menu fix (prevents Robin's building menu from instantly closing).</summary>
        public bool EnableCarpenterMenuFix { get; set; } = true;

        /// <summary>Whether to debounce furniture Y-button interactions (prevents rapid toggle between placed and picked up).</summary>
        public bool EnableFurnitureDebounce { get; set; } = true;

        /// <summary>
        /// Console-style two-press geode breaking at Clint. First A/X places the
        /// selected geode visibly on the anvil; second A/X starts the crack. B
        /// with anvil occupied cancels the placement (instead of closing).
        /// Vanilla Android uses one-press X that places + cracks atomically with
        /// no "geode on anvil" visual; A toggles a tooltip. Disable to restore
        /// vanilla behaviour.
        /// </summary>
        public bool EnableConsoleGeodeMenu { get; set; } = true;

        /// <summary>
        /// Replace the multi-tile green-square placement map (which marks every tile where the
        /// furniture's top-left corner can land — confusing for multi-tile pieces like beds)
        /// with a single colored ghost rectangle that shows exactly where the furniture will
        /// land. Reuses Object.DrawRedGreenRectangleForPlacing, which already exists in the
        /// engine but isn't activated on Android.
        /// </summary>
        public bool EnableConsoleFurniturePlacement { get; set; } = true;

        /// <summary>
        /// #68: Same single-ghost treatment as furniture, but for placeable CRAFTABLES
        /// (machines, kegs, preserves jars, sprinklers, etc. — plain Object / BigCraftable).
        /// On Android with a controller these otherwise show the full multi-tile green map
        /// over every valid tile, which is cluttered and useless. Replaces it with one ghost
        /// rectangle at the tile the craftable will actually land on. Sibling toggle so users
        /// can opt out per-category (separate from furniture).
        /// </summary>
        public bool EnableConsoleCraftablePlacement { get; set; } = true;

        /// <summary>
        /// #69: Hold A on the crafting/cooking menu to craft continuously (console parity),
        /// mirroring hold-to-buy/sell and hold-Y single-stack transfers. Android crafts one
        /// per A press; the quantity slider is only adjustable by touch, so this gives the
        /// controller a rapid-craft. Self-limits when ingredients or inventory space run out.
        /// </summary>
        public bool EnableHoldToCraft { get; set; } = true;

        /// <summary>
        /// #18: Controller support for the museum donation menu. On Android the
        /// game's own console snap navigation (inventory selection, D-pad museum-grid
        /// movement, A to place) is gated behind SnappyMenus, which is false on
        /// Android — so a controller can't select or place donations. When true, we
        /// turn SnappyMenus on only while the donation menu is open and let the
        /// game's own code drive it. Disable to restore vanilla touch-only behaviour.
        /// </summary>
        public bool EnableMuseumDonationController { get; set; } = true;

        /// <summary>Whether to fix controller navigation on GameMenu tabs (Social, Animals, Crafting, Collections, Options).</summary>
        public bool EnableGameMenuNavigation { get; set; } = true;

        /// <summary>
        /// #25: Holding an upgraded (Copper+) Hoe or Watering Can while moving charges the
        /// area effect (console parity) instead of rapid-firing single uses and locking
        /// movement. The character slides freely during the charge; release fires the charged
        /// area. A quick tap still performs one normal single use. Basic (level-0) tools and
        /// Pickaxe/Axe are unaffected. Disable to restore vanilla Android behavior.
        /// </summary>
        public bool EnableMoveWhileCharging { get; set; } = true;

        /// <summary>
        /// #25b: Console-parity slingshot aim. On Android the tap-to-move / mobile-input layer
        /// hijacks the slingshot — stick motion alone fires it, holding the tool button auto-fires
        /// repeatedly, and movement is blocked. When true, the slingshot is driven only by the
        /// physical tool button: hold to draw and aim (left stick swings the crosshair), release to
        /// fire once, and the stick moves you normally when you're not aiming. Disable to restore
        /// vanilla Android behaviour.
        /// </summary>
        public bool EnableSlingshotAim { get; set; } = true;

        /// <summary>
        /// #74: Console-style drop blocker. On Android, dropping an item lets you instantly re-grab
        /// it (often before you've left the menu) because the vanilla `Debris.DroppedByPlayerID`
        /// exclusion is never set. When true, deliberate player drops tag the dropper so the engine's
        /// own 1200ms exclusion engages — your own drop won't fly straight back. Monster loot, harvest,
        /// and other debris are unaffected. Disable to restore vanilla Android instant re-pickup.
        /// </summary>
        public bool EnableConsoleDropBlocker { get; set; } = true;

        /// <summary>
        /// #78: Console-style dialogue question selection. Vanilla Android marks the selected response
        /// in a question box (sleep Yes/No, NPC yes/no, festival/event choices) with a yellow highlight
        /// box + faded text. When true, the selected response instead gets the console look: the plain
        /// box (no yellow), full-strength text, a red outline matching the tool-hit box, and the regular
        /// finger cursor at its bottom-right corner. Controller only; touch/mouse keeps vanilla.
        /// </summary>
        public bool EnableConsoleDialogueCursor { get; set; } = true;

        /// <summary>Whether to use free cursor (vanilla) instead of snap navigation on the Options page and GMCM config page.</summary>
        public bool FreeCursorOnSettings { get; set; } = false;

        /// <summary>Hide the touch-only options (virtual-joystick Controls dropdown, on-screen controls
        /// toggle, invisible-button width, pinch-zoom, and the Adjust-joypad-controls button) from the
        /// in-game Options page when a controller is active. They're meaningless with a physical
        /// controller. Default on.</summary>
        public bool HideTouchOptionsWithController { get; set; } = true;

        /// <summary>
        /// Use bumpers (LB/RB) instead of triggers (LT/RT) for controls.
        /// Toolbar: D-Pad Up/Down switches rows, bumpers move within row.
        /// Shops: Bumpers adjust purchase quantity.
        /// For controllers where Stardew Valley can't read the triggers (e.g., Xbox via Bluetooth on Android).
        /// </summary>
        public bool UseBumpersInsteadOfTriggers { get; set; } = false;

        /*********
        ** Debug Settings
        *********/
        /// <summary>Whether to log verbose debug information.</summary>
        public bool VerboseLogging { get; set; } = false;
    }
}
