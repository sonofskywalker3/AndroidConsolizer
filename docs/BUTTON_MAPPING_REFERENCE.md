# Android Consolizer - Complete Button Mapping Reference

This document defines what every button should do for every combination of Layout and Style settings.

---

## Understanding the Settings

### Controller Layout (Physical Button Positions)

This tells the mod where the buttons physically are on your controller.

| Layout | A Position | B Position | X Position | Y Position |
|--------|------------|------------|------------|------------|
| **Switch/Odin** | Right | Bottom | Top | Left |
| **Xbox** | Bottom | Right | Left | Top |
| **PlayStation** | Bottom (Cross) | Right (Circle) | Left (Square) | Top (Triangle) |

### Control Style (Desired Behavior)

This defines what you want each button **action** to be. The mod will remap buttons to achieve this behavior on your controller.

| Style | Confirm / Talk / Pickup | Cancel / Inventory | Use Tool | Crafting Menu |
|-------|-------------------------|-------------------|----------|---------------|
| **Switch** | Right button | Bottom button | Left button | Top button |
| **Xbox** | Bottom button | Right button | Top button | Left button |

---

## How Remapping Works

The mod compares your **Layout** (where buttons physically are) with your **Style** (what behavior you want) and swaps buttons internally when needed.

### A/B Button Remapping Logic

| Layout | Style | A/B Swap? | Reason |
|--------|-------|-----------|--------|
| Switch | Switch | **NO** | Layout matches style |
| Switch | Xbox | **YES** | Want Xbox behavior on Switch controller |
| Xbox | Switch | **YES** | Want Switch behavior on Xbox controller |
| Xbox | Xbox | **NO** | Layout matches style |
| PlayStation | Switch | **YES** | Want Switch behavior on PS controller |
| PlayStation | Xbox | **NO** | Layout matches style |

### X/Y Button Remapping Logic (GAMEPLAY ONLY)

During **gameplay** (walking around, no menu open), X and Y control Use Tool and Crafting Menu.

Android's default: X=Tool, Y=Craft

| Layout | Style | X/Y Swap? | Reason |
|--------|-------|-----------|--------|
| Switch | Switch | **YES** | Android default matches Xbox style, need to swap for Switch style |
| Switch | Xbox | **NO** | Android default already matches Xbox style |
| Xbox | Switch | **NO** | Xbox X/Y positions + Android default = correct Switch style |
| Xbox | Xbox | **YES** | Xbox X/Y positions need swap to match Xbox style |
| PlayStation | Switch | **NO** | Same as Xbox + Switch |
| PlayStation | Xbox | **YES** | Same as Xbox + Xbox |

**Note:** X/Y swap logic is OPPOSITE of A/B swap logic because X and Y have different position relationships between layouts.

**Important:** In **menus**, X and Y are NOT swapped. Menu actions are always:
- **X button** = Sort
- **Y button** = Add to Stacks (chest), Ship One (shipping bin)

---

## Complete Button Mappings By Combination

### Combination 1: Switch Layout + Switch Style

**A/B: NO swap | X/Y: YES swap**

#### Physical Button Positions (Switch/Odin)
```
        [X]              TOP = Crafting Menu
   [Y]      [A]     LEFT = Use Tool    RIGHT = Confirm
        [B]            BOTTOM = Cancel/Inventory
```

#### Actions by Context

| Context | A (Right) | B (Bottom) | X (Top) | Y (Left) |
|---------|-----------|------------|---------|----------|
| **Gameplay** | Confirm/Talk/Pickup | Cancel/Open Inventory | Open Crafting Menu | Use Tool |
| **Shop** | Purchase Item | Exit Shop | - | - |
| **Chest** | - | Exit Chest | Sort Chest | Add to Stacks |
| **Inventory** | - | Close Menu | Sort Inventory | - |
| **Shipping Bin** | Ship Stack | Exit | - | Ship One |

| Bumpers/Triggers | Action |
|------------------|--------|
| **LB** | Previous Toolbar Row |
| **RB** | Next Toolbar Row |
| **LT** | Move Selection Left |
| **RT** | Move Selection Right |

---

### Combination 2: Switch Layout + Xbox Style

**A/B: YES swap | X/Y: NO swap**

