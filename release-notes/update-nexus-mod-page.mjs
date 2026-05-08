// Playwright script: update the Nexus Mods mod page (version + description)
// for AndroidConsolizer (mod 41869, game stardewvalley).
//
// Uses a persistent browser profile under release-notes/.nexus-playwright-profile/
// (gitignored) so cookies survive across runs. First run requires manual login;
// subsequent runs reuse the cookies.
//
// Flow:
//   1. Browser opens headed at the mod edit page.
//   2. If you're not logged in, log in (the script waits up to 5 minutes for you
//      to land back at the edit URL).
//   3. Script auto-discovers the version and description fields, fills them.
//   4. You review the page, click Save in Nexus's UI yourself.
//   5. Close the browser window when done — script exits.
//
// Usage:
//   node release-notes/update-nexus-mod-page.mjs
//
// Environment overrides:
//   NEXUS_DESCRIPTION_FILE — path to bbcode file (default release-notes/nexus-3.6.0-description.bbcode)
//   NEXUS_VERSION          — version string (default read from manifest.json)
//   NEXUS_PROFILE_DIR      — persistent context dir (default release-notes/.nexus-playwright-profile)

import { chromium } from 'playwright';
import { readFileSync, existsSync } from 'fs';
import { fileURLToPath } from 'url';
import { dirname, resolve, join } from 'path';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const PROJECT_ROOT = resolve(__dirname, '..');

const MOD_ID = 41869;
const GAME_DOMAIN = 'stardewvalley';
const EDIT_URL = `https://www.nexusmods.com/games/${GAME_DOMAIN}/mods/${MOD_ID}/edit`;

const PROFILE_DIR = process.env.NEXUS_PROFILE_DIR
  || resolve(__dirname, '.nexus-playwright-profile');

const DESC_PATH = process.env.NEXUS_DESCRIPTION_FILE
  || resolve(__dirname, 'nexus-3.6.0-description.bbcode');

const VERSION = process.env.NEXUS_VERSION
  || JSON.parse(readFileSync(join(PROJECT_ROOT, 'manifest.json'), 'utf8')).Version;

if (!existsSync(DESC_PATH)) {
  console.error(`Description file not found: ${DESC_PATH}`);
  process.exit(1);
}
const description = readFileSync(DESC_PATH, 'utf8');

console.log(`Profile dir : ${PROFILE_DIR}`);
console.log(`Description : ${DESC_PATH} (${description.length} chars)`);
console.log(`Version     : ${VERSION}`);
console.log(`Edit URL    : ${EDIT_URL}`);
console.log();

const context = await chromium.launchPersistentContext(PROFILE_DIR, {
  headless: false,
  viewport: { width: 1400, height: 900 },
  args: ['--start-maximized'],
});

// Exit cleanly when the user closes the browser window.
context.on('close', () => {
  console.log('Browser closed. Done.');
  process.exit(0);
});

const page = context.pages()[0] || await context.newPage();

console.log('Navigating to edit page...');
try {
  await page.goto(EDIT_URL, { waitUntil: 'domcontentloaded', timeout: 60_000 });
} catch (e) {
  console.warn(`Initial navigation failed: ${e.message}. Continuing — page may still load.`);
}

// If redirected to login, wait up to 5 minutes for the user to land back on the edit URL.
const initialUrl = page.url();
if (!initialUrl.includes('/edit')) {
  console.log(`Currently at: ${initialUrl}`);
  console.log('Looks like you need to log in. Please complete login in the browser window.');
  console.log('Waiting up to 5 minutes for you to reach the mod edit page...');
  try {
    await page.waitForURL(url => url.toString().includes(`/mods/${MOD_ID}/edit`), { timeout: 5 * 60_000 });
  } catch (e) {
    console.error('Did not reach edit page in time. Aborting.');
    await context.close();
    process.exit(1);
  }
}

console.log(`Now on: ${page.url()}`);
console.log('Looking for form fields...');

// Auto-discover the version field.
const versionSelectors = [
  'input[name="file-version"]',
  'input[name="fileversion"]',
  'input[name="version"]',
  'input#version',
  'input#mod-version',
  'input[name="mod[version]"]',
  'input[name="modversion"]',
];
let versionSel = null;
for (const sel of versionSelectors) {
  try {
    if (await page.locator(sel).first().count() > 0) { versionSel = sel; break; }
  } catch { /* ignore */ }
}

const descSelectors = [
  'textarea[name="description"]',
  'textarea[name="mod[description]"]',
  'textarea#description',
  'textarea#mod-description',
  'textarea#mod_description',
  'textarea[name="moddescription"]',
];
let descSel = null;
for (const sel of descSelectors) {
  try {
    if (await page.locator(sel).first().count() > 0) { descSel = sel; break; }
  } catch { /* ignore */ }
}

if (!versionSel || !descSel) {
  console.log();
  console.log('Could not auto-discover form fields.');
  console.log(`  versionSel: ${versionSel || 'NOT FOUND'}`);
  console.log(`  descSel   : ${descSel || 'NOT FOUND'}`);
  console.log();
  console.log('Dumping candidate selectors:');
  const candidates = await page.evaluate(() => {
    const r = { inputs: [], textareas: [] };
    document.querySelectorAll('input').forEach(el => {
      r.inputs.push({ name: el.name, id: el.id, type: el.type, placeholder: el.placeholder, valuePreview: (el.value || '').slice(0, 50) });
    });
    document.querySelectorAll('textarea').forEach(el => {
      r.textareas.push({ name: el.name, id: el.id, placeholder: el.placeholder, valuePreview: (el.value || '').slice(0, 80) });
    });
    return r;
  });
  console.log('Inputs (non-hidden):');
  console.log(JSON.stringify(candidates.inputs.filter(i => i.type !== 'hidden'), null, 2));
  console.log('Textareas:');
  console.log(JSON.stringify(candidates.textareas, null, 2));
  console.log();
  console.log('Browser left open so you can update fields manually. Close the window when done.');
} else {
  console.log(`Version field : ${versionSel}`);
  console.log(`Description   : ${descSel}`);

  try {
    await page.locator(versionSel).first().fill(VERSION);
    console.log(`✓ Version field filled with: ${VERSION}`);
  } catch (e) {
    console.error(`Version fill failed: ${e.message}`);
  }

  try {
    await page.locator(descSel).first().fill(description);
    console.log(`✓ Description field filled (${description.length} chars).`);
  } catch (e) {
    console.error(`Description fill failed: ${e.message}`);
  }

  console.log();
  console.log('Form filled. Review the page in the browser, then click Save (Nexus button).');
  console.log('Close the browser window when done — the script will exit on close.');
}

// Keep the script alive until the browser closes.
await new Promise(() => {});
