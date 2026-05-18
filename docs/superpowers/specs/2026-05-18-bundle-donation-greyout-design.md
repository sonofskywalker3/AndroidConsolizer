# Design: Grey Out Non-Donatable Items on Bundle Donation Page (#46)

**Date:** 2026-05-18
**Milestone:** v3.8.0 — Console Parity: Quick Wins
**TODO item:** #46 — Grey Out Non-Donatable Items on Bundle Page
**Target version:** v3.7.20
**Test device:** Ayaneo Pocket Air Mini (primary)

---

## Problem

`TODO.md` #46: "When a bundle donation page is open, items in inventory
that cannot be donated to that bundle should be greyed out."

On the donation page (open a CC room bundle from the JunimoNote menu),
the inventory shows every item at full brightness regardless of whether
the item can be donated to that specific bundle. The player has to
remember bundle requirements (or scroll the ingredient list above) and
mentally filter their inventory. Console SDV greys out the non-matching
items so the valid donations are visually obvious.

## Decision (user-confirmed during brainstorm)

- **Stack-count rule:** **identity-only**. Items match if they could
  ever satisfy any uncompleted ingredient of the active bundle,
  regardless of current stack size. A partial wheat stack stays
  highlighted even if the bundle requires 5 and the player has 3 —
  the player can see "this is the right type, I need more."
  Implementation: `Bundle.canAcceptThisItem(item, null, ignore_stack_count: true)`.
- **Gate:** active only while `specificBundlePage = True` AND
  `currentPageBundle != null`. Restores vanilla `highlightMethod` on
  donation-page exit.
- **No GMCM toggle.** Same scoping decision as #44 (zero-price items on
  sell tab) — ships always-on; add a toggle later only if someone
  complains. Console-parity feature, low downside.

## Approach (chosen: A)

**A. Install `highlightMethod` on donation-page entry; restore on exit.**
Same pattern as #44 (`ShopMenuPatches.cs:1045-1061`,
`HighlightItemToSellWithPriceCheck`). The `_onDonationPage` state in
`JunimoNoteMenuPatches.cs` already tracks the entry/exit transitions
we need; piggy-back on those instead of adding new lifecycle hooks.

Rejected alternatives:

- **B. Per-frame override in Update_Postfix.** Reassigns
  `highlightMethod` every tick on the donation page. Bulletproof against
  the menu reassigning the field mid-life, but #44's experience showed
  one-shot install + restore is sufficient — no evidence
  `JunimoNoteMenu` ever rewrites `inventory.highlightMethod` once the
  donation page opens.
- **C. Patch `InventoryMenu.draw` to apply a custom greyout filter.**
  Bypasses `highlightMethod` entirely, more invasive, and breaks the
  established pattern. No upside.

## Implementation

Single file change: `Patches/JunimoNoteMenuPatches.cs`. No new patch
file, no new Apply registration.

### Reflection

`Bundle currentPageBundle` is private on `JunimoNoteMenu` (decompile
line 100). Cache the FieldInfo at `Apply()` time alongside the existing
`_specificBundlePageField`:

```csharp
private static FieldInfo _currentPageBundleField;
// in Apply():
_currentPageBundleField = AccessTools.Field(typeof(JunimoNoteMenu), "currentPageBundle");
```

If reflection fails (`_currentPageBundleField == null`), log Warn at
Apply and skip the greyout install entirely (the rest of the file's
patches keep working — this is one feature among many in the file).

### highlightMethod swap

Hook into the existing donation-page entry/exit. The current file
already toggles `_onDonationPage` based on `specificBundlePage`
inspection (see `IsOnDonationPage` helper, JunimoNoteMenuPatches.cs:1150).
Add two new pieces of state and call them at the same transition points:

```csharp
private static InventoryMenu.highlightThisItem _savedHighlightMethod;
private static bool _greyoutInstalled;

private static bool DonationGreyoutFilter(Item item)
{
    if (item == null) return false;

    var menu = Game1.activeClickableMenu as JunimoNoteMenu;
    if (menu == null) return true; // menu gone; allow everything to avoid stale-grey

    var bundle = _currentPageBundleField?.GetValue(menu) as Bundle;
    if (bundle == null) return true;
    if (!bundle.depositsAllowed) return false; // completed/locked bundle, grey all

    return bundle.canAcceptThisItem(item, null, ignore_stack_count: true);
}

private static void InstallDonationGreyout(JunimoNoteMenu menu)
{
    if (_greyoutInstalled || _currentPageBundleField == null) return;
    if (menu?.inventory == null) return;
    _savedHighlightMethod = menu.inventory.highlightMethod;
    menu.inventory.highlightMethod = DonationGreyoutFilter;
    _greyoutInstalled = true;
}

private static void RestoreDonationGreyout(JunimoNoteMenu menu)
{
    if (!_greyoutInstalled) return;
    if (menu?.inventory != null)
        menu.inventory.highlightMethod = _savedHighlightMethod;
    _savedHighlightMethod = null;
    _greyoutInstalled = false;
}
```

Wire calls into the existing donation-page enter/exit transitions
already present in the file (the same spots that flip `_onDonationPage`
true/false). On menu close — patched via `cleanupBeforeExit` postfix or
similar, follow whatever the file already does for donation-page
teardown — call `RestoreDonationGreyout` to be safe even if the
inventory or menu reference is stale.

### Bundle reference correctness

`currentPageBundle` is set by `setUpBundleSpecificPage` (the only
caller per a quick decompile grep). The donation page can't be open
without `currentPageBundle != null` — but the filter still null-checks
to fail open (return true → no grey) rather than fail closed (return
false → everything grey).

### Order of installation vs. existing patches

`JunimoNoteMenuPatches.cs` already manipulates `inventory.highlightMethod`?
**Check before writing the code.** A grep of the file for
`highlightMethod` shows zero matches today, so no collision. If a
future patch installs another override, this design's save/restore
chain will lose the *previous* override (only the original is saved).
Out of scope; add only if it ever becomes a problem.

## Files

| File | Change |
|---|---|
| `Patches/JunimoNoteMenuPatches.cs` | Add `_currentPageBundleField`, `_savedHighlightMethod`, `_greyoutInstalled`, `DonationGreyoutFilter`, `InstallDonationGreyout`, `RestoreDonationGreyout`. Wire install/restore into the existing donation-page enter/exit transition points. |
| `manifest.json` | Bump `Version` to `3.7.20`. |

Single commit:
`v3.7.20: #46 JunimoNoteMenu — grey out non-donatable items on bundle donation page`.

## Test plan

User runs on Ayaneo Pocket Air Mini:

1. Build, deploy.
2. Travel to Community Center, open any incomplete bundle's donation
   page.
3. **Expected:** items in inventory that don't match any uncompleted
   ingredient are greyed; items that do match are at full brightness.
   Partial stacks of matching items are at full brightness (identity-only
   rule).
4. Deposit a matching item to complete an ingredient. The remaining
   slots' matches should still highlight; items that *only* matched the
   just-completed ingredient become greyed.
5. Open a fully-completed bundle's donation page (if accessible). All
   items should be greyed (`depositsAllowed = false`).
6. Close the menu, reopen it, navigate to a *different* bundle. The
   greyout should reflect the new bundle, not stale state from the
   previous one.
7. Open a non-bundle JunimoNote screen (overview / reward summary). No
   greyout should apply — vanilla highlight restored.

## Non-goals

- Vault (cash) bundles. Those use a `purchaseButton`, not ingredient
  slots (see DONE.md #45). The greyout filter still runs but
  `canAcceptThisItem` returns false for everything because there are
  no ingredients to match — which is the correct outcome (the
  inventory is greyed because nothing in it can be deposited to a cash
  bundle; the donation happens via the purchase button).
- No change to A-press / deposit logic. Greyout is visual + vanilla's
  own `receiveLeftClick` deposit path respects `highlightMethod`, so
  blocking deposits of greyed items comes for free.
- No GMCM toggle.

## Lessons (to fold into DONE.md after verification)

- `Bundle.canAcceptThisItem(item, slot, ignore_stack_count)` is the
  canonical "does this item match any uncompleted ingredient" check.
  Pass `slot=null` and `ignore_stack_count=true` for identity-only
  filtering.
- `Bundle.depositsAllowed` is False on completed/locked bundles — grey
  everything in that case.
- The #44 (`ShopMenuPatches.cs`) save-and-restore-`highlightMethod`
  pattern transfers cleanly to bundle donation. Same hook (`InventoryMenu.highlightMethod`),
  same lifecycle (install on page enter, restore on exit).
