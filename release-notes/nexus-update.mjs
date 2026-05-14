// Drive a dedicated Chrome automation profile to update the Nexus mod-page
// description + version for mod 41869.
//
// Description editor = SCEditor (BBCode WYSIWYG). Setting its mirror <textarea>
// directly does NOT dirty Nexus's React form (Save stays disabled). The reliable
// path is SCEditor's own API: get the instance (set on the original textarea as
// `_sceditor`) and call `.val(bbcode)` — that fires SCEditor's valuechanged event
// which Nexus's form listens for, enabling Save. Fallback: source-mode toggle.
// Verify by reloading the edit page and re-reading the content.
//
// Dedicated profile rationale: Chrome v136+ blocks remote debugging on the
// default profile; a copy can't decrypt its cookies (app-bound encryption). A
// dedicated profile with its own login sidesteps both and stays logged in.
//
// Usage:  NEXUS_PW_PROFILE=<dedicated user-data dir>  node nexus-update.mjs

import { chromium } from 'playwright';
import { readFileSync } from 'fs';
import { fileURLToPath } from 'url';

const HERE = (p) => fileURLToPath(new URL(p, import.meta.url));
const USER_DATA = process.env.NEXUS_PW_PROFILE;
const CHROME = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const EDIT_URL = 'https://www.nexusmods.com/games/stardewvalley/mods/41869/edit';
const HOME_URL = 'https://www.nexusmods.com/';
const VERSION = '3.7.0';
const MARKER = "What's New in v3.7";
const LOGIN_WAIT_MS = 6 * 60 * 1000;

if (!USER_DATA) { console.error('[FATAL] NEXUS_PW_PROFILE not set'); process.exit(3); }
const description = readFileSync(HERE('./nexus-3.7.0-description.bbcode'), 'utf8');
console.log(`[init] desc=${description.length} chars  profile=${USER_DATA}`);

const ctx = await chromium.launchPersistentContext(USER_DATA, {
  executablePath: CHROME,
  headless: false,
  viewport: null,
  args: ['--disable-blink-features=AutomationControlled', '--no-first-run', '--no-default-browser-check', '--start-maximized'],
});
const page = ctx.pages()[0] || await ctx.newPage();
page.setDefaultTimeout(60000);

const isLoggedIn = async () => {
  const cookies = await ctx.cookies('https://www.nexusmods.com');
  return cookies.some(c => c.name === 'nexusmods_session_refresh' && c.value);
};

