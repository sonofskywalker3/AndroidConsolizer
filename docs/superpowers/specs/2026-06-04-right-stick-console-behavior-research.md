# Right Stick — Console Behavior Research (for v4.0 "The Right Stick Update")

**Date:** 2026-06-04
**Status:** Research complete; feeds TODO #12 (Right Joystick Cursor Mode) + #62 (Right-Stick Furniture Ghost). Write an implementation spec from this before coding.
**Method:** Stardew wiki/community sources + Android decompile (`…\decompiler\stardew-valley-android\…\Game1.cs`).

## Headline finding

On console the right stick **is the free mouse cursor** — the *same* cursor that drives tool targeting, tile interaction, furniture/building placement, and menu clicking. There is no separate "aim" system. **All of this logic already exists, fully written, in the Android build**, gated only on `options.gamepadControls`. The right-stick→`setMousePositionRaw` call sits in `Game1.UpdateControlInput`. The reason Android doesn't behave like console is almost certainly that (a) the mobile control path `_mobileUpdateControlInput` runs and drives `mostRecentlyUsedControlType = TOUCH` (which suppresses `drawMouse` — the same trap as the #18 museum cursor), and/or (b) `gamepadControls` is effectively off at runtime. So #12 is largely "let the existing engine path run + actually draw the cursor," not "build a cursor system."

## Behavior by context

