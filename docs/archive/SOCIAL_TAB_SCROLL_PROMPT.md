# Fresh Agent Prompt: Social Tab Scrolling

## Your Task

Implement dpad/left-stick scrolling on the Social tab (tab 2) of the GameMenu in the AndroidConsolizer mod for Stardew Valley on Android. The current code (v3.3.25) lets the user navigate between the 4 visible villager slots with dpad, but can NOT scroll past them to reach the other 33 villagers. You need to make it so pressing down past the last visible slot scrolls the list down (and up past the first scrolls up).

**A-press (opening gift log/profile) is ALSO unreliable.** It currently sets `clickedEntry` field on SocialPage via reflection, which was thought to work in v3.3.25 but testing shows it's inconsistent. You need to fix BOTH scrolling AND A-press. See the "A-Press Problem" section below for what we know.

## What You're Working With

### The Mod
- SMAPI mod for Stardew Valley Android, using Harmony patches
- All social tab code is in `AndroidConsolizer/Patches/GameMenuPatches.cs`
- Build: `dotnet build AndroidConsolizer.csproj -c Release` from `AndroidConsolizer/` directory
- Deploy to Ayaneo device via ADB (see CLAUDE.md / MEMORY.md for exact commands)

### Current Architecture (v3.3.25)
The Social tab has 37 villager slots but only 4 are visible at a time. The code has:

1. **`FixSocialPage()`** — Called when switching to Social tab. Fixes slot bounds (all stuck at Y=0 on Android) by computing visual positions based on `slotPosition`. Assigns bounds Y = slotsYStart + (slotIndex - slotPosition) * slotHeight.

2. **`HandleSocialInput()`** — Intercepts `receiveGamePadButton` for the Social tab. Handles A (open profile), DPadDown/Up (navigate slots).

3. **`NavigateSocialSlot(direction)`** — Moves cursor between slots. Currently attempts to call `page.receiveScrollWheelAction()` when navigating past visible area, but **this is a no-op on Android's SocialPage** — slotPosition never changes.

4. **`RefixSocialBounds()`** — Re-fixes slot bounds after slotPosition changes.

### Key Fields (accessed via reflection)
- `characterSlots` — IList of 37 ClickableComponents (one per villager)
- `slotPosition` — int, first visible slot index (stays 0, never changes from scroll)
- `slotHeight` — int, height per slot (138)
- `mainBox` — Rectangle, visible area ({X:0 Y:88 W:877 H:634})
- `slotsYStart` — int, Y offset for first slot within mainBox (34)
- `clickedEntry` — int, set to slot index to open that villager's profile (-1 = none)
- `scrollArea` — MobileScrollbox instance that wraps the page

### The MobileScrollbox (the rendering system)
This is the key piece. On Android, the SocialPage is wrapped in a `MobileScrollbox` that handles ALL scrolling and rendering:

- **`getYOffsetForScroll()`** — Current scroll offset in pixels. 0 = top, goes NEGATIVE when scrolled down (e.g., -1380 means scrolled down 10 slots)
- **`setYOffsetForScroll(int)`** — DO NOT USE. Corrupts internal momentum state. Values decay by 2px/frame and right-stick scroll produces garbage negative values afterward.
- **`maxYOffset`** — 4554 (= 33 slots * 138 height = (37 total - 4 visible) * slotHeight)
- **`receiveScrollWheelAction(int)`** — Exists on scrollbox but unclear if it works reliably
- **`receiveLeftClick/leftClickHeld/releaseLeftClick`** — Touch handling methods
- **`update(GameTime)`** — Runs every frame, manages momentum/decay

**Critical facts about the scrollbox:**
- `slotPosition` stays at 0 regardless of scroll. The scrollbox uses `yOffsetForScroll` for rendering, NOT slotPosition.
- Writing to `setYOffsetForScroll()` CORRUPTS the scrollbox. Don't do it.
- Writing to `slotPosition` directly garbles the display (double-offset with scrollbox rendering).
- Right-stick scroll works natively (the scrollbox handles it). yOffset goes negative in steps of ~136-140.

### What Was Already Tried (and failed, v3.3.26-v3.3.33)

1. **Set both slotPosition + yOffset** (v3.3.26) — Double-offset, broke everything
2. **Set slotPosition only** (v3.3.27) — Android doesn't use slotPosition for rendering, garbled display
3. **Set yOffset only** (v3.3.28) — Corrupts scrollbox momentum, values decay, breaks right-stick scroll and A-press
4. **Call scrollbox.receiveScrollWheelAction()** (v3.3.29) — Still broke rendering and A-press
5. **Direct scrollbox.receiveLeftClick/releaseLeftClick** (v3.3.31) — Doesn't set clickedEntry; scrollbox processes taps asynchronously
6. **GetMouseState override** (v3.3.32) — Cursor flickering, slot switching, side effects
7. **Deferred clickedEntry in Draw_Postfix** (v3.3.33) — Untested but irrelevant to scroll

**All of these tried to manipulate the scrollbox or its state directly. None worked.**

### Diagnostic Data from v3.3.31 (the most informative run)

When the user scrolls with the RIGHT STICK (which works natively):
```
yOffset=-136, slotPosition=0
yOffset=-276, slotPosition=0
yOffset=-412, slotPosition=0
... (continues in steps of ~136-140)
yOffset=-3588, slotPosition=0
```
- yOffset goes negative in consistent steps
- slotPosition NEVER changes
- The scrollbox handles all rendering based on yOffset

