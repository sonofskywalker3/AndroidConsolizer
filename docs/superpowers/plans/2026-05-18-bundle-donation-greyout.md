# Bundle Donation Greyout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Grey out inventory items that can't be donated to the active bundle on `JunimoNoteMenu`'s donation page, matching console SDV behaviour.

**Architecture:** Save/swap/restore `InventoryMenu.highlightMethod` at the donation-page enter/exit transitions already tracked by `_onDonationPage` in `JunimoNoteMenuPatches.cs`. Filter delegates to `Bundle.canAcceptThisItem(item, slot: null, ignore_stack_count: true)` so partial stacks of matching items stay highlighted.

**Tech Stack:** C# / Stardew Modding API (SMAPI) / HarmonyLib reflection. No unit-test framework available — verification is device-based.

**Spec:** [`docs/superpowers/specs/2026-05-18-bundle-donation-greyout-design.md`](../specs/2026-05-18-bundle-donation-greyout-design.md)

---

## File Structure

Single file modified: `Patches/JunimoNoteMenuPatches.cs`. No new patch file, no new `Apply()` registration. `manifest.json` version bump only.

| File | What it owns |
|---|---|
| `Patches/JunimoNoteMenuPatches.cs` | All JunimoNoteMenu input + cursor + (new) greyout logic. Greyout adds ~50 lines: one reflection cache line, two static state fields, one filter method, two helpers, two call-site wires, one reset line. |
| `manifest.json` | Version bump 3.7.19 → 3.7.20. |

---

## Task 1: Add reflection cache for `currentPageBundle`

**Files:**
- Modify: `Patches/JunimoNoteMenuPatches.cs:79` (insert after existing `_whichAreaField` cache)

`Bundle currentPageBundle` is `private` on `JunimoNoteMenu` (decompile line 100). The existing `Apply()` already caches sibling private fields via `AccessTools.Field`. Add one more.

- [ ] **Step 1: Add the cached `FieldInfo` declaration**

Find the existing reflection field declarations around line 26-35 and add this with them:

```csharp
private static FieldInfo _currentPageBundleField;
```

- [ ] **Step 2: Populate the cache in `Apply()`**

In `Apply()` after the line `_whichAreaField = AccessTools.Field(typeof(JunimoNoteMenu), "whichArea");` (around line 79), add:

```csharp
_currentPageBundleField = AccessTools.Field(typeof(JunimoNoteMenu), "currentPageBundle");
if (_currentPageBundleField == null)
{
    Monitor.Log("[JunimoNote] currentPageBundle reflection failed — donation greyout disabled.", LogLevel.Warn);
}
```

The `null` check lets the rest of the file's patches keep working if reflection ever breaks; the greyout becomes a no-op (filter returns `true` → vanilla behaviour).

---

## Task 2: Add state fields and the filter method

**Files:**
- Modify: `Patches/JunimoNoteMenuPatches.cs` (add fields near other static state, add method near other helpers)

- [ ] **Step 1: Add the two new static fields**

Near the other state fields (around line 41 where `private static bool _onDonationPage;` lives), add:

```csharp
private static InventoryMenu.highlightThisItem _savedHighlightMethod;
private static bool _greyoutInstalled;
```

- [ ] **Step 2: Add the filter method**

Add this method somewhere readable — recommended just before the `// ===== Draw prefix/postfix =====` region marker (around line 686). The filter must be static (assignable as `InventoryMenu.highlightThisItem` delegate):

```csharp
private static bool DonationGreyoutFilter(Item item)
{
    if (item == null) return false;

    var menu = Game1.activeClickableMenu as JunimoNoteMenu;
    if (menu == null) return true;

    var bundle = _currentPageBundleField?.GetValue(menu) as Bundle;
    if (bundle == null) return true;
    if (!bundle.depositsAllowed) return false;

    return bundle.canAcceptThisItem(item, null, ignore_stack_count: true);
}
```

Fail-open (`return true` → not greyed) on null menu / null bundle so a stale install can't leave the inventory falsely greyed.