### Overworld / gameplay
- **Right stick moves a free on-screen mouse cursor.** That cursor determines where tools hit (hoe/can/pickaxe/axe target tile), what you check/interact with, and gift targeting. Tool targeting falls out for free — the game already aims tools at the mouse tile.
- **Cursor auto-hides after inactivity (~4 s)**, reverting tool targeting to "in front of the player."
- **R3 (click the right stick) = open in-game Chat** (tap).
- **Hold R3 ≥ 250 ms = open the Emote wheel** (aim with stick, release/confirm to emote). Tap-vs-hold threshold = `emoteMenuShowTime = 250`.
- **Right stick does NOT control zoom.** Zoom is the Options "Zoom Level" slider (already shipped in AC as #77).

### Menus (general)
- **Left stick / D-pad = snap navigation; right stick = scroll** (wiki "Menu" column).
- Right stick Y is translated into scroll-wheel events for scrollable menus even with snappy menus on.
- "Click" = move cursor + **A** (A = left-click in menus). R3 is overworld chat/emote only, not a menu click.
- Moving the stick flips `lastCursorMotionWasMouse` + resets `timerUntilMouseFade`, which is how the game toggles snap-mode ↔ free-cursor rendering and back when idle.

### Specific menus / modes
| Context | Right stick on console |
|---|---|
| Furniture / rug / wallpaper placement | Moves the placement cursor; the ghost follows it. A = pick up, move, Y = place; a-button rotates while held. The canonical right-stick use-case (= AC #62). |
| Carpenter / Robin build menu | Moves the building-placement cursor (the ghost). |
| Map | Moves a cursor over the map (read labels / warp targets); does **not** pan a viewport. |
| Shops / inventory / chests / crafting / GameMenu tabs | Right stick = **scroll**; left stick / D-pad = snap-select; A = select/buy. |
| Fishing minigame | No right-stick role. |
| Combat / slingshot | Right stick does **nothing**; slingshot aim is the **left** stick (matches AC #25b). |
| Minigames (Junimo Kart, Prairie King, casino, festival games) | Right stick is converted into **D-pad key events** (±0.2 threshold) — a distinct code path. |
| Zoom | **Not** the right stick — Options slider. |

**Switch 2 caveat:** the Dec 2025 Switch 2 update added native right-Joy-Con mouse mode, which changed/sometimes broke right-stick cursor behavior on that platform. Not relevant to Android, but post-Dec-2025 Switch videos may look different.

## Behavioral details (for fidelity)
- **Dead zone:** `0` for the cursor (any non-zero stick value moves it, `Game1.cs:13306`); the scroll/minigame paths use **0.2**.
- **Cursor speed / acceleration:** `thumbstickToMouseModifier = _cursorSpeed/720f * viewport.Height * elapsedSeconds` (`Game1.cs:1986-1996`); `ComputeCursorSpeed` (`5554+`) ramps base ~0.7→2.0 with a boost when stick length > 0.9. Frame-rate-normalized.
- **Auto-hide:** `timerUntilMouseFade = 4000` ms; counts down in `drawMouseCursor` (`15618-15628`); at 0 with `gamepadControls`, `mouseCursorTransparency = 0`.
- **Screen constraint:** `setMousePositionRaw` → clamped to window by the input layer.

## Decompile findings (Android — code that already exists)
All in `…\StardewValley\Game1.cs`:
1. **Overworld right-stick → cursor**, `UpdateControlInput` ~`13298-13334`: `if (options.gamepadControls) { if (|Right.X|>0 || |Right.Y|>0) setMousePositionRaw(mouseX + Right.X*mod, mouseY - Right.Y*mod); timerUntilMouseFade = 4000; }`. The entire overworld behavior, complete.
2. **R3 → chat / hold → emote:** hold accumulates `rightStickHoldTime` (`4471-4473`, reset `4503-4505`); tap→chat `UpdateChatBox` (`13271`, `rightStickHoldTime < emoteMenuShowTime`); hold→emote (`14229-14237`, `>= emoteMenuShowTime` → `new EmoteMenu{ gamepadMode = true }`). Constants: `emoteMenuShowTime=250` (`2961`), `rightStickHoldTime` (`413`), `timerUntilMouseFade` (`804`).
3. **Menu right-stick → scroll**, `updateActiveMenu` `5747-5769` (+ textEntry `6013-6035`): `Right.Y > 0.2 → receiveScrollWheelAction(1)`, `< -0.2 → (-1)`, polling timer `220 - |Y|*170`.
4. **Menu left-stick → free cursor**, `5665-5670`: gated `!snappyMenus || overrideSnappyMenuCursorMovementBan()`.
5. **Minigame right-stick → D-pad key events**, `4834-4865`: R-stick X/Y (±0.2) → `currentMinigame.receiveKeyPress/Release(Up/Down/Left/Right)`.
6. **Cursor-speed model:** `thumbstickToMouseModifier` `1986-1996`; `ComputeCursorSpeed` `5554+`.
7. **Cursor fade/visibility:** `drawMouseCursor` `15618-15628`; `setMousePositionRaw` `5532-5537`.

**Helper signatures on Android:** `Game1.setMousePositionRaw(int,int)` (public static `5532`), `Game1.setMousePosition(Point)` (`5527`), `Game1.thumbstickToMouseModifier` (private static — **reflect**), `Game1.timerUntilMouseFade` (public static int `804`), `Game1.lastCursorMotionWasMouse` (public static bool `673`), `Game1.rightStickHoldTime` (public static int `413`), `Game1.emoteMenuShowTime` (public static int `415`).

## Implementation implications for AC
1. **Overworld cursor:** per tick, if right stick non-zero, `Game1.setMousePositionRaw(mouseX + RawRightStickX*mod, mouseY - RawRightStickY*mod)` — `RawRightStickX/Y` are already cached in `GameplayButtonPatches`; `mod` via reflected `thumbstickToMouseModifier` (or replicate `_cursorSpeed/720*viewport.Height*dt`).
2. **Force the cursor to draw:** the mobile layer suppresses `drawMouse` (control type reads TOUCH — known #18 issue). Either set `mostRecentlyUsedControlType = GAMEPAD` on right-stick motion, or draw the cursor ourselves (AC already has the pattern) and keep `timerUntilMouseFade = 4000` on motion.
3. **Auto-hide:** mirror `timerUntilMouseFade`; hide + revert tool targeting to facing tile on expiry.
4. **Tool targeting is free** once the cursor moves — the game aims tools at the mouse tile.
5. **R3 tap = chat, hold ≥250 ms = emote:** decide whether chat is desirable on Android single-player (may want hold-emote only, tap-chat suppressed).
6. **Menus = scroll:** feed right-stick Y to `childMenu.receiveScrollWheelAction(±1)` at 0.2 with the `220-|Y|*170` timer; coexist with AC's snap nav (left/D-pad selects, right scrolls).
7. **Placement menus (furniture #62 / carpenter):** drive the existing ghost off the same cursor — AC already has a CarpenterMenu ghost-follow-cursor (`GetMouseState` override); wire the right stick into that cursor.
8. **Minigames:** optionally translate right-stick → D-pad key events (low priority; left stick covers most).
9. **Do NOT touch zoom from the right stick** — stays the #77 Options slider.
10. **Snap ↔ free toggle:** flip `lastCursorMotionWasMouse` / reset fade on motion so the game's own rendering switches snap↔free and resumes snap when idle.

## Open questions / confidence
- **High:** right stick = cursor; R3 tap=chat / hold=emote (250 ms); menu right stick = scroll; minigame right stick = D-pad; zoom is a slider; slingshot aim = left stick.
- **Medium:** one third-party guide claims right-stick scrolls the toolbar in the *overworld*; the wiki maps toolbar switching to shoulders/triggers. Treat overworld right stick as cursor-only unless a toolbar-scroll mapping is specifically wanted.
- **Decide for Android:** whether to enable R3-chat at all in single-player; whether to ship the minigame D-pad translation; whether the overworld cursor should be opt-in (it changes tool targeting from facing-tile to cursor-tile, which is a real feel change some players may not want — likely a GMCM toggle).

**Sources:** Stardew Valley Wiki (Controls, Options); community controls guides; SDV forums (emotes/right stick, console zoom, Switch 2 control issues); GameRant (rotate furniture); decompile line refs inline above.
