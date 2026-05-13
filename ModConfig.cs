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
        /// Stops the right thumbstick from moving the mouse cursor during overworld gameplay.
        /// Vanilla Android maps the right stick to cursor motion, which causes interact/sickle
        /// to target tiles many squares away from the player. Disable to restore vanilla behavior.
        /// </summary>
        public bool SuppressRightStickInOverworld { get; set; } = true;

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
        /// Replace the multi-tile green-square placement map (which marks every tile where the
        /// furniture's top-left corner can land — confusing for multi-tile pieces like beds)
        /// with a single colored ghost rectangle that shows exactly where the furniture will
        /// land. Reuses Object.DrawRedGreenRectangleForPlacing, which already exists in the
        /// engine but isn't activated on Android.
        /// </summary>
        public bool EnableConsoleFurniturePlacement { get; set; } = true;

        /// <summary>Whether to fix controller navigation on GameMenu tabs (Social, Animals, Crafting, Collections, Options).</summary>
        public bool EnableGameMenuNavigation { get; set; } = true;

        /// <summary>Whether to use free cursor (vanilla) instead of snap navigation on the Options page and GMCM config page.</summary>
        public bool FreeCursorOnSettings { get; set; } = false;

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