async function gotoEdit() {
  await page.goto(EDIT_URL, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.sceditor-container', { timeout: 60000 });
  await page.waitForTimeout(3000);
}

async function readState() {
  return page.evaluate((marker) => {
    const tas = [...document.querySelectorAll('textarea')].filter(t => /describe your mod/i.test(t.placeholder || ''));
    const ta = tas.sort((a, b) => (b.value || '').length - (a.value || '').length)[0];
    const v = ta ? (ta.value || '') : '';
    const ver = document.querySelector('#mod-version');
    return { descLen: v.length, hasMarker: v.includes(marker), hasBrTags: v.includes('<br'), version: ver ? ver.value : null };
  }, MARKER);
}

try {
  // ---- login ----
  console.log('[nav]', HOME_URL);
  await page.goto(HOME_URL, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(2500);
  if (!(await isLoggedIn())) {
    console.log('\n=== ACTION NEEDED: log into Nexus Mods in the Chrome window. ===\n');
    const deadline = Date.now() + LOGIN_WAIT_MS;
    let ok = false;
    while (Date.now() < deadline) {
      await page.waitForTimeout(5000);
      try { if (await isLoggedIn()) { ok = true; break; } } catch {}
      console.log(`[login] waiting... ${Math.round((deadline - Date.now()) / 1000)}s left`);
    }
    if (!ok) { console.log('[ERROR] login wait timed out.'); await ctx.close(); process.exit(2); }
  }
  console.log('[login] ok');

  // ---- open edit page ----
  console.log('[nav]', EDIT_URL);
  await gotoEdit();
  console.log('[before]', JSON.stringify(await readState()));

  // ---- set description via SCEditor instance API ----
  const setResult = await page.evaluate((bbcode) => {
    const log = [];
    const tas = [...document.querySelectorAll('textarea')].filter(t => /describe your mod/i.test(t.placeholder || ''));
    const ta = tas.sort((a, b) => (b.value || '').length - (a.value || '').length)[0];
    if (!ta) return { ok: false, log: ['no candidate textarea'] };
    log.push('original textarea curLen=' + (ta.value || '').length);

    let inst = ta._sceditor || null;
    if (inst) log.push('instance via ta._sceditor');
    if (!inst && window.sceditor && typeof window.sceditor.instance === 'function') {
      try { inst = window.sceditor.instance(ta); if (inst) log.push('instance via sceditor.instance()'); }
      catch (e) { log.push('sceditor.instance() threw: ' + e.message); }
    }
    if (!inst) {
      let el = ta;
      for (let i = 0; i < 10 && el; i++) { if (el._sceditor) { inst = el._sceditor; log.push('instance via ancestor._sceditor'); break; } el = el.parentElement; }
    }
    if (!inst || typeof inst.val !== 'function') {
      log.push('NO SCEditor instance with .val() — fallback needed');
      return { ok: false, needFallback: true, log };
    }
    inst.val(bbcode);
    if (typeof inst.updateOriginal === 'function') { inst.updateOriginal(); log.push('updateOriginal()'); }
    ta.dispatchEvent(new Event('input', { bubbles: true }));
    ta.dispatchEvent(new Event('change', { bubbles: true }));
    const got = inst.val();
    log.push(`after val(): instLen=${(got || '').length} taLen=${(ta.value || '').length}`);
    return { ok: (got || '').includes('v3.7') || (ta.value || '').includes('v3.7'), method: 'api', log };
  }, description);
  console.log('[setdesc]', JSON.stringify(setResult));

  if (!setResult.ok && setResult.needFallback) {
    console.log('[setdesc] API unavailable — source-mode fallback');
    const srcBtn = page.locator('a.sceditor-button-source, .sceditor-button-source').first();
    if (await srcBtn.count() === 0) { console.log('[ABORT] no source button'); await ctx.close(); process.exit(7); }
    await srcBtn.click(); await page.waitForTimeout(1000);
    const srcTas = page.locator('.sceditor-container textarea');
    for (let i = 0; i < await srcTas.count(); i++) {
      const t = srcTas.nth(i);
      if (await t.isVisible().catch(() => false)) {
        await t.click();
        await page.keyboard.press('Control+A');
        await page.keyboard.insertText(description);  // real input events SCEditor sees
        break;
      }
    }
    await page.waitForTimeout(500);
    await srcBtn.click(); await page.waitForTimeout(1200);
    console.log('[setdesc] source-mode fallback done');
  } else if (!setResult.ok) {
    console.log('[ABORT] description set failed and no fallback path. No save.');
    await page.screenshot({ path: HERE('./_nexus-error.png'), fullPage: true });
    await ctx.close(); process.exit(7);
  }

  // ---- dirty Nexus's React form ----
  // Programmatic SCEditor .val() doesn't fire the events Nexus's form listens for,
  // so the Save button stays disabled. Do a REAL, content-neutral keystroke in the
  // editor body (type a space, immediately backspace it) — a genuine keystroke
  // triggers SCEditor's valuechanged chain that Nexus's form is wired to.
  console.log('[dirty] real keystroke in SCEditor body to trigger form dirty-tracking');
  try {
    await page.frameLocator('.sceditor-container iframe').locator('body').click({ timeout: 8000 });
    console.log('[dirty] focused WYSIWYG iframe body');
  } catch (e) {
    console.log('[dirty] iframe click failed (' + (e.message || '').split('\n')[0] + ') — using inst.focus()');
    await page.evaluate(() => {
      const ta = [...document.querySelectorAll('textarea')].filter(t => /describe your mod/i.test(t.placeholder || ''))
        .sort((a, b) => (b.value || '').length - (a.value || '').length)[0];
      if (ta && ta._sceditor && ta._sceditor.focus) ta._sceditor.focus();
    });
  }
  await page.keyboard.type(' ');
  await page.keyboard.press('Backspace');
  await page.waitForTimeout(2500);

  // ---- version (already 3.7.0 from a prior run, but set anyway) ----
  const verEl = page.locator('#mod-version');
  if (await verEl.count()) { await verEl.fill(VERSION); console.log('[ver] ->', await verEl.inputValue()); }

  await page.waitForTimeout(2000); // let the React form react / enable Save
  await page.screenshot({ path: HERE('./_nexus-filled.png'), fullPage: true });

  // ---- save: target an ENABLED Save button; fast-fail (no 90s hang) ----
  const enabledSave = page.locator('button:not([disabled]):has-text("Save")').first();
  if (await enabledSave.count() === 0) {
    const states = await page.evaluate(() => [...document.querySelectorAll('button')]
      .filter(b => /save/i.test(b.innerText || ''))
      .map(b => ({ text: (b.innerText || '').trim().slice(0, 20), disabled: b.disabled, cls: (b.className || '').slice(0, 70) })));
    console.log('[ABORT] no ENABLED Save button — form not dirty. Save button states:', JSON.stringify(states));
    await ctx.close(); process.exit(9);
  }
  console.log('[save] clicking enabled Save');
  await enabledSave.click({ timeout: 15000 });
  await page.waitForTimeout(9000);
  await page.screenshot({ path: HERE('./_nexus-saved.png'), fullPage: true });

  // ---- verify by reload ----
  console.log('[verify] reloading to confirm persistence...');
  await gotoEdit();
  const after = await readState();
  console.log('[after]', JSON.stringify(after));
  await page.screenshot({ path: HERE('./_nexus-verify.png'), fullPage: true });

  if (after.hasMarker && after.version === VERSION) {
    console.log(`[SUCCESS] description persisted (len=${after.descLen}), version=${after.version}`);
    await ctx.close(); process.exit(0);
  }
  console.log(`[FAIL] post-reload: hasMarker=${after.hasMarker} version=${after.version} descLen=${after.descLen}`);
  await ctx.close(); process.exit(8);
} catch (err) {
  console.log('[EXCEPTION]', (err && err.stack) || err);
  try { await page.screenshot({ path: HERE('./_nexus-error.png'), fullPage: true }); } catch {}
  await ctx.close().catch(() => {});
  process.exit(1);
}
