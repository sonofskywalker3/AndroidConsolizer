# Options Page Injection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore stripped-but-functional vanilla options to the Android Options page (tool-hit 11/12 + zoom 18), hide touch-only options when a controller is active, and keep the scroll extent sized to the live list so late-added entries (GMCM's button) are reachable.

**Architecture:** A new Harmony patch class `OptionsPageInjectionPatches` owns "what's in the `options` list and how far it scrolls" (ctor postfix to inject/hide, update postfix to size the scroll). It composes with the existing `OptionsPagePatches` ("how the controller navigates the list"). All Android-differing members via `AccessTools` reflection with null-checks; all patch bodies wrapped in try/catch that falls through to vanilla.

**Tech Stack:** C# / SMAPI / HarmonyLib. **No automated tests** — verification is `dotnet build` (must be 0 errors) + on-device test on the G Cloud. Build: `dotnet build AndroidConsolizer.csproj -c Release`. Deploy: `cd ../SyncdewValley; pwsh -NoProfile -File sync.ps1 deploy`. Log: `… sync.ps1 logs`. One change per commit, PATCH `0.0.1` bump in `manifest.json` first.

**Decompile facts (verified):** Android `OptionsPage` ctor `(int x, int y, int width, int height, float widthMod=1f, float heightMod=1f)`; private `List<OptionsElement> options`; private `MobileScrollbox scrollArea`; public `OptionsButton optionsButtonAdjustControls`. Scroll baked once: `maxYOffset = ContentHeight - (height-16) + 50` where `ContentHeight = 20 + Σ(ItemHeight + 20)`. `OptionsCheckbox(label, which)` / `OptionsSlider(label, which, x, y, width)` ctors auto-call `setCheckBoxToProperValue`/`setSliderToProperValue`, and `Options` handles 11/12/18 there → injected controls self-populate. Engine handles 11/12 in `changeCheckBoxOption`; zoom 18's `changeSliderOption` case is a broken stepper, so apply zoom via `changeDropDownOption(18,"<pct>%")` (absolute). `MobileScrollbox` is Android-only (absent from PC DLL) → reflect `setMaxYOffset(int)` + `maxYOffset` off the runtime object. `MobileScrollbox.setMaxYOffset` stores `0` as `1` and resets `havePanelScrolled=false`, so only call it when the value actually changes.

---

## File Structure

- **Create** `Patches/OptionsPageInjectionPatches.cs` — ctor postfix (inject 11/12/zoom, hide touch options) + update postfix (scroll-fit).
- **Modify** `ModEntry.cs` — register the new patch; remove `EnforceToolHitLocationOptions` + its call + the "Show Tool Hit Location" GMCM entry; add the "Hide Touch Options" GMCM entry.
- **Modify** `ModConfig.cs` — remove `EnableToolHitLocation`; add `HideTouchOptionsWithController`.
- **Modify** `Patches/OptionsPagePatches.cs` — route slider value-apply through a helper that special-cases zoom (18).

---

## Task 1: Scroll-fit (Feature 4) — create the patch class + register

Foundational and independently valuable (fixes the GMCM-button-below-the-floor bug on its own).

**Files:**
- Create: `Patches/OptionsPageInjectionPatches.cs`
- Modify: `ModEntry.cs:206` (register after `OptionsPagePatches.Apply`)
- Modify: `manifest.json` (version → `3.8.24`)