When the user presses A after scrolling, `clickedEntry` gets set by the game's touch simulation based on the REAL mouse cursor position mapped through yOffset. The formula: `slot = (mouseY - scrollboxY - yOffset) / slotHeight`. This is how the game natively opens profiles on touch.

## A-Press Problem

Setting `clickedEntry` via reflection was thought to work but is **inconsistent**. From v3.3.31 diagnostic data:

- **Setting `clickedEntry` directly does NOT reliably open profiles.** Sometimes it works, sometimes it doesn't.
- **The game's touch simulation is what actually opens profiles.** When the user physically touches a villager, the MobileScrollbox handles the touch and eventually opens the profile. The touch sim reads the **real mouse cursor position** from `GetMouseState()` and maps it through yOffset to determine which slot was tapped.
- **Formula:** `slotIndex = (mouseY - scrollboxBoundsY - yOffset) / slotHeight`. When the real mouse cursor happened to be at ~Y:124 and yOffset=-1240, clickedEntry was set to 9. At yOffset=-2068, it became 15. The pattern is 100% consistent — it's the mouse position that matters.
- **At yOffset=0 with mouse at default position**, clickedEntry stays -1 (the default mouse position is probably off-screen or outside the scrollbox).
- **GetMouseState override** (CarpenterMenu pattern) caused cursor flickering and slot switching — too many side effects for this page.
- **Direct scrollbox.receiveLeftClick/releaseLeftClick** doesn't set clickedEntry (it's async, processed later by update()).

The A-press fix needs a different approach. Some ideas:
1. Find the actual method that opens the profile (not just clickedEntry) and call it directly
2. Dump all SocialPage methods to find candidates like `_performClick`, `showCharacter`, etc.
3. Simulate a complete touch gesture at the correct position without GetMouseState side effects
4. Patch SocialPage.update() to intercept clickedEntry processing

## Design Philosophy

**"Fix the data, don't fight the system."** The scrollbox is a complex active system with momentum, decay, and internal state. Fighting it (writing yOffset, writing slotPosition) has failed 8 times.

Instead of trying to scroll the scrollbox programmatically, consider completely different approaches:

### Ideas to Explore (you're not limited to these)

1. **Simulate the right-stick input** — The right stick DOES scroll the list. What if dpad-down at the boundary temporarily injects right-stick input into the gamepad state? The scrollbox already knows how to handle right-stick scroll.

2. **Use the scrollbox's own touch/drag simulation** — Real touch scrolling works. Simulate a drag gesture (receiveLeftClick at Y=500, leftClickHeld at Y=400, releaseLeftClick at Y=400) to scroll one slot's worth.

3. **Don't scroll the scrollbox at all** — Keep the scrollbox at yOffset=0. Instead, change WHICH villager data each slot displays. This would mean the slots always show slotPosition through slotPosition+3, but we control slotPosition ourselves. The scrollbox stays still; we rotate the data.

4. **Patch the scrollbox's update()** — Instead of trying to set yOffset directly (which corrupts state), patch the update method to smoothly animate to a target yOffset. Set a `_targetYOffset` variable and let the patched update() gradually move toward it.

5. **Something else entirely** — You might find a better approach by examining how other menus with scrollboxes work on Android.

## Rules

1. **One change per version bump (0.0.1)**
2. **Diagnose first, fix second** — Build diagnostic versions to understand behavior before attempting fixes
3. **Commit every change** with `git add <specific files> && git commit -m "vX.X.X: description"`
4. **NEVER break A-press** — Always verify clickedEntry still works
5. **Start from v3.3.25** — This is the clean baseline. Bump to v3.3.26 for your first change.
6. Read CLAUDE.md and MEMORY.md for build/deploy/commit procedures

## Build & Deploy Quick Reference
```bash
# Build
cd "C:\Users\Jeff\Documents\Projects\Stardee Valoo\AndroidConsolizer"
dotnet build AndroidConsolizer.csproj -c Release

# Deploy to Ayaneo
"C:/Program Files/platform-tools/adb.exe" push "C:/Users/Jeff/Documents/Projects/Stardee Valoo/AndroidConsolizer/bin/Release/net6.0/AndroidConsolizer.dll" "//sdcard/Android/data/abc.smapi.gameloader/files/Mods/AndroidConsolizer/AndroidConsolizer.dll"
"C:/Program Files/platform-tools/adb.exe" push "C:/Users/Jeff/Documents/Projects/Stardee Valoo/AndroidConsolizer/manifest.json" "//sdcard/Android/data/abc.smapi.gameloader/files/Mods/AndroidConsolizer/manifest.json"
"C:/Program Files/platform-tools/adb.exe" shell monkey -p abc.smapi.gameloader -c android.intent.category.LAUNCHER 1

# Pull logs
"C:/Program Files/platform-tools/adb.exe" shell am force-stop abc.smapi.gameloader
"C:/Program Files/platform-tools/adb.exe" pull "//sdcard/Android/data/abc.smapi.gameloader/files/ErrorLogs/SMAPI-latest.txt" "C:/Users/Jeff/Documents/Projects/Stardee Valoo/AndroidConsolizer/bin/Release/net6.0/SMAPI-latest.txt"
```
