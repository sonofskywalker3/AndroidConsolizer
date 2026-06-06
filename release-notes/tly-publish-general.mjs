// TLY first-publish Phase A: set General fields on mod 47192 — version, short
// description, full description (BBCode via SCEditor) — then Save. Screenshots to
// TheLongestYear/test-output for verification. Files upload + Publish are Phase B.
// Usage: NEXUS_PW_PROFILE='C:\Users\Jeff\.nexus-automation-profile' node tly-publish-general.mjs
import { chromium } from 'playwright';
import { readFileSync } from 'fs';

const USER_DATA = process.env.NEXUS_PW_PROFILE;
const CHROME = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const TLY = 'C:\\Users\\Jeff\\Documents\\Projects\\Stardee Valoo\\TheLongestYear\\';
const SHOT = TLY + 'test-output\\';
const EDIT_URL = 'https://www.nexusmods.com/games/stardewvalley/mods/47192/edit/general';
const VERSION = '0.9.6';
const SHORT_DESC = 'Restore the Community Center within a single year — or the Junimos rewind the seasons and you begin again, a little stronger.';
const MARKER = "What's New in 0.9.6";
if (!USER_DATA) { console.error('[FATAL] NEXUS_PW_PROFILE not set'); process.exit(3); }
const bbcode = readFileSync(TLY + 'docs\\nexus-description.bbcode', 'utf8');
console.log('[init] bbcode=' + bbcode.length + ' chars');

const ctx = await chromium.launchPersistentContext(USER_DATA, {
  executablePath: CHROME, headless: false, viewport: null,
  args: ['--disable-blink-features=AutomationControlled', '--no-first-run', '--no-default-browser-check', '--start-maximized'],
});
const page = ctx.pages()[0] || await ctx.newPage();
page.setDefaultTimeout(60000);

try {
  await page.goto(EDIT_URL, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.sceditor-container', { timeout: 60000 });
  await page.waitForTimeout(3000);

  // ---- mod version ----
  const verEl = page.locator('#mod-version');
  if (await verEl.count()) { await verEl.fill(VERSION); console.log('[ver] ->', await verEl.inputValue()); }

  // ---- short description (plain textarea currently holding "placeholder") ----
  const shortRes = await page.evaluate((val) => {
    const tas = [...document.querySelectorAll('textarea')];
    // the short-desc textarea is the one NOT owned by SCEditor and short/placeholder-ish
    const cand = tas.find(t => !t._sceditor && /placeholder/i.test(t.value || '')) ||
                 tas.find(t => !t._sceditor && /describe what your mod/i.test(t.placeholder || ''));
    if (!cand) return { ok: false, msg: 'short-desc textarea not found' };
    const set = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value').set;
    set.call(cand, val);
    cand.dispatchEvent(new Event('input', { bubbles: true }));
    cand.dispatchEvent(new Event('change', { bubbles: true }));
    return { ok: true, len: cand.value.length };
  }, SHORT_DESC);
  console.log('[short]', JSON.stringify(shortRes));

  // ---- full description via SCEditor instance API ----
  const setRes = await page.evaluate((bb) => {
    const log = [];
    const ta = [...document.querySelectorAll('textarea')].find(t => t._sceditor);
    if (!ta) return { ok: false, needFallback: true, log: ['no _sceditor textarea'] };
    const inst = ta._sceditor;
    if (!inst || typeof inst.val !== 'function') return { ok: false, needFallback: true, log: ['no .val()'] };
    inst.val(bb);
    if (typeof inst.updateOriginal === 'function') inst.updateOriginal();
    ta.dispatchEvent(new Event('input', { bubbles: true }));
    ta.dispatchEvent(new Event('change', { bubbles: true }));
    const got = inst.val() || '';
    log.push('instLen=' + got.length + ' taLen=' + (ta.value || '').length);
    return { ok: got.includes('Longest Year'), method: 'api', log };
  }, bbcode);
  console.log('[fulldesc]', JSON.stringify(setRes));

  if (!setRes.ok && setRes.needFallback) {
    console.log('[fulldesc] API failed — source-mode fallback');
    const srcBtn = page.locator('a.sceditor-button-source, .sceditor-button-source').first();
    if (await srcBtn.count()) {
      await srcBtn.click(); await page.waitForTimeout(800);
      const t = page.locator('.sceditor-container textarea').first();
      await t.click(); await page.keyboard.press('Control+A'); await page.keyboard.insertText(bbcode);
      await page.waitForTimeout(400); await srcBtn.click(); await page.waitForTimeout(1000);
    }
  }

  // ---- dirty the React form with a real keystroke in the SCEditor body ----
  try {
    await page.frameLocator('.sceditor-container iframe').locator('body').click({ timeout: 8000 });
  } catch { await page.evaluate(() => { const ta=[...document.querySelectorAll('textarea')].find(t=>t._sceditor); ta && ta._sceditor && ta._sceditor.focus && ta._sceditor.focus(); }); }
  await page.keyboard.type(' '); await page.keyboard.press('Backspace');
  await page.waitForTimeout(2500);

  await page.screenshot({ path: SHOT + 'tly-general-filled.png', fullPage: true });

  // ---- Save ----
  const save = page.locator('button:not([disabled]):has-text("Save")').first();
  if (await save.count() === 0) {
    const states = await page.evaluate(() => [...document.querySelectorAll('button')].filter(b=>/save/i.test(b.innerText||'')).map(b=>({t:(b.innerText||'').trim().slice(0,16),d:b.disabled})));
    console.log('[ABORT] no enabled Save:', JSON.stringify(states));
    await page.screenshot({ path: SHOT + 'tly-general-nosave.png', fullPage: true });
    await ctx.close(); process.exit(9);
  }
  console.log('[save] clicking');
  await save.click({ timeout: 15000 });
  await page.waitForTimeout(8000);
  await page.screenshot({ path: SHOT + 'tly-general-saved.png', fullPage: true });

  // ---- verify ----
  await page.goto(EDIT_URL, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.sceditor-container', { timeout: 60000 });
  await page.waitForTimeout(2500);
  const after = await page.evaluate((marker) => {
    const ta = [...document.querySelectorAll('textarea')].find(t => t._sceditor);
    const v = ta ? (ta.value || '') : '';
    const ver = document.querySelector('#mod-version');
    const short = [...document.querySelectorAll('textarea')].find(t => !t._sceditor && (t.value||'').length>0 && (t.value||'').length<400);
    return { fullLen: v.length, hasMarker: v.includes(marker), version: ver ? ver.value : null, shortVal: short ? short.value.slice(0,60) : null };
  }, MARKER);
  console.log('[after]', JSON.stringify(after));
  await page.screenshot({ path: SHOT + 'tly-general-verify.png', fullPage: true });
  await ctx.close();
  process.exit(after.hasMarker && after.version === VERSION ? 0 : 8);
} catch (err) {
  console.log('[EXCEPTION]', (err && err.stack) || err);
  try { await page.screenshot({ path: SHOT + 'tly-general-error.png', fullPage: true }); } catch {}
  await ctx.close().catch(() => {});
  process.exit(1);
}
