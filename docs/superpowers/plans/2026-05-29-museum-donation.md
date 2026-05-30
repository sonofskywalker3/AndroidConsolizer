# Museum Donation Controller Support (#18) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a controller select and place items in the Android museum donation menu, with no touch required.

**Architecture:** The full console snap chain already exists in the Android `MuseumMenu` but is gated behind `Game1.options.SnappyMenus` (false on Android). We force that flag `true` for the lifetime of the *donation* menu only (prefix on `LibraryMuseum.OpenDonationMenu`) and restore it when the menu closes (SMAPI `MenuChanged` event, `OldMenu is MuseumMenu`). Stardew's own code then does all navigation and placement. Rearrange mode is untouched. Gated by a GMCM toggle.

**Tech Stack:** C# SMAPI mod, HarmonyLib, Generic Mod Config Menu. No unit-test harness — verification is `dotnet build` (Release) + on-device test on G Cloud, per project convention. One feature = one `0.0.1` commit (`.claude/CLAUDE.md`).

**Spec:** `docs/superpowers/specs/2026-05-29-museum-donation-design.md`

**Deviation from spec (intentional):** restore happens via the SMAPI `MenuChanged` event rather than a `cleanupBeforeExit`/`OnDonationMenuClosed` postfix — strictly more reliable for a global flag and keeps the Harmony surface to a single patched method. `cleanupBeforeExit` postfix is the documented fallback (Task 7) if device testing shows the flag failing to restore.

---

## File Structure

| File | Responsibility | Change |
|------|----------------|--------|
| `Patches/MuseumMenuPatches.cs` | Force `SnappyMenus` on for the donation menu; expose a restore entry point. Owns the saved-state fields. | **Create** |
| `ModConfig.cs` | Add `EnableMuseumDonationController` toggle (default true). | Modify |
| `ModEntry.cs` | Register the patch in `Apply` list; call restore from `OnMenuChanged`; add the GMCM `AddBoolOption`. | Modify |
| `manifest.json` | Version bump. | Modify |

All feature code lands in **one commit** at **v3.7.58** (Task 5). The transient diagnostic logging it carries is stripped in a **separate** commit at **v3.7.59** (Task 7) after device verification.

---

### Task 1: Add the config toggle

**Files:**
- Modify: `ModConfig.cs` (add property near the other Standalone Feature toggles, after `EnableHoldToCraft` ~line 115)

- [ ] **Step 1: Add the property**

In `ModConfig.cs`, after the `EnableHoldToCraft` property (currently ends ~line 115), add:

```csharp
        /// <summary>
        /// #18: Controller support for the museum donation menu. On Android the
        /// game's own console snap navigation (inventory selection, D-pad museum-grid
        /// movement, A to place) is gated behind SnappyMenus, which is false on
        /// Android — so a controller can't select or place donations. When true, we
        /// turn SnappyMenus on only while the donation menu is open and let the
        /// game's own code drive it. Disable to restore vanilla touch-only behaviour.
        /// </summary>
        public bool EnableMuseumDonationController { get; set; } = true;
```

- [ ] **Step 2: Verify it compiles later** (no standalone build yet — combined into Task 5 build). Move on.

---

### Task 2: Create the patch file

**Files:**
- Create: `Patches/MuseumMenuPatches.cs`

- [ ] **Step 1: Write the full patch file**

Create `Patches/MuseumMenuPatches.cs` with exactly this content:

```csharp
using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// #18 Museum donation controller support.
    ///
    /// Root cause (decompile-verified): the full console snap chain in the Android
    /// MuseumMenu — ctor cursor snap, receiveKeyPress grid navigation via
    /// LibraryMuseum.findMuseumPieceLocationInDirection, releaseLeftClick ->
    /// inventory.selectItemAt selection, and placeItem placement — is all gated on
    /// Game1.options.SnappyMenus, which is FALSE on Android. Game1's
    /// D-pad/left-stick -> receiveKeyPress dispatch (Game1.cs ~5788/5820) is ALSO
    /// SnappyMenus-gated, so with a controller the D-pad does nothing and A fires a
    /// click at a stale, un-moved cursor. Result: donation requires touch.
    ///
    /// Fix: force SnappyMenus true for the lifetime of the DONATION menu only
    /// (prefix LibraryMuseum.OpenDonationMenu, which is the donate path only —
    /// OpenRearrangeMenu is a separate method and is intentionally NOT patched), then
    /// restore the prior value when the menu closes (driven from ModEntry.OnMenuChanged
    /// when OldMenu is MuseumMenu). The game's own console code then performs all
    /// navigation and placement; this patch writes zero navigation logic.
    ///
    /// We patch a single, plain managed method (OpenDonationMenu) and restore via the
    /// SMAPI MenuChanged event — deliberately NOT patching any input override
    /// (receiveKeyPress / releaseLeftClick) to stay clear of the Android mono-runtime
    /// SIGSEGV landmine documented for GeodeMenu.releaseLeftClick.
    /// </summary>
    internal static class MuseumMenuPatches
    {
        private static IMonitor Monitor;

        // True while we have force-enabled SnappyMenus for an open donation menu.
        // Guards the restore so it runs exactly once and only when we changed it.
        private static bool _weForcedSnappy;

        // The SnappyMenus value as it was before we forced it true.
        private static bool _savedSnappyMenus;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(LibraryMuseum), nameof(LibraryMuseum.OpenDonationMenu)),
                    prefix: new HarmonyMethod(typeof(MuseumMenuPatches), nameof(OpenDonationMenu_Prefix))
                );
                monitor.Log("[MuseumMenu] patch applied (OpenDonationMenu prefix; restore via MenuChanged).", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                monitor.Log($"[MuseumMenu] Failed to apply patch: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Force SnappyMenus true before the donation MuseumMenu is constructed, so the
        /// ctor's `if (Game1.options.SnappyMenus)` snap-setup runs and Game1's
        /// D-pad -> receiveKeyPress dispatch becomes active for the controller.
        /// </summary>
        private static void OpenDonationMenu_Prefix()
        {
            if (ModEntry.Config?.EnableMuseumDonationController != true) return;
            if (_weForcedSnappy) return; // already forced; restore pending on close

            _savedSnappyMenus = Game1.options.SnappyMenus;
            Game1.options.SnappyMenus = true;
            _weForcedSnappy = true;

            // TRANSIENT DIAGNOSTIC (remove in the follow-up strip-logging patch once
            // root cause is device-confirmed): proves the flag was false and we flipped it.
            try { Monitor.Log($"[MuseumMenu] OpenDonationMenu: forced SnappyMenus true (was {_savedSnappyMenus}).", LogLevel.Info); } catch { }
        }

        /// <summary>
        /// Restore the pre-flip SnappyMenus value. Called from ModEntry.OnMenuChanged
        /// whenever a MuseumMenu closes. Idempotent and a no-op unless we forced the flag
        /// (so it harmlessly runs for rearrange-mode MuseumMenu closes too).
        /// </summary>
        public static void RestoreSnappyOnMenuClose()
        {
            if (!_weForcedSnappy) return;
            Game1.options.SnappyMenus = _savedSnappyMenus;
            _weForcedSnappy = false;

            // TRANSIENT DIAGNOSTIC (remove in the follow-up strip-logging patch):
            try { Monitor.Log($"[MuseumMenu] menu closed: restored SnappyMenus to {_savedSnappyMenus}.", LogLevel.Info); } catch { }
        }
    }
}
```

---

### Task 3: Register the patch and the restore hook in ModEntry

**Files:**
- Modify: `ModEntry.cs` (Apply list ~line 176; `OnMenuChanged` ~line 228)

- [ ] **Step 1: Register the patch in the Apply list**

In `ModEntry.cs`, in the Harmony apply block, after the line:

```csharp
            Patches.CraftingPagePatches.Apply(harmony, this.Monitor);
```

add:

```csharp
            Patches.MuseumMenuPatches.Apply(harmony, this.Monitor);
```

- [ ] **Step 2: Add the restore hook to OnMenuChanged**

In `ModEntry.cs`, inside `OnMenuChanged` (starts ~line 228), at the **top** of the method body (before the existing `if (e.OldMenu is GameMenu ...)` block), add:

```csharp
            // #18: When the museum donation menu closes, restore the SnappyMenus value
            // we forced on while it was open. Guarded inside the patch, so this is a
            // no-op for rearrange-mode MuseumMenu closes (we never forced the flag there).
            if (e.OldMenu is StardewValley.Menus.MuseumMenu)
            {
                Patches.MuseumMenuPatches.RestoreSnappyOnMenuClose();
            }
```

> Note: `ModEntry.cs` already has `using StardewValley.Menus;` (line 9), so `MuseumMenu` is in scope; the fully-qualified name above is belt-and-suspenders and compiles either way.

---

### Task 4: Add the GMCM toggle

**Files:**
- Modify: `ModEntry.cs` (`RegisterConfigMenu`, after the "Hold A to Craft" option ~line 1243)

- [ ] **Step 1: Add the AddBoolOption block**

In `ModEntry.cs`, immediately after the `AddBoolOption` block for `EnableHoldToCraft` (the one with `name: () => "Hold A to Craft"`, ends ~line 1243), add:

```csharp
            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Museum Donation (Controller)",
                tooltip: () => "Use the controller to donate at the museum: snap-select an item, move across the museum grid with the D-pad, and place with A. Disable to restore vanilla touch-only donation.",
                getValue: () => Config.EnableMuseumDonationController,
                setValue: value => Config.EnableMuseumDonationController = value
            );
```

---

### Task 5: Build, version-bump, and commit the feature (v3.7.58)

**Files:**
- Modify: `manifest.json` (line 4: `"Version": "3.7.57"` → `"3.7.58"`)

- [ ] **Step 1: Bump the version**

In `manifest.json`, change line 4 from:

```json
    "Version": "3.7.57",
```

to:

```json
    "Version": "3.7.58",
```

- [ ] **Step 2: Build (Release)**

Run (PowerShell):

```powershell
dotnet build "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer\AndroidConsolizer.csproj" -c Release
```

Expected: `Build succeeded`, 0 errors. Output ZIP: `bin/Release/net6.0/AndroidConsolizer 3.7.58.zip`.

- [ ] **Step 3: Commit the feature (one commit, all feature files)**

```powershell
git -C "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer" add ModConfig.cs ModEntry.cs Patches/MuseumMenuPatches.cs manifest.json
git -C "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer" commit -m "v3.7.58: #18 museum donation controller support (force SnappyMenus while donating)"
```

Commit message body should note: root cause (console snap chain gated on SnappyMenus, false on Android), the fix (force true via OpenDonationMenu prefix, restore via MenuChanged), the GMCM toggle, and that it carries transient diagnostic logging to be stripped after device verification.

---

### Task 6: Device verification (G Cloud) — user-tested

This is a **meaningful playtest** (navigation feel + placement correctness), appropriate to ask the user to run.

- [ ] **Step 1: Deploy to G Cloud**

```powershell
$env:ANDROID_SERIAL = "192.168.228.87:5555"
pwsh -NoProfile -File "C:\Users\Jeff\Documents\Projects\Stardee Valoo\SyncdewValley\sync.ps1" deploy
```

(If the device dropped: `& "C:/Program Files/platform-tools/adb.exe" connect 192.168.228.87:5555` first. Use PowerShell, not Bash, for any `/storage/...` path.)

- [ ] **Step 2: User test** — in the Cheatside save, spawn a donatable artifact/mineral (CJB Item Spawner, key `I`), go to the museum (Pelican Town, ≥9am), open Gunther → Donate, and with the **controller** try to: snap-select the item from the bag, D-pad across the museum tiles, and press A to place. Report what works / what doesn't.

- [ ] **Step 3: Pull logs and confirm the diagnostic lines**

```powershell
pwsh -NoProfile -File "C:\Users\Jeff\Documents\Projects\Stardee Valoo\SyncdewValley\sync.ps1" logs
```

Then read `test-output/SMAPI-latest.txt`. Expected:
- `[MuseumMenu] OpenDonationMenu: forced SnappyMenus true (was False).` — confirms root cause on G Cloud.
- `[MuseumMenu] menu closed: restored SnappyMenus to False.` — confirms restore fired.

- [ ] **Step 4: Branch on result**
  - **Works** → proceed to Task 7 (strip logging).
  - **Flag not restored / side effect** → add the `cleanupBeforeExit` postfix fallback (see spec "Risk & fallback"): patch `AccessTools.Method(typeof(MuseumMenu), "cleanupBeforeExit")` with a postfix that calls `RestoreSnappyOnMenuClose()`. Re-test. Still log-only restore; idempotent.
  - **Game dies on open with empty SMAPI log** → suspect native SIGSEGV; pull `& "C:/Program Files/platform-tools/adb.exe" -s 192.168.228.87:5555 logcat -b crash -d` and reassess (this design avoids input-override patches specifically to prevent this).
  - **Navigation still dead despite flag flip** → add a transient log-only postfix on `MuseumMenu.receiveKeyPress` to confirm whether the D-pad reaches the menu, and reassess the Game1 dispatch assumptions.

---

### Task 7: Strip transient logging and commit (v3.7.59)

**Files:**
- Modify: `Patches/MuseumMenuPatches.cs` (remove the two `TRANSIENT DIAGNOSTIC` log lines)
- Modify: `manifest.json` (`3.7.58` → `3.7.59`)

- [ ] **Step 1: Remove the diagnostic log in `OpenDonationMenu_Prefix`**

Delete these two lines (the comment + the log) from `OpenDonationMenu_Prefix`:

```csharp
            // TRANSIENT DIAGNOSTIC (remove in the follow-up strip-logging patch once
            // root cause is device-confirmed): proves the flag was false and we flipped it.
            try { Monitor.Log($"[MuseumMenu] OpenDonationMenu: forced SnappyMenus true (was {_savedSnappyMenus}).", LogLevel.Info); } catch { }
```

- [ ] **Step 2: Remove the diagnostic log in `RestoreSnappyOnMenuClose`**

Delete these two lines from `RestoreSnappyOnMenuClose`:

```csharp
            // TRANSIENT DIAGNOSTIC (remove in the follow-up strip-logging patch):
            try { Monitor.Log($"[MuseumMenu] menu closed: restored SnappyMenus to {_savedSnappyMenus}.", LogLevel.Info); } catch { }
```

- [ ] **Step 3: Bump version**

`manifest.json` line 4: `"3.7.58"` → `"3.7.59"`.

- [ ] **Step 4: Build**

```powershell
dotnet build "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer\AndroidConsolizer.csproj" -c Release
```

Expected: `Build succeeded`, 0 errors.

- [ ] **Step 5: Commit**

```powershell
git -C "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer" add Patches/MuseumMenuPatches.cs manifest.json
git -C "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer" commit -m "v3.7.59: #18 remove transient museum-donation diagnostic logging (device-confirmed)"
```

---

### Task 8: Documentation — close out #18

**Files:**
- Modify: `DONE.md` (add a #18 entry)
- Modify: `TODO.md` (remove/mark the #18 section)
- Modify: `STATUS.md` (move #18 to done in the v3.8.0 milestone summary)
- Create: a user-memory note recording the SnappyMenus-gating root cause

- [ ] **Step 1: Add the `DONE.md` entry** — include: root cause (console snap chain gated on `Game1.options.SnappyMenus`, false on Android; Game1 D-pad→receiveKeyPress dispatch also gated), the fix (force true via `OpenDonationMenu` prefix, restore via `MenuChanged`), files touched, versions (v3.7.58 feature, v3.7.59 strip), device-verified note, and the rejected approaches (un-gate this menu / custom cursor) as documented fallbacks.

- [ ] **Step 2: Mark `#18` done in `TODO.md`** — follow the `#71` precedent (leave a ✅ DONE line pointing at `DONE.md`, or remove the section per the file's convention).

- [ ] **Step 3: Update `STATUS.md`** — in the v3.8.0 milestone summary, move #18 from "Remaining" to done with the version numbers.

- [ ] **Step 4: Write the memory note** — create `museum-donation-snappymenus-gating.md` in the auto-memory dir, type `feedback`/`reference`: "Android MuseumMenu has the full console snap/donation logic but it's gated on `Game1.options.SnappyMenus` (false on Android); Game1's D-pad→receiveKeyPress dispatch is gated too. Forcing SnappyMenus true for the donation menu's lifetime activates it. Predicts museum donation already works on Ayaneo (snappyMenus=True there)." Add a one-line pointer to `MEMORY.md`. Link `[[ayaneo-runtime-quirks]]` and `[[geodemenu-releaseleftclick-harmony-crash]]`.

- [ ] **Step 5: Commit docs**

```powershell
git -C "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer" add DONE.md TODO.md STATUS.md
git -C "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer" commit -m "docs: close out #18 museum donation controller support"
```

---

## Self-Review

**Spec coverage:**
- Root-cause / approach (flip SnappyMenus, donation-scoped) → Tasks 2–3. ✅
- Donation-only scope (rearrange untouched) → patching `OpenDonationMenu` only; restore guarded by `_weForcedSnappy`. ✅
- GMCM toggle default true → Tasks 1, 4. ✅
- Restore on close → Task 3 (MenuChanged); spec's `cleanupBeforeExit` is the Task 6 fallback. ✅ (intentional, documented deviation)
- Diagnostic-first / transient logging → carried in v3.7.58 (Task 2/5), stripped v3.7.59 (Task 7). ✅
- Verification plan (G Cloud test, Ayaneo cross-check, landmine watch) → Task 6. ✅
- Risk & fallback (un-gate this menu) → Task 6 Step 4. ✅
- Success criteria → Task 6 acceptance. ✅

**Placeholder scan:** No TBD/TODO; all code shown in full. ✅

**Type consistency:** `_weForcedSnappy`, `_savedSnappyMenus`, `RestoreSnappyOnMenuClose()`, `OpenDonationMenu_Prefix`, `EnableMuseumDonationController` are referenced consistently across Tasks 1–4 and 7. Method patched: `LibraryMuseum.OpenDonationMenu` (public). Restore invoked from `ModEntry.OnMenuChanged`. ✅
