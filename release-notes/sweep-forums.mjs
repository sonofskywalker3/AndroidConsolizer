// Forum sweep across ALL published mods — Nexus Posts (comments) + Bugs tabs, plus Reddit.
// Headful throwaway Chrome (clears Cloudflare; headless trips the bot check). No login
// needed — everything swept here is public. Window auto-closes at the end.
//
// Reddit is NOT reliably readable from here either: as of 2026-08-26 the r/StardewValley thread
// serves a "prove your humanity" bot check to this throwaway profile, so that leg reports BLOCKED
// and you read it in the signed-in browser. Do not try to defeat the check.
//
// PRIVATE Nexus bug reports are NOT visible to this logged-out sweep: check the bugs tab
// via Claude-in-Chrome on Jeff's signed-in regular browser instead (see user memory
// nexus-use-regular-chrome.md). NEXUS_PW_PROFILE is now ignored on purpose — the
// automation profile's sessions kept expiring and forcing pointless re-logins.
//
// Usage:
//   node sweep-forums.mjs
//
// Output: one JSON per mod-tab + a combined digest under <repo>/release-notes/forum-sweeps/.

import { chromium } from 'playwright';
import { writeFileSync, mkdirSync } from 'fs';

const CHROME = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const USER_DATA = null; // automation profile retired for sweeps — public pages only; logged-in reads go through Claude-in-Chrome
const OUT_DIR = 'C:\\Users\\Jeff\\Documents\\Projects\\Stardee Valoo\\AndroidConsolizer\\release-notes\\forum-sweeps';
const STAMP = new Date().toISOString().slice(0, 16).replace(/[:T]/g, '-');

const MODS = [
  { key: 'androidconsolizer', name: 'Android Consolizer', id: 41869 },
  { key: 'cartcatalog',       name: 'Cart Catalog',       id: 47146 },
  { key: 'naptime',           name: 'Nap Time',           id: 42616 },
  { key: 'thelongestyear',    name: 'The Longest Year',   id: 47192 },
];

const REDDIT_THREADS = [
  { key: 'tly-beta',      sub: 'StardewValley',     id: '1txuhfb' },
  { key: 'tly-mods',      sub: 'StardewValleyMods', id: '1txu610' },
  { key: 'tly-smapi',     sub: 'SMAPI',             id: '1txtkb4' },
];

mkdirSync(OUT_DIR, { recursive: true });

const launchOpts = {
  executablePath: CHROME,
  headless: false,
  viewport: null,
  args: ['--disable-blink-features=AutomationControlled', '--no-first-run', '--no-default-browser-check', '--window-size=1280,1600'],
};

