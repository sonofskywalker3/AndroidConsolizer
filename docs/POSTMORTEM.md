# v2.7.10 Regression Post-Mortem

## What Happened
v2.7.10 was supposed to be a "refactoring only, no behavior changes" release that extracted decision logic from patch files into a new `Logic/` directory. It introduced a regression in inventory management that made the A-button behavior broken. The bug persisted through all subsequent versions (v2.7.10 through v2.9.2).

## Why It Wasn't Caught
- **No commits were made** between v2.7.0 and v2.9.2 (~40 builds). When the regression was discovered, there was no source code history to bisect.
- **Multiple changes were bundled** in the same version — the refactoring + decision logging + new types were all in v2.7.10.
- **The refactoring was assumed safe** because the logic was "the same" — but decompilation reveals subtle differences in control flow.

## What Changed in InventoryManagementPatches.cs (v2.7.9 -> v2.7.10)
Key differences found by decompiling the v2.7.9 and v2.7.10 DLLs:

1. **Removed `InventoryPage_Draw_Prefix`** — Empty prefix was removed, changing Harmony patch registration from prefix+postfix to postfix-only. Shouldn't matter but changed the patch chain.

2. **Replaced direct switch/case with enum dispatch** — The A-button handler went from:
   ```csharp
   // v2.7.9 (WORKING)
   switch (myID) {
     case 105: /* trash */ break;
     case 106: /* sort */ break;
     default:
       if (IsHoldingItem) return PlaceItem(...);
       if (!isInventorySlot) return false;
       if (Items[myID] == null) return false;
       return PickUpItem(...);
   }
   ```
   to:
   ```csharp
   // v2.7.10 (BROKEN)
   InventoryAction action = InventoryLogic.DetermineAButtonAction(myID, ...);
   switch (action) {
     case Trash: /* trash */ break;
     case Sort: /* sort */ break;
     case Place/Swap/Stack: return PlaceItem(...);
     case PickUp: return PickUpItem(...);
     default: return myID == 105;
   }
   ```
   The refactored version pre-calculates stacking info before deciding the action, and the enum dispatch changes the order of checks subtly.

3. **Removed trash can lid animation code** from the draw postfix entirely.

4. **Changed stacking to use `InventoryLogic.CalculateStack()`** — While mathematically identical, the delegation changes the call site.

## Lessons Learned
- **NEVER refactor and change behavior in the same commit** — even "pure refactoring" can introduce bugs
- **ALWAYS commit every version** — if v2.7.10 had been committed, we could have reverted just that one patch
- **ONE change per version** — if v2.7.10 had been ONLY the refactoring (no decision logging, no new types), it would have been easier to test
- **Keep inline logic that works** — extracting working code into helper classes for "cleanliness" is not worth the regression risk
- **The v2.7.9 DLL's decompiled InventoryManagementPatches.cs is the gold standard** — when re-implementing features, do NOT restructure this file's A-button handling
- **DO NOT re-implement the Logic/ extraction (TODO item #5).** The inline code works. Leave it inline.