- [ ] **Step 1: Create `Patches/OptionsPageInjectionPatches.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Injects stripped-but-functional vanilla options back into the Android OptionsPage, hides
    /// touch-only options when a controller is active, and keeps the scroll extent sized to the live
    /// options list (so late-added entries like GMCM's button are reachable). Separate from
    /// OptionsPagePatches, which owns controller navigation of the list.
    /// </summary>
    internal static class OptionsPageInjectionPatches
    {
        private static IMonitor Monitor;

        private static FieldInfo _optionsField;
        private static FieldInfo _scrollAreaField;

        // MobileScrollbox is Android-only — resolve its members off the runtime object, once.
        private static MethodInfo _msSetMaxYOffset;
        private static FieldInfo _msMaxYOffsetField;
        private static bool _msResolved;

        // ContentHeight constants mirrored from OptionsPage.cs (Y_SPACING=20; scrollArea build:
        // maxYOffset = ContentHeight - (height-16) + 50).
        private const int YSpacing = 20;
        private const int ScrollBottomPad = 16;
        private const int ScrollExtra = 50;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            try
            {
                _optionsField = AccessTools.Field(typeof(OptionsPage), "options");
                _scrollAreaField = AccessTools.Field(typeof(OptionsPage), "scrollArea");

                var update = AccessTools.Method(typeof(OptionsPage), nameof(OptionsPage.update), new[] { typeof(GameTime) });
                if (update != null)
                    harmony.Patch(update, postfix: new HarmonyMethod(typeof(OptionsPageInjectionPatches), nameof(Update_Postfix)));

                Monitor.Log("OptionsPage injection patches applied.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply OptionsPage injection patches: {ex.Message}", LogLevel.Error);
            }
        }

        private static void ResolveScrollboxMembers(object scrollArea)
        {
            if (_msResolved || scrollArea == null) return;
            var t = scrollArea.GetType();
            _msSetMaxYOffset = AccessTools.Method(t, "setMaxYOffset", new[] { typeof(int) });
            _msMaxYOffsetField = AccessTools.Field(t, "maxYOffset");
            _msResolved = true;
        }

        /// <summary>Feature 4: keep the scroll extent sized to the live options list so late-added
        /// entries (GMCM's button) and our injected options are reachable. The ctor bakes maxYOffset
        /// once; entries added/removed after that leave it stale. Only re-applies when the computed
        /// value actually changes (setMaxYOffset resets havePanelScrolled, so don't call it every frame).</summary>
        private static void Update_Postfix(OptionsPage __instance)
        {
            try
            {
                var options = _optionsField?.GetValue(__instance) as List<OptionsElement>;
                if (options == null) return;

                var scrollArea = _scrollAreaField?.GetValue(__instance);
                if (scrollArea == null) return;
                ResolveScrollboxMembers(scrollArea);
                if (_msSetMaxYOffset == null || _msMaxYOffsetField == null) return;

                int contentHeight = YSpacing;
                for (int i = 0; i < options.Count; i++)
                    contentHeight += options[i].ItemHeight + YSpacing;

                int viewportH = __instance.height - ScrollBottomPad;
                int newMax = Math.Max(0, contentHeight - viewportH + ScrollExtra);
                int applied = (newMax == 0) ? 1 : newMax; // MobileScrollbox stores 0 as 1
                int currentMax = (int)_msMaxYOffsetField.GetValue(scrollArea);
                if (applied != currentMax)
                    _msSetMaxYOffset.Invoke(scrollArea, new object[] { newMax });
            }
            catch (Exception ex)
            {
                Monitor?.Log($"OptionsPage scroll-fit error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
```

- [ ] **Step 2: Register the patch in `ModEntry.cs`** — add immediately after line 206:

```csharp
            Patches.OptionsPagePatches.Apply(harmony, this.Monitor);
            Patches.OptionsPageInjectionPatches.Apply(harmony, this.Monitor);
```

- [ ] **Step 3: Bump `manifest.json`** version to `3.8.24`.

- [ ] **Step 4: Build** — `dotnet build AndroidConsolizer.csproj -c Release`. Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add Patches/OptionsPageInjectionPatches.cs ModEntry.cs manifest.json
git commit -m "v3.8.24: Options page dynamic scroll-fit — size scroll to live list (GMCM button reachable)"
```

- [ ] **Step 6: Deploy + device test** — `cd ../SyncdewValley; pwsh -NoProfile -File sync.ps1 deploy`. User: open the in-game Options page, scroll to the very bottom — **GMCM's "Mod Options" button should now be reachable** via stick/scroll (previously below the floor). Pull log, confirm no `scroll-fit error`.

---

## Task 2: Tool-hit checkboxes (Feature 1) + retire #76 force

**Files:**
- Modify: `Patches/OptionsPageInjectionPatches.cs` (add ctor postfix)
- Modify: `ModEntry.cs` (remove `EnforceToolHitLocationOptions` + call + GMCM entry)
- Modify: `ModConfig.cs` (remove `EnableToolHitLocation`)
- Modify: `manifest.json` (→ `3.8.25`)

- [ ] **Step 1: Add injection fields + ctor postfix to `OptionsPageInjectionPatches.cs`.** Add these constants near the other constants:

```csharp
        // Hardcoded labels — PC string keys (Options_AlwaysShowToolHitLocation etc.) are absent from
        // the Android content, so LoadString won't resolve them.
        private const string LabelAlwaysShowToolHit = "Always show tool hit location";
        private const string LabelHideToolHitMoving = "Hide tool hit location while moving";

        private const int OptAlwaysShowToolHit = 11;
        private const int OptHideToolHitMoving = 12;
