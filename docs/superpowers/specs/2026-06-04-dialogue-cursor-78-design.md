# #78 — Dialogue Choice Boxes: Console Finger-Cursor + Red Outline

**Date:** 2026-06-04
**Status:** Approved, implementing
**Milestone:** 3.9.0 (next public release, local-only)
**File:** `Patches/DialogueBoxPatches.cs` (extend existing), `ModConfig.cs`, `ModEntry.cs` (GMCM)

## Problem

On console (Switch), a `DialogueBox` question prompt (e.g. "go to sleep?" Yes/No, NPC
yes/no, festival/event choices) shows the selected response with the **regular finger
cursor + a red outline**. On Android the selected response is shown with a **yellow
highlight box + faded text**, which the user dislikes.

## Diagnosis

AC does not patch dialogue drawing today (the existing `DialogueBoxPatches` only
pre-selects the top option in a `setUpQuestions` postfix). The yellow look is **vanilla
Android's own** rendering in `DialogueBox.draw` (`DialogueBox.cs:984-998`):

- Selected response → highlighted texture box `(267,256,10,10)` + text at **0.6 alpha**.
- Unselected responses → plain box `(256,256,10,10)` + full-alpha text.

So "what's selected" reads as the yellow box + faded text. That is the target to replace.

The console parity reference: a **red outline** matching the tool-hit-location box border
(`Farmer.cs:6368`, `mouseCursors` tile 29, world scale) and the **regular finger cursor**
(`mouseCursors` tile 0).

## Approach — chosen

A **postfix on `DialogueBox.draw`** (patching `draw` is safe on Android; only
`releaseLeftClick` is the documented landmine). Runs after vanilla draw and overlays the
**selected** response only. Minimal blast radius — no transpiler, no full draw
reimplementation. Unselected options, question text, portraits, transitions all stay
vanilla.

Rejected alternatives:
- **Transpiler swapping `267→256`** — kills the yellow box but leaves the text faded, so a
  postfix is still needed; net more fragile (IL on Android mono) for no gain.
- **Full `draw` prefix reimplementation** — must replicate portrait/icon/transition/font
  edge cases; high regression risk.

## Behavior

Gate (all must hold): `Config.EnableConsoleDialogueCursor`, `__instance.isQuestion`,
`!__instance.transitioning`, gamepad mode (`Game1.options.gamepadControls &&
!Game1.lastCursorMotionWasMouse`), `responses` non-empty, `selectedResponse` in
`[0, responses.Length)`, and responses are visible
(`characterIndexInDialogue >= getCurrentString().Length - 1`).

Steps for the selected response:

1. **Recompute layout** — replicate the `num` Y accumulation from `draw`:
   `num = y - (heightForQuestions - height) + getHeightOfString(getCurrentString(), width-48) + 48`,
   then add `getHeightOfString(responses[i].responseText, width-80) + 16 + 32` for each
   `i < selectedResponse`.
   Box rect: `boxX = x + 12`, `boxY = num - 16`, `boxW = width - 32`,
   `boxH = getHeightOfString(responses[sel].responseText, width-80) + 32`
   (matches the vanilla selected/yellow rect exactly, so coverage is pixel-clean).
2. **Cover the yellow box** — `IClickableMenu.drawTextureBox(b, Game1.mouseCursors,
   new Rectangle(256,256,10,10), boxX, boxY, boxW, boxH, Color.White, 4f)`. Opaque 9-slice
   over identical rect → fully hides the yellow box and its faded text.
3. **Redraw selected text at full alpha** — `SpriteText.drawString(b,
   responses[sel].responseText, Utility.To4(x+40), Utility.To4(num + 4 -
   (responses.Length > 2 ? 4 : 0)), 999999, width-80, 999999, 1f)` (same position vanilla's
   selected branch used; alpha 1f instead of 0.6f).
4. **Red outline** — 4 `Game1.staminaRect` bars (top/bottom/left/right) around the box,
   thickness `OUTLINE_THICKNESS = 4` (1 source-px × the menu's 4× scale, matching the
   tool-hit box border). Color = sampled once from `mouseCursors` tile 29 (the reddest
   non-transparent pixel) and cached in a static `Color?`; try/catch fallback to
   `new Color(255, 0, 0)` if `GetData` is unavailable on the runtime.
5. **Finger cursor** — `b.Draw(Game1.mouseCursors, new Vector2(boxX + boxW - 16,
   boxY + boxH - 16), getSourceRectForStandardTileSheet(mouseCursors, 0, 16, 16),
   Color.White, 0f, Vector2.Zero, 4f, ...)` — regular pointing finger anchored at the
   box bottom-right corner (same anchor math as the inventory held-item draw).

Whole postfix body wrapped in try/catch (VerboseLogging-gated error log).

## Platform notes

- Direct field access for `x/y/width/height/responses/selectedResponse/isQuestion/
  transitioning/characterIndexInDialogue/heightForQuestions` — all standard SDV fields
  present on both PC DLL and Android (the existing patch already reads
  `responses`/`selectedResponse` directly). If `heightForQuestions` fails to compile
  against the PC reference DLL, reflect it via `AccessTools.Field` (cached).
- `Utility.To4`, `SpriteText.*`, `IClickableMenu.drawTextureBox`,
  `getSourceRectForStandardTileSheet` are all public on both platforms.

## Out of scope

NumberSelectionMenu and other non-`DialogueBox` choice UIs. Touch/mouse path (keeps
vanilla). Box alignment polish beyond covering the yellow rect.

## Implementation order (one commit each, PATCH 0.0.1)

1. Add `EnableConsoleDialogueCursor` to `ModConfig.cs` + GMCM entry in `ModEntry.cs`.
2. Add the `Draw_Postfix` to `DialogueBoxPatches.cs` and register it.

## Verification

- Engineering: build clean; log shows "DialogueBox patches applied." with no errors.
- Device (G Cloud, meaningful playtest): open the sleep Yes/No prompt and an NPC yes/no
  — confirm the selected option shows the red outline + finger at bottom-right, no yellow,
  text full-strength; D-pad up/down moves the indicator; touch still shows vanilla.
