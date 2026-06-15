// Headless-ish Playwright scraper for Android Consolizer (Nexus mod 41869).
// Pulls the Posts (comments) tab + the Bugs tab. Headful + dedicated Chrome
// profile (clears Cloudflare; headless trips the bot check). Window auto-closes.
//
// Usage:
//   NEXUS_PW_PROFILE='C:\Users\Jeff\.nexus-automation-profile' node scrape-ac-comments.mjs
//
// Output: JSON per tab + a digest under release-notes/forum-sweeps/.

import { chromium } from 'playwright';
import { writeFileSync, mkdirSync } from 'fs';

const CHROME = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const USER_DATA = process.env.NEXUS_PW_PROFILE;
const OUT_DIR = 'C:\\Users\\Jeff\\Documents\\Projects\\Stardee Valoo\\AndroidConsolizer\\release-notes\\forum-sweeps';
const STAMP = new Date().toISOString().slice(0, 16).replace(/[:T]/g, '-');
const MOD = { key: 'androidconsolizer', name: 'Android Consolizer', id: 41869 };

mkdirSync(OUT_DIR, { recursive: true });

const launchOpts = {
  executablePath: CHROME,
  headless: false,
  viewport: null,
  args: ['--disable-blink-features=AutomationControlled', '--no-first-run', '--no-default-browser-check', '--window-size=1280,1600'],
};

let ctx;
if (USER_DATA) {
  ctx = await chromium.launchPersistentContext(USER_DATA, launchOpts);
} else {
  const browser = await chromium.launch(launchOpts);
  ctx = await browser.newContext({ viewport: launchOpts.viewport });
}
const page = ctx.pages()[0] || await ctx.newPage();
page.setDefaultTimeout(60000);

async function clearCloudflare(label) {
  for (let i = 0; i < 12; i++) {
    await page.waitForTimeout(2500);
    const blocked = await page.evaluate(() =>
      /security verification|verify you are not a bot|Just a moment/i.test(document.body.innerText));
    if (!blocked) return;
    console.log(`[${label}] waiting on Cloudflare… (${i + 1})`);
  }
}

const digest = [];

for (const tab of ['posts', 'bugs']) {
  const url = `https://www.nexusmods.com/stardewvalley/mods/${MOD.id}?tab=${tab}`;
  try {
    await page.goto(url, { waitUntil: 'domcontentloaded' });
    await clearCloudflare(`${MOD.key}/${tab}`);
    await page.waitForTimeout(2500);

    const items = await page.evaluate((t) => {
      const out = [];
      if (t === 'posts') {
        document.querySelectorAll('.comment, li.comment, .comment-block').forEach(n => {
          let author = (n.querySelector('.comment-name')?.innerText || '').trim();
          if (!author) author = (n.querySelector('a[href*="/profile/"]')?.innerText || '').trim();
          const body = (n.querySelector('.comment-content, .comment-text, .comment-body')?.innerText || '').trim();
          const date = (n.querySelector('.date, time, .comment-date')?.innerText || '').trim();
          if (body) out.push({ author, date, body });
        });
      } else {
        const sels = ['tr.bug', 'li.bug', '.bug', '[data-bug-id]', '[class*="bug-row"]', '[class*="bug-item"]'];
        for (const sel of sels) {
          const els = document.querySelectorAll(sel);
          if (els.length) {
            els.forEach(el => out.push({ body: el.innerText.trim().replace(/\s+\n/g, '\n').slice(0, 1500) }));
            break;
          }
        }
      }
      return out;
    }, tab);

    let fallback = '';
    if (items.length === 0) {
      fallback = await page.evaluate(() =>
        (document.querySelector('#mod_tab_content, .mod_tab_content, #comments, .comments, main, #wrap_main') || document.body)
          .innerText.trim().slice(0, 8000));
    }

    writeFileSync(`${OUT_DIR}\\${STAMP}_${MOD.key}_${tab}.json`,
      JSON.stringify({ mod: MOD.name, id: MOD.id, tab, url, count: items.length, items, fallback }, null, 2));
    console.log(`[${MOD.key}/${tab}] parsed=${items.length}${fallback ? ' (+fallback text)' : ''}`);
    digest.push({ source: `Nexus ${MOD.name} ${tab}`, count: items.length, items, fallback });
  } catch (e) {
    console.log(`[${MOD.key}/${tab}] FAIL: ${e.message}`);
    digest.push({ source: `Nexus ${MOD.name} ${tab}`, error: e.message });
  }
}

await ctx.close();

writeFileSync(`${OUT_DIR}\\${STAMP}_AC_DIGEST.json`, JSON.stringify(digest, null, 2));
console.log(`\n[done] written to ${OUT_DIR}\\${STAMP}_*`);