let ctx;
let browser = null;   // set on the non-persistent path so the run can actually exit
if (USER_DATA) {
  ctx = await chromium.launchPersistentContext(USER_DATA, launchOpts);
} else {
  browser = await chromium.launch(launchOpts);
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

// ---------- Nexus: Posts + Bugs per mod ----------
for (const mod of MODS) {
  for (const tab of ['posts', 'bugs']) {
    const url = `https://www.nexusmods.com/stardewvalley/mods/${mod.id}?tab=${tab}`;
    try {
      await page.goto(url, { waitUntil: 'domcontentloaded' });
      await clearCloudflare(`${mod.key}/${tab}`);
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
          // Bugs tab: rows of issues.
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

      // Fallback: if nothing structured parsed, capture the tab's visible text so we don't miss anything.
      let fallback = '';
      if (items.length === 0) {
        fallback = await page.evaluate(() =>
          (document.querySelector('#mod_tab_content, .mod_tab_content, #comments, .comments, main, #wrap_main') || document.body)
            .innerText.trim().slice(0, 6000));
      }

      writeFileSync(`${OUT_DIR}\\${STAMP}_${mod.key}_${tab}.json`,
        JSON.stringify({ mod: mod.name, id: mod.id, tab, url, count: items.length, items, fallback }, null, 2));
      // The bugs tab renders nothing for a logged-out visitor, and private reports are invisible to
      // one no matter what, so a parsed count of 0 here means "could not read", NOT "no open bugs".
      // Say that in the digest instead of printing a 0 that reads like a clean bug tracker.
      const unreadableBugs = tab === 'bugs' && items.length === 0;
      console.log(`[${mod.key}/${tab}] parsed=${items.length}${fallback ? ' (+fallback text)' : ''}` +
        (unreadableBugs ? '  <- logged out: read the bugs tab in the signed-in browser' : ''));
      digest.push({
        source: `Nexus ${mod.name} ${tab}`,
        count: unreadableBugs ? null : items.length,
        ...(unreadableBugs ? { note: 'NOT READ - logged-out sweep cannot see the bugs tab; check it in the signed-in browser' } : {}),
        items, fallback,
      });
    } catch (e) {
      console.log(`[${mod.key}/${tab}] FAIL: ${e.message}`);
      digest.push({ source: `Nexus ${mod.name} ${tab}`, error: e.message });
    }
  }
}

// ---------- Reddit threads ----------
// Scraped through reddit's public JSON endpoint, not the old.reddit HTML: the HTML scrape
// silently returned an empty comment list on 2026-08-26 (bot/consent interstitial served to this
// throwaway profile) and the digest reported "0 comments", which reads exactly like a dead thread.
// Anything that does not parse into a real listing is now a hard FAIL in the digest, never a zero.
// Caveat: a logged-out read comes back a comment or two short of what the signed-in browser shows
// (2026-08-26: 32 vs 33 on tly-mods, 0 vs 1 on tly-smapi), so treat these counts as a floor.
for (const thread of REDDIT_THREADS) {
  await page.waitForTimeout(3000);   // stagger: reddit 403s a burst of .json hits
  const permalink = `https://www.reddit.com/r/${thread.sub}/comments/${thread.id}/`;
  try {
    let data = null;
    let lastErr = '';
    let blocked = false;
    for (let attempt = 1; attempt <= 3 && !data; attempt++) {
      // Re-navigate every attempt: a 403 response navigates the page, and the next evaluate on the
      // old context dies with "Execution context was destroyed".
      await page.goto(permalink, { waitUntil: 'domcontentloaded' });
      await clearCloudflare(`reddit/${thread.key}`);
      const humanity = await page.evaluate(() =>
        /prove your humanity|verify you are human/i.test(document.title + ' ' + document.body.innerText.slice(0, 400)));
      if (humanity) {
        blocked = true;
        lastErr = 'reddit served a bot check ("prove your humanity") to this throwaway profile';
        console.log(`[reddit/${thread.key}] attempt ${attempt}: bot check`);
        await page.waitForTimeout(15000);
        continue;
      }
      const result = await page.evaluate(async ([sub, id]) => {
        try {
          const res = await fetch(`/r/${sub}/comments/${id}.json?limit=500&raw_json=1`);
          const text = await res.text();
          if (!text.trim().startsWith('['))
            return { error: `status ${res.status}, body starts "${text.trim().slice(0, 40)}" (not JSON)` };
          const json = JSON.parse(text);
          const post = json[0]?.data?.children?.[0]?.data;
          if (!post) return { error: 'no post node in listing' };
          const out = [];
          const walk = (node) => {
            for (const c of node?.data?.children || []) {
              if (c.kind !== 't1') continue;
              out.push({
                author: c.data.author,
                created: new Date(c.data.created_utc * 1000).toISOString(),
                score: c.data.score,
                permalink: c.data.permalink,
                body: c.data.body || '',
              });
              walk(c.data.replies);
            }
          };
          walk(json[1]);
          return { post: { title: post.title, author: post.author, ups: post.ups }, out };
        } catch (e) {
          return { error: String(e) };
        }
      }, [thread.sub, thread.id]);

      if (result.error) {
        lastErr = result.error;
        console.log(`[reddit/${thread.key}] attempt ${attempt} failed: ${result.error}`);
        await page.waitForTimeout(15000);   // 403 here is rate limiting, not a dead thread
        continue;
      }
      data = result;
    }

    // A thread with a real title and zero comments is believable; no title at all is a failed scrape.
    if (!data && blocked)
      throw new Error(`BLOCKED by reddit's bot check - read this thread in the signed-in browser instead (${lastErr})`);
    if (!data) throw new Error(`JSON fetch failed after 3 attempts: ${lastErr}`);
    if (!data.post.title) throw new Error('listing parsed but the post has no title (blocked page?)');

    const newest = data.out.length
      ? data.out.map(c => c.created).sort().at(-1)
      : '(no comments)';
    writeFileSync(`${OUT_DIR}\${STAMP}_reddit_${thread.key}.json`,
      JSON.stringify({ url: permalink, post: data.post, comments: data.out }, null, 2));
    console.log(`[reddit/${thread.key}] post="${data.post.title}" comments=${data.out.length} newest=${newest}`);
    digest.push({
      source: `Reddit ${thread.key} ("${data.post.title}")`,
      count: data.out.length, newest, items: data.out,
    });
  } catch (e) {
    // Loud on purpose: a FAIL row must never be mistaken for a quiet thread.
    console.log(`[reddit/${thread.key}] FAIL: ${e.message}`);
    digest.push({ source: `Reddit ${thread.key}`, count: null, error: `SCRAPE FAILED - ${e.message}` });
  }
}

await ctx.close();
// ctx.close() only closes the context on the non-persistent path; without this the chrome
// process (and node with it) stays alive and the run has to be killed from outside.
if (browser) await browser.close();

writeFileSync(`${OUT_DIR}\\${STAMP}_DIGEST.json`, JSON.stringify(digest, null, 2));
console.log(`\n[done] sweep written to ${OUT_DIR}\\${STAMP}_*  (digest: ${STAMP}_DIGEST.json)`);
