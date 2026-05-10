# Social Page A-Press Diagnostic Plan (v3.3.36)

## Goal
Add targeted diagnostic logging to determine why the first A-press on the social page always fails to open the gift log, while the second A-press always succeeds.

## Background

### The Bug
On the Android social tab (GameMenu), pressing A to open a villager's gift log is inconsistent. The first A-press after entering/re-entering the page ALWAYS fails (`clickedEntry` stays -1). The second A-press works.

### What We Know From Decompiling the Android DLL

The Android `SocialPage.receiveLeftClick` (decompiled from `StardewValley-Android.dll` in project root) is completely different from PC. It uses a **two-phase click system**:

**Phase 1 — `receiveLeftClick` sets `clickedEntry`:**
```csharp
public override void receiveLeftClick(int x, int y, bool playSound = true)
{
    if (newScrollbar.sliderContains(x, y))
    {
        scrolling = true;
    }
    else if (newScrollbar.sliderContains(x, y) || newScrollbar.sliderRunnerContains(x, y))
    {
        float num = newScrollbar.setY(y);
        scrollArea.setYOffsetForScroll(-(int)(num * (float)scrollArea.getMaxYOffset() / 100f));
        updateSlots();
        Game1.playSound("shwip");
    }
    else
    {
        scrollArea.receiveLeftClick(x, y);   // <-- POSSIBLE SIDE EFFECT: may reposition sprites
    }
    for (int i = 0; i < sprites.Count; i++)
    {
        Rectangle val = new Rectangle(sprites[i].bounds.X, sprites[i].bounds.Y,
                                       sprites[i].bounds.Width, sprites[i].bounds.Height + 4);
        if (val.Contains(x, y))
        {
            clickedEntry = i;   // <-- Hit test uses SPRITES, not characterSlots
        }
    }
}
```

**Phase 2 — `releaseLeftClick` opens the profile:**
```csharp
public override void releaseLeftClick(int x, int y)
{
    base.releaseLeftClick(x, y);
    scrollArea.releaseLeftClick(x, y);
    if (clickedEntry >= 0 && clickedEntry < sprites.Count)
    {
        Rectangle val = new Rectangle(sprites[clickedEntry].bounds...);
        if (val.Contains(x, y))
        {
            // Opens ProfileMenu
            SocialEntry socialEntry = GetSocialEntry(clickedEntry);
            ProfileMenu profileMenu = new ProfileMenu(socialEntry, SocialEntries);
            Game1.activeClickableMenu = profileMenu;
        }
    }
    scrolling = false;
}
```

**Key insight:** `receiveLeftClick` hit-tests against `sprites[i].bounds`, NOT `characterSlots[i].bounds`. Our mod fixes `characterSlots` bounds but never touches `sprites`.

### What We DON'T Know (What This Diagnostic Must Answer)

1. **Are `sprites[i]` and `characterSlots[i]` the same object references?** If yes, our existing bounds fix already applies to `sprites` too, and the problem is elsewhere.

2. **What are `sprites[i].bounds` values?** We've never logged them. We need to know if they match `characterSlots` bounds (which we fixed) or have their original broken values.

3. **Does `scrollArea.receiveLeftClick()` trigger `updateSlots()` as a side effect?** This is the leading hypothesis for why the second click works: the first click's `scrollArea.receiveLeftClick` repositions `sprites` bounds, and the second click benefits from the corrected positions.

4. **Does `releaseLeftClick` fire after each A-press?** The profile only opens in `releaseLeftClick`. Touch simulation must generate both click and release events for the profile to open.

5. **Why does `clickedEntry` not match the slot index?** Slot 3 produced `clickedEntry=4`, slot 2 produced `clickedEntry=5`. The `sprites` list may have different ordering than `characterSlots`.

### Existing Log Evidence (from v3.3.35)

