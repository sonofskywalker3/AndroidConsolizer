# Handoff — Options-page injection DONE (zoom works); NEXT = #78 dialogue choice-box cursor

**Date:** 2026-06-04 (continuation of the earlier same-day handoff)
**Tip:** **v3.8.31** (committed to `master`, local only — NOT pushed/released)
**Device:** G Cloud, loaded with the latest deploy, VerboseLogging ON. ADB may drop to wireless/idle — reconnect USB if `adb devices` is empty.

> **Push rule (current):** pushing commits to GitHub is fine anytime; only **releases** (`gh release create`, Nexus) need an explicit "yes." Work directly on `master`. One change per commit, PATCH `0.0.1` bump in `manifest.json` first; `git add <specific files>` (never `-A`). NEVER `/sdcard/` → use `/storage/emulated/0/`. Deploy + log-pull are the agent's job (`cd ../SyncdewValley; pwsh -NoProfile -File sync.ps1 deploy|logs`).

---

## What got done this session (all device-verified unless noted)

### #73 Seed placement box — ✅ DONE
Device-confirmed "seeds are perfect" (the v3.8.20 single red/green box, no ghost). Marked done in TODO/.

### #75 Shop buy-list scroll — ✅ DONE (the big one)
The v3.8.21 fix was **wrong** — it restored `ShopMenu.currentItemIndex`, which on Android is a **dead PC-paging vestige that stays 0**. The real buy-list scroll is the **MobileScrollbox `scrollArea` pixel offset**. The user caught it ("the cart test DID scroll, you just aren't capturing it"). Three correct commits:
- **v3.8.22** — `RebuildSaleButtonsAndRestoreSnap` now captures `scrollArea.getYOffsetForScroll()` before the rebuild and re-applies it after (recompute `maxYOffset`, clamp, `updateItemButtons()`). Fixes the snap-to-top. User: "tested well."
- **v3.8.23** — preserve a positive (top over-scroll) offset across a purchase so buying doesn't shift pre-existing whitespace.
- **v3.8.24** — **fix the CAUSE** of that whitespace: the game's own DPad-up nav overshoots the offset positive with no top clamp (`ShopMenu.cs:948-950`); `ClampScrollTop` (called each frame on the buy tab) pins it at 0. User: "the whitespace fix worked, good job."
- **4.0 epic captured:** a true console-style shop = a from-scratch custom `IClickableMenu` (there is NO built-in console ShopMenu on Android — the port replaced it wholesale; PC shop UI isn't in the binary). Use the PC-DLL decompile as a blueprint (can't ship copied game source); carries all transaction logic. Revisit only if the mobile shop still feels off.

### #77 Restore stripped vanilla Options to the Android Options page — ✅ DONE (7 commits)
Spec: `docs/superpowers/specs/2026-06-04-options-page-injection-design.md`; plan: `docs/superpowers/plans/2026-06-04-options-page-injection.md`. New file **`Patches/OptionsPageInjectionPatches.cs`** (owns "what's in the list + how far it scrolls"); navigation still lives in `OptionsPagePatches.cs`.
- **v3.8.25** dynamic scroll-fit — recompute `MobileScrollbox.maxYOffset` from the live options list each frame so **GMCM's button is reachable** (was below the scroll floor).
- **v3.8.26** inject native tool-hit checkboxes (whichOption 11, 12) + **retire the #76 force entirely** (removed `EnforceToolHitLocationOptions`, `Config.EnableToolHitLocation`, and its GMCM entry). #77 **supersedes #76**.
- **v3.8.27** hide the 5 touch-only options (139 Controls dropdown, 140 on-screen-controls toggle, 146 invisible-button width, 147 pinch-zoom, + the Adjust-joypad-controls button) when `gamepadControls`; GMCM toggle `HideTouchOptionsWithController` (default on).
- **v3.8.28** inject zoom slider (whichOption 18).
- **v3.8.29** console-ish ordering — tool-hit after "Show Advanced Crafting Information" (34), zoom before the first audio slider (1), via `InsertAfter`/`InsertBefore` by whichOption (not appended at the bottom).
- **v3.8.30** **zoom actually moves the render** — `ApplyMobileZoom` drives the mobile PinchZoom pipeline (`PinchZoom.Instance.SetZoomLevel(z)` + `Options.desiredBaseZoomLevel`/`baseZoomLevel` + `Game1.game1.Window_ClientSizeChanged(null,null)` + `forceSnapOnNextViewportUpdate`), mirroring the game's own programmatic zoom-set at `Game1.cs:1782-1787`. Setting `desiredBaseZoomLevel` alone is ignored on mobile (the device test proved it: pinch moved the number, the slider didn't).
- **v3.8.31** widen the zoom bar to **50-200** (mobile zoom is ~46-400%, default 150; the 100 cap was below default).
- **Device status:** user confirmed zoom works. **Only open item: user's final nod that the 50-200 range feels right.** Engineering verified from the log ("OptionsPage injection patches applied", no errors).