```

In `Apply`, after the `update` patch block, register the ctor postfix:

```csharp
                var ctor = AccessTools.Constructor(typeof(OptionsPage),
                    new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(float), typeof(float) });
                if (ctor != null)
                    harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(OptionsPageInjectionPatches), nameof(Ctor_Postfix)));
                else
                    Monitor.Log("OptionsPageInjectionPatches: OptionsPage ctor not found — injection disabled", LogLevel.Warn);
```

Add the `Ctor_Postfix` method:

```csharp
        /// <summary>Append injected options to the OptionsPage list. They self-sync their displayed
        /// value because OptionsCheckbox/OptionsSlider ctors call set*ToProperValue, and Options
        /// handles 11/12/18 there. The ctor's updateContentPositions already ran, so positions are set
        /// on the next update tick (before first draw); the scroll-fit postfix (Task 1) resizes the
        /// scroll to include these. Appended at the end (an "extra options" block).</summary>
        private static void Ctor_Postfix(OptionsPage __instance)
        {
            try
            {
                var options = _optionsField?.GetValue(__instance) as List<OptionsElement>;
                if (options == null) return;

                options.Add(new OptionsCheckbox(LabelAlwaysShowToolHit, OptAlwaysShowToolHit));
                options.Add(new OptionsCheckbox(LabelHideToolHitMoving, OptHideToolHitMoving));
            }
            catch (Exception ex)
            {
                Monitor?.Log($"OptionsPage Ctor_Postfix error: {ex.Message}", LogLevel.Error);
            }
        }
```

- [ ] **Step 2: Remove the #76 force from `ModEntry.cs`.** Delete the `EnforceToolHitLocationOptions()` method (the block starting `/// <summary>#76: keep the console "always show tool hit location"...` through the method's closing brace) AND its call site in `OnUpdateTicked` (the comment block + `EnforceToolHitLocationOptions();` line ~382-384).

- [ ] **Step 3: Remove the GMCM "Show Tool Hit Location" entry from `ModEntry.cs`** — delete the `configMenu.AddBoolOption(... name: () => "Show Tool Hit Location" ...)` block (lines ~1396-1402).

- [ ] **Step 4: Remove `EnableToolHitLocation` from `ModConfig.cs`** — delete the `/// <summary> #76 ...` doc block and `public bool EnableToolHitLocation { get; set; } = true;` (lines ~158-165).

- [ ] **Step 5: Bump `manifest.json`** to `3.8.25`.

- [ ] **Step 6: Build** — expected `0 Error(s)` (confirm no lingering references to `EnableToolHitLocation` / `EnforceToolHitLocationOptions`).

- [ ] **Step 7: Commit**

```bash
git add Patches/OptionsPageInjectionPatches.cs ModEntry.cs ModConfig.cs manifest.json
git commit -m "v3.8.25: #76 retire force, expose native tool-hit checkboxes (11,12) in Options page"
```

- [ ] **Step 8: Deploy + device test** — Options page now shows "Always show tool hit location" + "Hide tool hit location while moving". Toggling "always show" on makes the red target box appear; it persists across sessions (StartupPreferences). Confirm the box is OFF by default now (vanilla default) and turns on when checked.

---

## Task 3: Hide touch options (Feature 3) + GMCM toggle

**Files:**
- Modify: `ModConfig.cs` (add `HideTouchOptionsWithController`)
- Modify: `Patches/OptionsPageInjectionPatches.cs` (hide logic + field)
- Modify: `ModEntry.cs` (GMCM entry)
- Modify: `manifest.json` (→ `3.8.26`)