#### Physical Button Positions (Switch/Odin) - Actions Remapped
```
        [X]              TOP = Use Tool (Xbox style: Top=tool)
   [Y]      [A]     LEFT = Crafting Menu (Xbox style: Left=craft)    RIGHT = Cancel (Xbox style: Right=cancel)
        [B]            BOTTOM = Confirm (Xbox style: Bottom=confirm)
```

#### Actions by Context

| Context | A (Right) | B (Bottom) | X (Top) | Y (Left) |
|---------|-----------|------------|---------|----------|
| **Gameplay** | Cancel/Open Inventory | Confirm/Talk/Pickup | Use Tool | Open Crafting Menu |
| **Shop** | Exit Shop | Purchase Item | - | - |
| **Chest** | Exit Chest | - | Sort Chest | Add to Stacks |
| **Inventory** | Close Menu | - | Sort Inventory | - |
| **Shipping Bin** | Exit | Ship Stack | - | Ship One |

| Bumpers/Triggers | Action |
|------------------|--------|
| **LB** | Previous Toolbar Row |
| **RB** | Next Toolbar Row |
| **LT** | Move Selection Left |
| **RT** | Move Selection Right |

---

### Combination 3: Xbox Layout + Switch Style

**A/B: YES swap | X/Y: NO swap**

#### Physical Button Positions (Xbox)
```
        [Y]              TOP = Crafting Menu (Switch style: Top=craft)
   [X]      [B]     LEFT = Use Tool (Switch style: Left=tool)    RIGHT = Confirm (Switch style: Right=confirm)
        [A]            BOTTOM = Cancel (Switch style: Bottom=cancel)
```

#### Actions by Context

| Context | A (Bottom) | B (Right) | X (Left) | Y (Top) |
|---------|------------|-----------|----------|---------|
| **Gameplay** | Cancel/Open Inventory | Confirm/Talk/Pickup | Use Tool | Open Crafting Menu |
| **Shop** | Exit Shop | Purchase Item | - | - |
| **Chest** | Exit Chest | - | Sort Chest | Add to Stacks |
| **Inventory** | Close Menu | - | Sort Inventory | - |
| **Shipping Bin** | Exit | Ship Stack | - | Ship One |

| Bumpers/Triggers | Action |
|------------------|--------|
| **LB** | Previous Toolbar Row |
| **RB** | Next Toolbar Row |
| **LT** | Move Selection Left |
| **RT** | Move Selection Right |

---

### Combination 4: Xbox Layout + Xbox Style

**A/B: NO swap | X/Y: YES swap**

#### Physical Button Positions (Xbox)
```
        [Y]              TOP = Use Tool
   [X]      [B]     LEFT = Crafting Menu    RIGHT = Cancel
        [A]            BOTTOM = Confirm
```

#### Actions by Context

| Context | A (Bottom) | B (Right) | X (Left) | Y (Top) |
|---------|------------|-----------|----------|---------|
| **Gameplay** | Confirm/Talk/Pickup | Cancel/Open Inventory | Open Crafting Menu | Use Tool |
| **Shop** | Purchase Item | Exit Shop | - | - |
| **Chest** | - | Exit Chest | Sort Chest | Add to Stacks |
| **Inventory** | - | Close Menu | Sort Inventory | - |
| **Shipping Bin** | Ship Stack | Exit | - | Ship One |

| Bumpers/Triggers | Action |
|------------------|--------|
| **LB** | Previous Toolbar Row |
| **RB** | Next Toolbar Row |
| **LT** | Move Selection Left |
| **RT** | Move Selection Right |

---

### Combination 5: PlayStation Layout + Switch Style

**A/B: YES swap | X/Y: NO swap** (same as Xbox + Switch)

#### Physical Button Positions (PlayStation)
```
        [Triangle]       TOP = Crafting Menu
   [Square]    [Circle]  LEFT = Use Tool    RIGHT = Confirm
        [Cross]          BOTTOM = Cancel
```

#### Actions by Context

| Context | Cross (Bottom) | Circle (Right) | Square (Left) | Triangle (Top) |
|---------|----------------|----------------|---------------|----------------|
| **Gameplay** | Cancel/Open Inventory | Confirm/Talk/Pickup | Use Tool | Open Crafting Menu |
| **Shop** | Exit Shop | Purchase Item | - | - |
| **Chest** | Exit Chest | - | Sort Chest | Add to Stacks |
| **Inventory** | Close Menu | - | Sort Inventory | - |
| **Shipping Bin** | Exit | Ship Stack | - | Ship One |

