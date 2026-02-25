using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;

namespace AndroidConsolizer
{
    /// <summary>Physical button layout of the controller.</summary>
    public enum ControllerLayout
    {
        /// <summary>Switch/Odin layout: A=right, B=bottom, X=top, Y=left.</summary>
        Switch,
        /// <summary>Xbox layout: A=bottom, B=right, X=left, Y=top.</summary>
        Xbox,
        /// <summary>PlayStation layout: Same positions as Xbox (Cross=A, Circle=B, Square=X, Triangle=Y).</summary>
        PlayStation
    }

    /// <summary>Desired control style (which console's button behavior to emulate).</summary>
    public enum ControlStyle
    {
        /// <summary>Switch-style controls: Right=confirm, Bottom=cancel.</summary>
        Switch,
        /// <summary>Xbox/PS-style controls: Bottom=confirm, Right=cancel.</summary>
        Xbox
    }

    /// <summary>
    /// Remaps controller buttons based on controller layout.
    /// A/B swap is handled at the GamePad.GetState level (GameplayButtonPatches).
    /// X/Y swap is also handled at GetState level now — Switch swaps in gameplay,
    /// Xbox/PS swaps in menus. ButtonRemapper is now a pass-through.
    /// </summary>
    internal static class ButtonRemapper
    {
        /// <summary>Remap an XNA Buttons value based on the current settings.
        /// X/Y swapping is now handled at the GetState level, so this is a pass-through.</summary>
        public static Buttons Remap(Buttons button)
        {
            return button;
        }

        /// <summary>Remap an SMAPI SButton value based on the current settings.
        /// X/Y swapping is now handled at the GetState level, so this is a pass-through.</summary>
        public static SButton Remap(SButton button)
        {
            return button;
        }
    }
}