```
// First A on slot 0 → FAIL (clickedEntry stays -1)
receiveLeftClick(103,190) clickedEntryBefore=-1 → clickedEntryAfter=-1

// First A on slot 1 → FAIL
receiveLeftClick(103,328) clickedEntryBefore=-1 → clickedEntryAfter=-1

// First A on slot 2 → FAIL
receiveLeftClick(103,466) clickedEntryBefore=-1 → clickedEntryAfter=-1

// First A on slot 3 → FAIL
receiveLeftClick(103,604) clickedEntryBefore=-1 → clickedEntryAfter=-1

// SECOND A on slot 3 → SUCCESS (clickedEntry=4, not 3!)
receiveLeftClick(103,604) clickedEntryBefore=-1 → clickedEntryAfter=4

// Re-enter page, A on slot 3 → FAIL
receiveLeftClick(103,604) clickedEntryBefore=-1 → clickedEntryAfter=-1

// Then A on slot 2 → SUCCESS (clickedEntry=5, not 2!)
receiveLeftClick(103,466) clickedEntryBefore=-1 → clickedEntryAfter=5
```

Key: same coordinates `(103,604)` fail on first click, succeed on second. Something changes between clicks.

## File To Modify

`AndroidConsolizer/Patches/GameMenuPatches.cs` — all changes go in this one file.

## Exact Changes (4 diagnostic additions)

### 1. Dump `sprites[i].bounds` in `SocialReceiveLeftClick_Prefix`