---

## NEXT TASK — #78 Dialogue choice boxes: console finger-cursor + red outline (not yellow tint)

**User ask (verbatim, 2026-06-04, in-game):** "on console the choice boxes like going to sleep use the finger cursor and red outline to show what's selected, I hate the yellow tinting we do, but it was good enough before, now I want you to add fixing that to this release."

- **Scope:** dialogue/question choice boxes — `DialogueBox` question responses (the "go to sleep?" Yes/No prompt, NPC yes/no, festival choices, etc.). Match console: a **red outline + finger/hand cursor** on the selected response; **drop the yellow tint**.
- **Diagnose first:** read the Android `DialogueBox.cs` (decompile) response-rendering to find where the selected response is currently drawn with the yellow tint — confirm whether it's AC (`Patches/DialogueBoxPatches.cs` already exists and is registered) or vanilla Android. Then replace the tint with the console indicator: red `Game1.staminaRect` outline + `Game1.mouseCursors` finger/cursor tile at the selected response.
- **This is creative UI work → use `superpowers:brainstorming` first.** A couple of quick questions to resolve: (a) exactly which menus count (just `DialogueBox` questions, or also NumberSelection/other choice UIs?), (b) the precise look (red outline thickness/inset; finger cursor to the left of the selected response, like console); (c) does AC already draw a console cursor elsewhere we should match (e.g. the shop/CC-bundle cursor-draw patterns).
- **File:** `Patches/DialogueBoxPatches.cs`. TODO #78 has the notes.

---

## Coding gotchas learned this session (verify before reusing)
- **Android shop buy-list scroll = `ShopMenu.scrollArea` (MobileScrollbox) pixel offset, NOT `currentItemIndex`** (that's a PC-paging vestige, stays 0). `rebuildSaleButtons` rebuilds buttons unscrolled; `update()` only re-applies the offset during momentum.
- **`OptionsCheckbox(label,which)` / `OptionsSlider(label,which,x,y,width)` ctors self-sync** their displayed value (they call `setCheckBoxToProperValue`/`setSliderToProperValue`), so injected controls show the right state with no extra wiring. Options handles 11/12/18 in those switches.
- **Android-only members needing reflection** (absent/different on the PC DLL the mod compiles against): `OptionsElement.ItemHeight`, the 5-arg `OptionsSlider` ctor, `OptionsPage.optionsButtonAdjustControls`, all `MobileScrollbox.*`, and `StardewValley.Mobile.PinchZoom` (resolve by name via `AccessTools.TypeByName`).
- **Mobile render zoom = PinchZoom pipeline** (`PinchZoom.Instance` / `MobileDisplay.ZoomScale` / `Game1.NativeZoomLevel`). Setting `Options.desiredBaseZoomLevel` alone does nothing; mirror `Game1.cs:1782-1787` and call `Window_ClientSizeChanged` to apply.

## 3.9.0 bundle (next public release, local-only so far)
#25, #25b, #74, #76 (superseded by #77's native checkboxes), #73, #75, **#77 (Options injection incl. working zoom)**, and pending **#78**. 4.0 = Right Stick Update (#12, #62) + the console-shop-menu epic.