| Bumpers/Triggers | Action |
|------------------|--------|
| **LB (L1)** | Previous Toolbar Row |
| **RB (R1)** | Next Toolbar Row |
| **LT (L2)** | Move Selection Left |
| **RT (R2)** | Move Selection Right |

---

### Combination 6: PlayStation Layout + Xbox Style

**A/B: NO swap | X/Y: YES swap** (same as Xbox + Xbox)

#### Physical Button Positions (PlayStation)
```
        [Triangle]       TOP = Use Tool
   [Square]    [Circle]  LEFT = Crafting Menu    RIGHT = Cancel
        [Cross]          BOTTOM = Confirm
```

#### Actions by Context

| Context | Cross (Bottom) | Circle (Right) | Square (Left) | Triangle (Top) |
|---------|----------------|----------------|---------------|----------------|
| **Gameplay** | Confirm/Talk/Pickup | Cancel/Open Inventory | Open Crafting Menu | Use Tool |
| **Shop** | Purchase Item | Exit Shop | - | - |
| **Chest** | - | Exit Chest | Sort Chest | Add to Stacks |
| **Inventory** | - | Close Menu | Sort Inventory | - |
| **Shipping Bin** | Ship Stack | Exit | - | Ship One |

| Bumpers/Triggers | Action |
|------------------|--------|
| **LB (L1)** | Previous Toolbar Row |
| **RB (R1)** | Next Toolbar Row |
| **LT (L2)** | Move Selection Left |
| **RT (R2)** | Move Selection Right |

---

## Quick Reference Tables

### What Each Action Should Be Mapped To

| Action | Switch Style Position | Xbox Style Position |
|--------|----------------------|---------------------|
| Confirm / Talk / Pickup | Right | Bottom |
| Cancel / Open Inventory | Bottom | Right |
| Use Tool | Left | Top |
| Open Crafting Menu | Top | Left |

### Menu Button Actions (After Remapping)

These are the **logical actions** that should happen, regardless of which physical button triggers them:

| Menu Context | Confirm Action | Cancel Action | X Action | Y Action |
|--------------|----------------|---------------|----------|----------|
| **Shop** | Purchase Item | Exit Shop | - | - |
| **Chest** | - | Exit Chest | Sort | Add to Stacks |
| **Inventory** | - | Close Menu | Sort | - |
| **Shipping Bin** | Ship Stack | Exit | - | Ship One |

### Toolbar Navigation (Never Remapped)

| Button | Action |
|--------|--------|
| **LB** | Previous toolbar row (wraps around) |
| **RB** | Next toolbar row (wraps around) |
| **LT** | Move selection left in row (wraps around) |
| **RT** | Move selection right in row (wraps around) |

---

## Implementation Notes

### For the Code

When implementing, remember:

1. **A/B swap** affects: Confirm, Cancel, Purchase, Exit actions (in ALL contexts)
2. **X/Y swap for GAMEPLAY only**: Use Tool, Crafting Menu are swapped based on layout/style
3. **Menu X/Y actions are NEVER swapped**: Sort is always X button, Add to Stacks/Ship One is always Y button
4. **Shipping Bin** uses Confirm button (A after remapping) for Ship Stack, Y for Ship One

### Architecture

- `ButtonRemapper.cs` handles A/B swapping for menu patches (ShouldSwapXY always returns false)
- `GameplayButtonPatches.cs` handles X/Y swapping during gameplay based on layout/style
- This separation ensures menu actions stay consistent while gameplay feels correct for each style

### Swap Logic Summary

**A/B Swap:** Swap when layout ≠ style
```
isXboxLayout != (style == ControlStyle.Xbox)
```

**X/Y Swap:** Swap when layout = style (OPPOSITE of A/B!)
```
isXboxLayout == (style == ControlStyle.Xbox)
```

### The Key Insight

- **Style** determines which POSITION does which ACTION
- **Layout** tells us which PHYSICAL BUTTON is at each POSITION
- If Layout and Style don't match, we swap so the correct POSITION does the correct ACTION

Example:
- You have Xbox controller (A at bottom)
- You want Switch style (confirm at right position)
- Pressing B (which is at right position on Xbox) should act as confirm
- So we swap: B press → treated as A internally
