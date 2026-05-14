// Build a single-paste DevTools Console snippet that fills the Nexus mod
// edit form with the current description and version. Embeds the BBCode
// as a JS string literal so the user only needs to paste once.
// Bump DESCRIPTION_FILE below when cutting a new release.
//
// Usage:
//   node build-console-snippet.mjs > nexus-3.7.0-console-snippet.js

import { readFileSync } from 'fs';
import { fileURLToPath } from 'url';
import { dirname, resolve, join } from 'path';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const PROJECT_ROOT = resolve(__dirname, '..');

const DESCRIPTION_FILE = 'nexus-3.7.0-description.bbcode';
const description = readFileSync(resolve(__dirname, DESCRIPTION_FILE), 'utf8');
const version = JSON.parse(readFileSync(join(PROJECT_ROOT, 'manifest.json'), 'utf8')).Version;

// JSON.stringify safely escapes for embedding in a JS string literal.
const descriptionLiteral = JSON.stringify(description);
const versionLiteral = JSON.stringify(version);

const snippet = `// Android Consolizer — Nexus mod page updater
// Paste this into Chrome DevTools Console while on the edit page:
//   https://www.nexusmods.com/games/stardewvalley/mods/41869/edit
// Then review the form and click Save in the Nexus UI.
(async () => {
  const VERSION = ${versionLiteral};
  const DESCRIPTION = ${descriptionLiteral};

  // React/Vue controlled inputs ignore plain .value writes — set via the
  // native setter and fire input/change so framework state updates.
  function setNativeValue(el, value) {
    const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
    const setter = Object.getOwnPropertyDescriptor(proto, 'value').set;
    setter.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  }

  function find(predicates, root = document) {
    for (const sel of predicates) {
      const el = root.querySelector(sel);
      if (el) return { el, sel };
    }
    return null;
  }

  // Try main document first, then walk same-origin iframes.
  function searchAll(predicates) {
    const docs = [document];
    document.querySelectorAll('iframe').forEach(f => {
      try { if (f.contentDocument) docs.push(f.contentDocument); } catch {}
    });
    for (const d of docs) {
      const hit = find(predicates, d);
      if (hit) return hit;
    }
    return null;
  }

  const versionSelectors = [
    'input[name="file-version"]',
    'input[name="fileversion"]',
    'input[name="version"]',
    'input#version',
    'input#mod-version',
    'input[name="mod[version]"]',
    'input[placeholder*="version" i]',
  ];
  const descSelectors = [
    'textarea[name="description"]',
    'textarea[name="mod[description]"]',
    'textarea#description',
    'textarea#mod-description',
    'textarea#mod_description',
    'textarea[placeholder*="description" i]',
  ];

  const v = searchAll(versionSelectors);
  const d = searchAll(descSelectors);

  if (!v) {
    console.warn('[Nexus updater] Version field not found. Inputs on page:');
    console.table(
      [...document.querySelectorAll('input')]
        .filter(i => i.type !== 'hidden')
        .map(i => ({ name: i.name, id: i.id, type: i.type, placeholder: i.placeholder, value: (i.value||'').slice(0,40) }))
    );
  } else {
    setNativeValue(v.el, VERSION);
    console.log(\`[Nexus updater] Version set via \${v.sel}\`);
  }

  if (!d) {
    console.warn('[Nexus updater] Description field not found. Textareas on page:');
    console.table(
      [...document.querySelectorAll('textarea')].map(t => ({
        name: t.name, id: t.id, placeholder: t.placeholder,
        valuePreview: (t.value||'').slice(0,80),
      }))
    );
  } else {
    setNativeValue(d.el, DESCRIPTION);
    console.log(\`[Nexus updater] Description set via \${d.sel} (\${DESCRIPTION.length} chars)\`);
  }

  if (v && d) {
    console.log('[Nexus updater] Done. Review the form, then click Save in the Nexus UI.');
  } else {
    console.warn('[Nexus updater] Some fields not auto-filled. See dumps above; you can fill them manually.');
  }
})();
`;

process.stdout.write(snippet);