`Bundle` is in `StardewValley` namespace — the file already has `using StardewValley;` so no new `using` needed.

---

## Task 3: Add Install / Restore helpers

**Files:**
- Modify: `Patches/JunimoNoteMenuPatches.cs` (next to the filter method)

- [ ] **Step 1: Add `InstallDonationGreyout`**

Directly below `DonationGreyoutFilter`:

```csharp
private static void InstallDonationGreyout(JunimoNoteMenu menu)
{
    if (_greyoutInstalled) return;
    if (_currentPageBundleField == null) return;
    if (menu?.inventory == null) return;

    _savedHighlightMethod = menu.inventory.highlightMethod;
    menu.inventory.highlightMethod = DonationGreyoutFilter;
    _greyoutInstalled = true;
}
```

- [ ] **Step 2: Add `RestoreDonationGreyout`**

Directly below `InstallDonationGreyout`:

```csharp
private static void RestoreDonationGreyout(JunimoNoteMenu menu)
{
    if (!_greyoutInstalled) return;

    if (menu?.inventory != null)
        menu.inventory.highlightMethod = _savedHighlightMethod;

    _savedHighlightMethod = null;
    _greyoutInstalled = false;
}
```

Restore is safe to call when not installed (early return) and when the menu reference is stale (still clears the flag so next install runs fresh).

---

## Task 4: Wire install / restore into the existing transition points

**Files:**
- Modify: `Patches/JunimoNoteMenuPatches.cs:649` (enter)
- Modify: `Patches/JunimoNoteMenuPatches.cs:651-657` (exit)

The `Update_Postfix` already detects the donation-page enter and exit transitions. Add one call to each.

- [ ] **Step 1: Wire install at the entry transition**

In `Update_Postfix`, find the block that enters the donation page (around line 634-650). The last line of that block is `SnapToSlot(__instance);`. Add the install call immediately after:

```csharp
                if (specificBundle && !_onDonationPage)
                {
                    _savedOverviewComponentId = __instance.currentlySnappedComponent?.myID ?? -1;
                    _onDonationPage = true;
                    _onOverviewPage = false;
                    _trackedSlotIndex = 0;
                    _inIngredientZone = false;
                    _enteredPageTick = Game1.ticks;

                    int invCount = Game1.player.Items.Count;
                    int slotCount = __instance.inventory?.inventory?.Count ?? 36;
                    _maxSlotIndex = Math.Min(invCount, slotCount) - 1;
                    if (_maxSlotIndex < 0) _maxSlotIndex = 0;

                    BuildIngredientRows(__instance);
                    SnapToSlot(__instance);
                    InstallDonationGreyout(__instance);  // <-- ADD THIS LINE
                }
```

- [ ] **Step 2: Wire restore at the exit transition**

In the immediately-following `else if` block (around line 651-657), add the restore call as the *first* line inside the block so the highlightMethod is reverted before any other state cleanup runs:

```csharp
                else if (!specificBundle && _onDonationPage)
                {
                    RestoreDonationGreyout(__instance);  // <-- ADD THIS LINE
                    _onDonationPage = false;
                    _overridingMouse = false;
                    _inIngredientZone = false;
                    _ingredientRows = null;
                }
```

---

## Task 5: Reset state in `OnMenuChanged`

**Files:**
- Modify: `Patches/JunimoNoteMenuPatches.cs:151-161`

`OnMenuChanged` is called externally (from `ModEntry`) when `Game1.activeClickableMenu` changes — including when the JunimoNoteMenu is replaced/closed. The instance we installed onto may be gone; we can't safely call `RestoreDonationGreyout` with the (likely-null-or-new) menu reference. Just clear our flags so the next instance starts clean.

- [ ] **Step 1: Add state resets to `OnMenuChanged`**

Locate `OnMenuChanged` (around line 151) and add the two new fields to the existing reset list:

```csharp
        public static void OnMenuChanged()
        {
            _onDonationPage = false;
            _onOverviewPage = false;
            _overridingMouse = false;
            _inIngredientZone = false;
            _ingredientRows = null;
            _savedOverviewComponentId = -1;
            _savedHighlightMethod = null;    // <-- ADD
            _greyoutInstalled = false;       // <-- ADD
            // Don't reset _rewardsMenuOpened/_pendingRewardIndices here —
            // they need to survive the menu transition from ItemGrabMenu back to JunimoNoteMenu
        }
```

---

## Task 6: Bump manifest version

**Files:**
- Modify: `manifest.json:4`

- [ ] **Step 1: Bump version to 3.7.20**

Change the `Version` line:

```json
    "Version": "3.7.20",
```

---

## Task 7: Build and verify clean compile

**Files:** none

- [ ] **Step 1: Build the mod**

Run:

```bash
cd "C:/Users/Jeff/Documents/Projects/Stardee Valoo/AndroidConsolizer" && dotnet build AndroidConsolizer.csproj -c Release
```

Expected output ends with:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

If there are warnings about the new code (most likely an unused-variable or null-reference warning), fix them before proceeding. The existing file builds clean today; new code must not regress that.

- [ ] **Step 2: Verify the v3.7.20 zip was produced**

Expected: a fresh `bin/Release/net6.0/AndroidConsolizer 3.7.20.zip` was generated. If the build log line `Generating the release zip at ...AndroidConsolizer 3.7.20.zip` is present, that's confirmation.

---

## Task 8: Deploy to device

**Files:** none

- [ ] **Step 1: Push to Ayaneo + relaunch**

Run:

```bash
cd "C:/Users/Jeff/Documents/Projects/Stardee Valoo/AndroidConsolizer" && pwsh -NoProfile -File "C:/Users/Jeff/Documents/Projects/Stardee Valoo/SyncdewValley/sync.ps1" deploy
```

Expected output ends with `Deploy v3.7.20 complete!` (or similar). The script handles version detection, push, relaunch, and Start-Game tap.

---

## Task 9: User device test (manual, blocks commit)

**Files:** none

- [ ] **Step 1: Ask user to test**

Tell the user the test plan from the spec:

1. Travel to Community Center, open any incomplete bundle's donation page.
2. **Expected:** items in inventory that don't match any uncompleted ingredient are greyed; items that do match are at full brightness. Partial stacks of matching items are at full brightness.
3. Deposit a matching item to complete an ingredient. Items that only matched the just-completed ingredient become greyed.
4. Open a fully-completed bundle's donation page (if accessible) — all items greyed.
5. Close and reopen the menu, navigate to a *different* bundle — greyout reflects the new bundle.
6. Non-bundle JunimoNote screens (overview, reward summary) — no greyout, vanilla highlight restored.

- [ ] **Step 2: Pull logs after user confirms**

After the user reports the test outcome, pull the device log:

```bash
cd "C:/Users/Jeff/Documents/Projects/Stardee Valoo/AndroidConsolizer" && pwsh -NoProfile -File "C:/Users/Jeff/Documents/Projects/Stardee Valoo/SyncdewValley/sync.ps1" logs
```

Check `test-output/SMAPI-latest.txt` for any `[JunimoNote]` errors. The `currentPageBundle reflection failed` warning would be the one to look for if greyout silently doesn't work — it would mean PC DLL has the field but Android runtime doesn't, requiring a different lookup.

If the test fails (greyout wrong, items incorrectly greyed/un-greyed), do **not** commit. Diagnose via decompile + log, then re-iterate.

---

## Task 10: Commit, update DONE.md / TODO.md