- [ ] **Step 1: Add config flag to `ModConfig.cs`** (near the other Options-page flags):

```csharp
        /// <summary>Hide the touch-only options (virtual-joystick Controls dropdown, on-screen
        /// controls toggle, invisible-button width, pinch-zoom, and the Adjust-joypad-controls button)
        /// from the in-game Options page when a controller is active. They're meaningless with a
        /// physical controller. Default on.</summary>
        public bool HideTouchOptionsWithController { get; set; } = true;
```

- [ ] **Step 2: Add the adjust-controls field + hide constants to `OptionsPageInjectionPatches.cs`:**

```csharp
        private static FieldInfo _adjustControlsField;

        // Touch-only options to hide when a controller is active (decided with user):
        // 139 Controls dropdown, 140 show-on-screen-controls toggle, 146 invisible-button width,
        // 147 pinch-zoom. The Adjust-joypad-controls OptionsButton is matched by reference.
        private static readonly int[] TouchOptionIds = { 139, 140, 146, 147 };
```

In `Apply`, after caching `_scrollAreaField`:

```csharp
                _adjustControlsField = AccessTools.Field(typeof(OptionsPage), "optionsButtonAdjustControls");
```

- [ ] **Step 3: Prepend the hide block to `Ctor_Postfix`** (before the `options.Add(...)` injection calls):

```csharp
                // Feature 3: hide touch-only options when a controller is active. gamepadControls
                // (not GamePad.IsConnected) matches the rest of OptionsPagePatches and sidesteps the
                // Ayaneo IsConnected=false quirk. Removing entries needs the scroll-fit (Task 1) to
                // re-size the list — already in place.
                if ((ModEntry.Config?.HideTouchOptionsWithController ?? false) && Game1.options.gamepadControls)
                {
                    var adjustBtn = _adjustControlsField?.GetValue(__instance) as OptionsElement;
                    options.RemoveAll(o =>
                        Array.IndexOf(TouchOptionIds, o.whichOption) >= 0
                        || (adjustBtn != null && ReferenceEquals(o, adjustBtn)));
                }
```

- [ ] **Step 4: Add the GMCM toggle to `ModEntry.cs`** (near the other Options-page toggles, e.g. after "Free Cursor on Settings"):

```csharp
            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Hide Touch Options (Controller)",
                tooltip: () => "Hide the touch-only options (virtual-joystick Controls, on-screen controls toggle, invisible-button width, pinch-zoom, Adjust Joypad Controls) from the in-game Options page when a controller is connected. They don't apply to a physical controller.",
                getValue: () => Config.HideTouchOptionsWithController,
                setValue: value => Config.HideTouchOptionsWithController = value
            );
```

- [ ] **Step 5: Bump `manifest.json`** to `3.8.26`.

- [ ] **Step 6: Build** — expected `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add Patches/OptionsPageInjectionPatches.cs ModConfig.cs ModEntry.cs manifest.json
git commit -m "v3.8.26: hide touch-only options on the Options page when a controller is active (GMCM toggle, default on)"
```

- [ ] **Step 8: Deploy + device test** — with the controller connected, the Options page no longer shows the Controls dropdown, on-screen-controls toggle, invisible-button-width slider, pinch-zoom checkbox, or the Adjust-joypad-controls button. Toggling the GMCM option off brings them back. Confirm snap-nav skips the gap cleanly (the entries are gone from the list).

---

## Task 4: Zoom slider (Feature 2) + apply special-case — **device-gated**

**Files:**
- Modify: `Patches/OptionsPageInjectionPatches.cs` (inject zoom slider)
- Modify: `Patches/OptionsPagePatches.cs` (zoom apply helper)
- Modify: `manifest.json` (→ `3.8.27`)

- [ ] **Step 1: Add zoom constants/label to `OptionsPageInjectionPatches.cs`:**

```csharp
        private const string LabelZoom = "Zoom level";
        private const int OptZoom = 18;
```

- [ ] **Step 2: Append the zoom slider in `Ctor_Postfix`** (after the two tool-hit `options.Add` calls):

