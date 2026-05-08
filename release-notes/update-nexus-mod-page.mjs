// Playwright script: update the Nexus Mods mod page (version + description)
// for AndroidConsolizer (mod 41869, game stardewvalley).
//
// Approach: launches Playwright Firefox and injects cookies extracted from
// the user's actual Firefox profile (via dump-firefox-cookies.py). Same
// browser engine + same cookies = passes Cloudflare and lands logged-in on
// the edit page.
//
// Prerequisite: run `python dump-firefox-cookies.py > nexus-cookies.json`
// in this directory first.
//
// Flow:
//   1. Reads cookies from nexus-cookies.json.
//   2. Launches Firefox, injects cookies.
//   3. Navigates to edit page.
//   4. Searches for version + description form fields (inputs, textareas,
//      contenteditable, and inside iframes).
//   5. Fills them.
//   6. Pauses with the browser open — user reviews, clicks Save in Nexus's
//      UI, closes the window. Script exits when the browser closes.
//
// Usage:
//   node update-nexus-mod-page.mjs

import { firefox } from 'playwright';
import { readFileSync, existsSync } from 'fs';
import { fileURLToPath } from 'url';
import { dirname, resolve, join } from 'path';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const PROJECT_ROOT = resolve(__dirname, '..');

const MOD_ID = 41869;
const GAME_DOMAIN = 'stardewvalley';
const EDIT_URL = `https://www.nexusmods.com/games/${GAME_DOMAIN}/mods/${MOD_ID}/edit`;

const COOKIES_PATH = process.env.NEXUS_COOKIES_FILE
  || resolve(__dirname, 'nexus-cookies.json');
const DESC_PATH = process.env.NEXUS_DESCRIPTION_FILE
  || resolve(__dirname, 'nexus-3.6.0-description.bbcode');
const VERSION = process.env.NEXUS_VERSION
  || JSON.parse(readFileSync(join(PROJECT_ROOT, 'manifest.json'), 'utf8')).Version;

if (!existsSync(COOKIES_PATH)) {
  console.error(`Cookies file not found: ${COOKIES_PATH}`);
  console.error('Run: python dump-firefox-cookies.py > nexus-cookies.json');
  process.exit(1);
}
if (!existsSync(DESC_PATH)) {
  console.error(`Description file not found: ${DESC_PATH}`);
  process.exit(1);
}

const cookies = JSON.parse(readFileSync(COOKIES_PATH, 'utf8'));
const description = readFileSync(DESC_PATH, 'utf8');

console.log(`Cookies     : ${COOKIES_PATH} (${cookies.length} cookies)`);
console.log(`Description : ${DESC_PATH} (${description.length} chars)`);
console.log(`Version     : ${VERSION}`);
console.log(`Edit URL    : ${EDIT_URL}`);
console.log();

const browser = await firefox.launch({ headless: false });
const context = await browser.newContext({ viewport: { width: 1400, height: 900 } });

// Add cookies. Playwright's strict cookie validator rejects:
//   - properties it doesn't recognize (strip to the known set);
//   - expires values that aren't -1 or a positive Unix timestamp (some
//     Firefox cookies have 0 or non-integer values — coerce to -1 = session).
await context.addCookies(cookies.map(c => {
  const out = {
    name: c.name,
    value: c.value,
    domain: c.domain,
    path: c.path,
    secure: !!c.secure,
    sameSite: c.sameSite || 'Lax',
  };
  // Firefox/browser_cookie3 reports expires in milliseconds; Playwright wants
  // Unix seconds. Anything above 1e11 (~year 5138 in seconds = ~year 1973 in ms)
  // is clearly in ms — divide. Below that, treat as seconds. -1 = session cookie.
  let exp = Number(c.expires);
  if (!Number.isFinite(exp) || exp <= 0) {
    exp = -1;
  } else if (exp > 1e11) {
    exp = Math.floor(exp / 1000);
  } else {
    exp = Math.floor(exp);
  }
  out.expires = exp;
  return out;
}));

context.on('close', () => { console.log('Context closed.'); });
browser.on('disconnected', () => {
  console.log('Browser closed. Done.');
  process.exit(0);
});

const page = await context.newPage();

console.log('Navigating to edit page...');
await page.goto(EDIT_URL, { waitUntil: 'networkidle', timeout: 60_000 }).catch(e => {
  console.warn(`Navigation hiccup: ${e.message}. Continuing.`);
});

console.log(`Now on: ${page.url()}`);

// Quick sanity check: are we logged in?
const pageBody = await page.evaluate(() => document.body.innerText.slice(0, 500));
const looksLikeLogin = /sign in|log in|sign\s*up/i.test(pageBody);
console.log(`Page text preview: ${pageBody.replace(/\s+/g, ' ').slice(0, 200)}`);
if (looksLikeLogin) {
  console.warn('Page text contains login-like keywords — cookie may not have authenticated us.');
}

// Look for form fields. Try main page AND every iframe.
async function findInFrames(frame, depth = 0) {
  const indent = '  '.repeat(depth);
  console.log(`${indent}Searching frame: ${frame.url() || '(main)'}`);
  const counts = await frame.evaluate(() => ({
    inputs: document.querySelectorAll('input').length,
    visibleInputs: Array.from(document.querySelectorAll('input')).filter(i => i.type !== 'hidden').length,
    textareas: document.querySelectorAll('textarea').length,
    contenteditable: document.querySelectorAll('[contenteditable="true"]').length,
  })).catch(() => ({ inputs: 0, visibleInputs: 0, textareas: 0, contenteditable: 0 }));
  console.log(`${indent}  inputs=${counts.inputs} visibleInputs=${counts.visibleInputs} textareas=${counts.textareas} contenteditable=${counts.contenteditable}`);

  if (counts.textareas > 0 || counts.visibleInputs > 0) {
    const dump = await frame.evaluate(() => {
      const visible = el => {
        const r = el.getBoundingClientRect();
        return r.width > 0 && r.height > 0;
      };
      return {
        inputs: Array.from(document.querySelectorAll('input')).filter(i => i.type !== 'hidden').map(i => ({
          name: i.name, id: i.id, type: i.type, placeholder: i.placeholder,
          value: (i.value || '').slice(0, 60), visible: visible(i),
        })),
        textareas: Array.from(document.querySelectorAll('textarea')).map(t => ({
          name: t.name, id: t.id, placeholder: t.placeholder,
          valuePreview: (t.value || '').slice(0, 80), visible: visible(t),
        })),
        contenteditable: Array.from(document.querySelectorAll('[contenteditable="true"]')).map(e => ({
          tag: e.tagName, id: e.id, class: e.className,
          textPreview: (e.innerText || '').slice(0, 80),
        })),
      };
    });
    console.log(`${indent}  INPUTS:`, JSON.stringify(dump.inputs, null, 2).split('\n').map(l => indent + '    ' + l).join('\n'));
    console.log(`${indent}  TEXTAREAS:`, JSON.stringify(dump.textareas, null, 2).split('\n').map(l => indent + '    ' + l).join('\n'));
    console.log(`${indent}  CONTENTEDITABLE:`, JSON.stringify(dump.contenteditable, null, 2).split('\n').map(l => indent + '    ' + l).join('\n'));
  }

  for (const child of frame.childFrames()) {
    await findInFrames(child, depth + 1);
  }
}

await findInFrames(page.mainFrame());

console.log();
console.log('Browser left open for manual interaction. Close the window when done.');

// Keep the script alive until the browser disconnects.
await new Promise(() => {});