**Files:**
- Modify: `manifest.json`
- Modify: `Patches/JunimoNoteMenuPatches.cs`
- Modify: `TODO.md` (remove #46)
- Modify: `DONE.md` (add #46 entry)

- [ ] **Step 1: Remove #46 from TODO.md**

Find the `### 46. Grey Out Non-Donatable Items on Bundle Page` block (around line 19-23 of TODO.md after the current edit state — actually line 19 since #39 was already removed) and delete the entire section, leaving a clean break between #47 and whatever precedes it.

- [ ] **Step 2: Add #46 entry to DONE.md**

In `DONE.md`, find the "Console Parity: Quick Wins (v3.8.0)" section (search for `## Console Parity: Quick Wins (v3.8.0)`). Add this entry above `### #39 Monster Eradication Tracking Page` (most recent on top):

```markdown
### #46 Grey Out Non-Donatable Items on Bundle Donation Page — v3.7.20
- **Symptom:** On `JunimoNoteMenu`'s per-bundle donation page, inventory items are shown at full brightness regardless of whether they can be donated to that bundle. Console SDV greys out the non-matching items.
- **Fix:** Save/swap/restore `InventoryMenu.highlightMethod` on donation-page enter/exit (same hook as #44 sell-tab greyout). Filter delegates to `Bundle.canAcceptThisItem(item, slot: null, ignore_stack_count: true)` — identity-only matching, partial stacks of valid items stay highlighted. Returns `false` for everything when `bundle.depositsAllowed = false` (completed/locked bundles, greys all). Returns `true` when bundle reflection or menu reference is null (fail-open, no false-grey).
- **Scope:** Always-on, no GMCM toggle. Cash/vault bundles are also greyed (no ingredient slots → nothing matches), which is the correct outcome — donation on those bundles goes through `purchaseButton`, not inventory clicks.
- **Bundle reference:** `JunimoNoteMenu.currentPageBundle` is private — reflected via `AccessTools.Field` cached at `Apply()` time. Reflection-failure path logs a Warn and leaves the rest of `JunimoNoteMenuPatches` functioning normally; greyout becomes a no-op.
- **File:** `Patches/JunimoNoteMenuPatches.cs` (additions only — no behaviour change to existing patches).
- **Verified:** Device test on Ayaneo Pocket Air Mini — non-matching items greyed on bundle entry, partial-stack rule confirmed (3-of-required-5 stays bright), greyout cleared on exit to overview/non-bundle screens.
- **Key lessons:**
  - `Bundle.canAcceptThisItem(item, null, true)` is the canonical identity-only "can this item ever satisfy any uncompleted ingredient" check. `null` slot + `ignore_stack_count: true` are the right defaults for visual filters where current stack size shouldn't decide visibility.
  - `Bundle.depositsAllowed` is the right gate for "is this bundle currently accepting anything at all" — covers completed bundles cleanly without needing to enumerate ingredient completion state.
```

- [ ] **Step 3: Stage and commit**

```bash
cd "C:/Users/Jeff/Documents/Projects/Stardee Valoo/AndroidConsolizer" && git add manifest.json Patches/JunimoNoteMenuPatches.cs TODO.md DONE.md && git commit -m "$(cat <<'EOF'
v3.7.20: #46 JunimoNoteMenu — grey out non-donatable items on bundle donation page

Save/swap/restore InventoryMenu.highlightMethod on donation-page
enter/exit. Filter: Bundle.canAcceptThisItem(item, null,
ignore_stack_count: true) — identity-only, partial stacks of matching
items stay highlighted. depositsAllowed=false greys everything
(completed/locked bundles). Null bundle / null menu fails open.

JunimoNoteMenu.currentPageBundle is private; reflection cached at
Apply() time. If reflection fails, logs Warn and disables greyout
without breaking the rest of JunimoNoteMenuPatches.

Pattern mirrors #44 (ShopMenuPatches.cs sell-tab zero-price greyout).
Single file change, no new patch registration, no GMCM toggle.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Verify commit landed**

```bash
cd "C:/Users/Jeff/Documents/Projects/Stardee Valoo/AndroidConsolizer" && git log --oneline -1
```

Expected: most recent commit is the v3.7.20 one above.

---

## Done check

When all tasks pass:
- v3.7.20 deployed and device-verified
- Single commit on master, branch ahead of origin by N+1
- TODO.md no longer contains #46
- DONE.md "Console Parity: Quick Wins (v3.8.0)" section has the #46 entry
- No new warnings in `dotnet build`
- v3.8.0 milestone now has 3 items remaining (#47, #27, #19)