```csharp
                // Feature 2 (device-gated): zoom slider. Self-syncs to desiredBaseZoomLevel*100 (50-100)
                // via setSliderToProperValue case 18. Bound the slider to 50-100 so OptionsPagePatches'
                // Left/Right nav steps within range; the value is APPLIED via changeDropDownOption (see
                // OptionsPagePatches.ApplySliderValue) because changeSliderOption(18) is a broken stepper.
                var zoom = new OptionsSlider(LabelZoom, OptZoom, -1, -1, __instance.width);
                AccessTools.Field(typeof(OptionsSlider), "sliderMinValue")?.SetValue(zoom, 50);
                AccessTools.Field(typeof(OptionsSlider), "sliderMaxValue")?.SetValue(zoom, 100);
                options.Add(zoom);
```

- [ ] **Step 3: Add the apply helper to `OptionsPagePatches.cs`** (anywhere in the class, e.g. near `HandleStickNav`):

```csharp
        /// <summary>Apply a slider's new value. Zoom (whichOption 18) is special-cased: its
        /// changeSliderOption case is a degenerate ±10 stepper that expects a 0-1 fraction, so we set
        /// it absolutely via changeDropDownOption("&lt;pct&gt;%"), which writes desiredBaseZoomLevel
        /// directly (decompile Options.cs:1506-1518). All other sliders use changeSliderOption.</summary>
        private static void ApplySliderValue(OptionsSlider slider, int newVal)
        {
            if (slider.whichOption == 18)
                Game1.options.changeDropDownOption(18, newVal + "%");
            else
                Game1.options.changeSliderOption(slider.whichOption, newVal);
        }
```

- [ ] **Step 4: Route both slider-apply sites through the helper in `OptionsPagePatches.cs`.** In `ReceiveGamePadButton_Prefix` (the `opt is OptionsSlider slider` branch) and in `HandleStickNav` (the `opt is OptionsSlider slider` branch), replace:

```csharp
                    Game1.options.changeSliderOption(slider.whichOption, newVal);
```

with:

```csharp
                    ApplySliderValue(slider, newVal);
```

(Both occurrences — there are two.)

- [ ] **Step 5: Bump `manifest.json`** to `3.8.27`.

- [ ] **Step 6: Build** — expected `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add Patches/OptionsPageInjectionPatches.cs Patches/OptionsPagePatches.cs manifest.json
git commit -m "v3.8.27: inject zoom slider (18) on Options page; apply zoom absolutely via changeDropDownOption"
```

- [ ] **Step 8: Deploy + DEVICE-GATE TEST (decides 3.9.0 vs 4.0)** — Options page shows a "Zoom level" slider at the current zoom. User moves it Left/Right with the controller and reports: **does the game world actually zoom?**
  - **If yes** → zoom ships in 3.9.0. Mark TODO #12's zoom sub-item done.
  - **If no (pinch-zoom / NativeZoomLevel overrides it)** → revert this commit (or gate the zoom injection behind a disabled flag) and move zoom to the 4.0 "Right Stick + Zoom" item, where it gets the custom wiring (also set `PinchZoom.Instance.ZoomLevel` / force `pinchZoom=false`). Tool-hit (Task 2) and the rest are unaffected.

---

## Post-implementation

- [ ] Move #76 / the tool-hit + zoom items from `TODO.md` to `DONE.md` per device results; note zoom's 3.9.0-vs-4.0 outcome.
- [ ] Update `STATUS.md` snapshot.
- [ ] 3.9.0 bundle now also includes the Options-page injection + scroll-fit + touch-option hiding — fold into the eventual 3.9.0 release notes.

## Self-review notes
- **Spec coverage:** Feature 1 → Task 2; Feature 2 (zoom) → Task 4; Feature 3 (hide) → Task 3; Feature 4 (scroll-fit) → Task 1. #76 force retirement → Task 2. All covered.
- **Ordering:** scroll-fit (Task 1) leads because both injection and hiding change the list length and depend on a correct scroll extent.
- **No placeholders:** every code step is concrete.
- **Type consistency:** `Ctor_Postfix` / `Update_Postfix` / `ApplySliderValue` / `ResolveScrollboxMembers` referenced consistently; `_optionsField`/`_scrollAreaField`/`_adjustControlsField`/`_msSetMaxYOffset`/`_msMaxYOffsetField` defined in Task 1/3 before use.
