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
    /// Remaps controller buttons in menus based on controller layout.
    /// A/B swap is handled at the GamePad.GetState level (GameplayButtonPatches).
    /// X/Y swap in menus is handled here based on layout.
    /// </summary>
    internal static class ButtonRemapper
    {
        /// <summary>
        /// Determines if X and Y buttons should be swapped based on LAYOUT.
        /// X/Y functions are positional: Top=Sort, Left=AddToStacks (same as gameplay).
        /// Switch layout: X=top, Y=left - no swap needed (X=Sort, Y=AddToStacks)
        /// Xbox/PS layout: X=left, Y=top - swap needed (Y=Sort, X=AddToStacks)
        /// </summary>
        private static bool ShouldSwapXY()
        {
            if (ModEntry.Config?.EnableButtonRemapping == false)
                return false;

            var layout = ModEntry.Config?.ControllerLayout ?? ControllerLayout.Switch;
            return layout == ControllerLayout.Xbox || layout == ControllerLayout.PlayStation;
        }

        /// <summary>Remap an XNA Buttons value based on the current settings.</summary>
        public static Buttons Remap(Buttons button)
        {
            bool swapXY = ShouldSwapXY();

            return button switch
            {
                Buttons.X when swapXY => Buttons.Y,
                Buttons.Y when swapXY => Buttons.X,
                _ => button
            };
        }

        /// <summary>Remap an SMAPI SButton value based on the current settings.</summary>
        public static SButton Remap(SButton button)
        {
            bool swapXY = ShouldSwapXY();

            return button switch
            {
                SButton.ControllerX when swapXY => SButton.ControllerY,
                SButton.ControllerY when swapXY => SButton.ControllerX,
                _ => button
            };
        }
    }
}