In the existing `SocialReceiveLeftClick_Prefix` method (around line 566), ADD logging of `sprites` bounds. Use reflection to access the `sprites` field (it's a `List<ClickableTextureComponent>`). Log the first 6 entries' bounds and check if they're the same object references as `characterSlots`.

Add this inside the try block, after the existing logging:

```csharp
// DIAGNOSTIC: Dump sprites bounds and check if same objects as characterSlots
var spritesField = AccessTools.Field(__instance.GetType(), "sprites");
if (spritesField != null)
{
    var spritesList = spritesField.GetValue(__instance) as IList;
    var charSlots = _socialCharacterSlotsField?.GetValue(__instance) as IList;
    if (spritesList != null)
    {
        int dumpCount = Math.Min(6, spritesList.Count);
        for (int si = 0; si < dumpCount; si++)
        {
            var sprite = spritesList[si] as ClickableComponent;
            var charSlot = (charSlots != null && si < charSlots.Count) ? charSlots[si] as ClickableComponent : null;
            bool sameRef = (sprite != null && charSlot != null && ReferenceEquals(sprite, charSlot));
            string spriteBounds = sprite != null ? $"({sprite.bounds.X},{sprite.bounds.Y},{sprite.bounds.Width},{sprite.bounds.Height})" : "null";
            string charBounds = charSlot != null ? $"({charSlot.bounds.X},{charSlot.bounds.Y},{charSlot.bounds.Width},{charSlot.bounds.Height})" : "null";
            bool hitTest = sprite != null && new Rectangle(sprite.bounds.X, sprite.bounds.Y, sprite.bounds.Width, sprite.bounds.Height + 4).Contains(x, y);
            Monitor?.Log($"[SpriteDiag] sprites[{si}] bounds={spriteBounds} charSlots[{si}] bounds={charBounds} sameRef={sameRef} wouldHit({x},{y})={hitTest}", LogLevel.Info);
        }
    }
}
```

### 2. Patch `SocialPage.releaseLeftClick` (NEW — prefix only)

Add a new Harmony prefix patch on `SocialPage.releaseLeftClick`. Log: tick, coordinates, `clickedEntry` value, `scrolling` state. This confirms whether touch simulation generates release events.

In `Apply()`, add:
```csharp
var socialReleaseMethod = AccessTools.Method(typeof(SocialPage), "releaseLeftClick", new[] { typeof(int), typeof(int) });
if (socialReleaseMethod != null)
{
    harmony.Patch(
        original: socialReleaseMethod,
        prefix: new HarmonyMethod(typeof(GameMenuPatches), nameof(SocialReleaseLeftClick_Prefix))
    );
    Monitor.Log("SocialPage.releaseLeftClick diagnostic patch applied.", LogLevel.Trace);
}
```

New method:
```csharp
private static void SocialReleaseLeftClick_Prefix(SocialPage __instance, int x, int y)
{
    try
    {
        int clickedEntry = _socialClickedEntryField != null ? (int)_socialClickedEntryField.GetValue(__instance) : -999;
        var scrollingField = AccessTools.Field(__instance.GetType(), "scrolling");
        bool scrolling = scrollingField != null && (bool)scrollingField.GetValue(__instance);
        Monitor?.Log($"[SocialReleaseDiag] releaseLeftClick({x},{y}) clickedEntry={clickedEntry} scrolling={scrolling} tick={Game1.ticks}", LogLevel.Info);
    }
    catch (Exception ex)
    {
        Monitor?.Log($"[SocialReleaseDiag] ERROR: {ex.Message}", LogLevel.Error);
    }
}
```

### 3. Patch `SocialPage.updateSlots` (NEW — prefix only)

Track when `updateSlots()` fires relative to click events. This tests the hypothesis that `scrollArea.receiveLeftClick` triggers `updateSlots` as a side effect.

In `Apply()`, add:
```csharp
var updateSlotsMethod = AccessTools.Method(typeof(SocialPage), "updateSlots");
if (updateSlotsMethod != null)
{
    harmony.Patch(
        original: updateSlotsMethod,
        prefix: new HarmonyMethod(typeof(GameMenuPatches), nameof(SocialUpdateSlots_Prefix))
    );
    Monitor.Log("SocialPage.updateSlots diagnostic patch applied.", LogLevel.Trace);
}
```

New method:
```csharp
private static void SocialUpdateSlots_Prefix(SocialPage __instance)
{
    try
    {
        // Only log when near a click event (within 5 ticks of last A-press)
        // Use a static field to track last A-press tick
        if (Math.Abs(Game1.ticks - _lastSocialAPressTickDiag) <= 5)
        {
            var spritesField = AccessTools.Field(__instance.GetType(), "sprites");
            var spritesList = spritesField?.GetValue(__instance) as IList;
            string firstSpriteBounds = "?";
            if (spritesList != null && spritesList.Count > 0)
            {
                var s = spritesList[0] as ClickableComponent;
                if (s != null) firstSpriteBounds = $"({s.bounds.X},{s.bounds.Y},{s.bounds.Width},{s.bounds.Height})";
            }
            Monitor?.Log($"[UpdateSlotsDiag] updateSlots() called! tick={Game1.ticks} sprites[0].bounds={firstSpriteBounds}", LogLevel.Info);
        }
    }
    catch { }
}
```

Add static field near the other statics at the top of the class:
```csharp
private static int _lastSocialAPressTickDiag = -999;
```

And in `HandleSocialInput` case `Buttons.A`, add at the top of the case:
```csharp
_lastSocialAPressTickDiag = Game1.ticks;
```

### 4. Log `sprites` identity check in `FixSocialPage`

At the end of `FixSocialPage()` (around line 376, before the field dump block), add a one-time check:

```csharp
// One-time: check if sprites and characterSlots share object references
if (!_dumpedSocialFields) // piggyback on existing one-time guard
{
    var spritesField = AccessTools.Field(page.GetType(), "sprites");
    if (spritesField != null)
    {
        var spritesList = spritesField.GetValue(page) as IList;
        if (spritesList != null && charSlots != null)
        {
            bool allSame = true;
            int checkCount = Math.Min(spritesList.Count, charSlots.Count);
            for (int s = 0; s < checkCount; s++)
            {
                if (!ReferenceEquals(spritesList[s], charSlots[s]))
                {
                    allSame = false;
                    var sprite = spritesList[s] as ClickableComponent;
                    var slot = charSlots[s] as ClickableComponent;
                    Monitor?.Log($"[SpriteDiag] MISMATCH at [{s}]: sprites bounds=({sprite?.bounds.X},{sprite?.bounds.Y},{sprite?.bounds.Width},{sprite?.bounds.Height}) charSlots bounds=({slot?.bounds.X},{slot?.bounds.Y},{slot?.bounds.Width},{slot?.bounds.Height})", LogLevel.Info);
                    if (s >= 5) { Monitor?.Log("[SpriteDiag] (truncated, showing first 6 mismatches)", LogLevel.Info); break; }
                }
            }
            if (allSame)
                Monitor?.Log($"[SpriteDiag] ALL {checkCount} sprites and characterSlots are the SAME object references — fixing one fixes both", LogLevel.Info);
            else
                Monitor?.Log($"[SpriteDiag] sprites and characterSlots are DIFFERENT objects — sprites bounds are NOT being fixed!", LogLevel.Info);
        }
    }
}
```

## Version & Build

1. Update `manifest.json` version from `3.3.35` to `3.3.36`
2. Build: `cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer" && dotnet build AndroidConsolizer.csproj -c Release`
3. Deploy to Pocket Air:
```bash
"C:/Program Files/platform-tools/adb.exe" push "C:/Users/Jeff/Documents/Projects/Stardee Valoo/AndroidConsolizer/bin/Release/net6.0/AndroidConsolizer.dll" //sdcard/Android/data/abc.smapi.gameloader/files/Mods/AndroidConsolizer/AndroidConsolizer.dll
```
4. Launch: `"C:/Program Files/platform-tools/adb.exe" shell am force-stop abc.smapi.gameloader && "C:/Program Files/platform-tools/adb.exe" shell monkey -p abc.smapi.gameloader -c android.intent.category.LAUNCHER 1`

## Git Commit

```
git add AndroidConsolizer/Patches/GameMenuPatches.cs AndroidConsolizer/manifest.json
git commit -m "v3.3.36: Diagnostic logging for social page A-press — sprites vs characterSlots identity check, releaseLeftClick tracking, updateSlots timing"
```

## What To Look For In Logs

After testing (open social tab, press A on a few villagers, note which succeed/fail), pull logs:
```bash
"C:/Program Files/platform-tools/adb.exe" shell am force-stop abc.smapi.gameloader && "C:/Program Files/platform-tools/adb.exe" pull //sdcard/Android/data/abc.smapi.gameloader/files/ErrorLogs/SMAPI-latest.txt "C:/Users/Jeff/Documents/Projects/Stardee Valoo/AndroidConsolizer/bin/Release/net6.0/SMAPI-latest.txt"
```

Search for these log tags:
- `[SpriteDiag]` — sprites vs characterSlots identity and bounds comparison
- `[SocialReleaseDiag]` — whether releaseLeftClick fires after each A-press
- `[UpdateSlotsDiag]` — whether updateSlots fires during/after click processing
- `[SocialClickDiag]` — existing receiveLeftClick diagnostics (enhanced with sprites data)

### Decision tree from results:

1. **If `sameRef=True` for all entries** → Our bounds fix already applies to sprites. The problem is NOT sprites bounds. Look at `updateSlots` timing and `releaseLeftClick` data.

2. **If `sameRef=False`** → Sprites have separate bounds. Check `wouldHit` column: if sprites bounds don't contain the click point on first press but DO on second press, the first click's `scrollArea.receiveLeftClick` repositioned them. **Fix: also fix `sprites` bounds in `FixSocialPage()`.**

3. **If `releaseLeftClick` never fires** → Touch sim doesn't generate release events. **Fix: call profile-opening code directly from our A-press handler instead of relying on the two-phase system.**

4. **If `updateSlots` fires during first click processing** → Confirms the repositioning hypothesis. The first click primes layout, second click benefits.
