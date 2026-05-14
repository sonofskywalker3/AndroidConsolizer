# Release Tooling

Living documentation for AndroidConsolizer's Nexus Mods publishing pipeline. Captures what's automated, what's blocked, and the manual steps that remain.

This doc replaces TODO items #66 (Nexus mod-page automation) and #67 (cookie refresh) — those weren't feature work, they were release infrastructure.

---

## Pipeline state

### What's automated

`.github/workflows/publish-nexus.yml` runs on `release.published` and handles:

- ZIP file upload to Nexus
- File version field (matches the GitHub release tag)
- `file_category=main`
- Archiving the previous file

Uses the V2 GraphQL upload action with the user's Nexus API key.

### What's automated via browser script

`release-notes/nexus-update.mjs` (Playwright driving a dedicated logged-in Chrome profile) handles:

- **Mod description** (the long-form BBCode) — set via the SCEditor instance API plus a real keystroke to dirty Nexus's React form, then verified by reloading the page
- **Mod-level version field**

### What's still manual

- **Changelog entry** on the version-history page — `unex changelog` can't authenticate against current Nexus (see below). Paste it in the browser from `release-notes/<version>-nexus-changelog.txt`.

The Nexus V2 GraphQL API has no mutations for description/version/changelog and the V1 REST API is read-only for mod metadata — hence the browser-automation approach.

---

## Workflow per release

1. `gh release create v<X.Y.Z> "<zip>" ...` — triggers `.github/workflows/publish-nexus.yml`, which uploads the file to Nexus (version, category, archiving handled).
2. Write `release-notes/<X.Y.Z>-nexus-changelog.txt` and `release-notes/nexus-<X.Y.Z>-description.bbcode`; point `nexus-update.mjs`'s description path at the new bbcode file.
3. `NEXUS_PW_PROFILE=C:\Users\Jeff\.nexus-automation-profile node release-notes/nexus-update.mjs` — sets the description + mod-level version on the live page and verifies by reloading. First-ever run needs a one-time Nexus login in the popped-up window; later runs are unattended (the dedicated profile stays logged in).
4. Paste the changelog entry manually on the version-history page (see "What's still manual").

---

## Why automation is hard

### Nexus V2 GraphQL mutations (73 total)

Surveyed exhaustively. Only `updateChangelog` is mod-related, and it's per-version-changelog only. **No mutations exist for description, summary, or mod-level version.**

### Nexus V1 REST API

Read-only for mod metadata. Can't update.

### `unex changelog`

Would post the per-version changelog entry, but **doesn't work against current Nexus**: `unex` v3.0.8 only sends the `nexusmods_session` cookie, while Nexus now also requires `nexusmods_session_refresh` (+ `cf_clearance`), and Cloudflare TLS-fingerprints non-browser clients regardless. Refreshing the cookie doesn't fix it — the tool is structurally outdated. Post the changelog manually in the browser.

### Python `requests` with Firefox cookies

Cloudflare 403's the request because of browser fingerprint mismatch. Doesn't matter how legitimate the cookie is.

### Playwright Firefox + Firefox cookies

Tried. The Nexus session cookies were stale and the signed-out fallback page rendered. Also fragile: Cloudflare can challenge new Firefox sessions even with valid cookies.

### Python `browser_cookie3.chrome`

Chrome 127+ has app-bound encryption that blocks even admin-elevated DPAPI access to the cookie store. Can't extract the cookie programmatically.

### DevTools Console snippet (auto-detect form fields)

Works in theory, but Nexus is an SPA — the description form fields aren't present at first paint, they hydrate later. Auto-detection times out before they appear. Live-paste of pre-built BBCode by the user is faster.

### Implemented: Playwright + a dedicated Chrome profile

`nexus-update.mjs` drives Chrome via Playwright. Two walls, both on Chrome v136+:

- **Can't attach to the real/default profile** — Chrome refuses `--remote-debugging-*` on the default user-data-dir ("requires a non-default data directory").
- **Can't copy the profile** — a copied profile can't decrypt its cookies (app-bound encryption), so it lands logged-out.

Solution: a **dedicated** Chrome profile at `C:\Users\Jeff\.nexus-automation-profile` — a non-default path (so debugging works) with its *own* one-time Nexus login (so cookies decrypt). Playwright `launchPersistentContext` against it, using the real Chrome binary. It stays logged in, so subsequent releases are unattended. SCEditor specifics (the description editor) are in memory `nexus-publishing.md`.

---

## Cookie refresh procedure (when needed)

`~/.nexus_session_cookie` was set 2026-02-08 (per file mtime), confirmed expired by `unex check` on 2026-05-08.

**Effect of expiration:** `unex changelog` fails. File uploads via V2 GraphQL still work (uses API key, not cookie).

**Refresh procedure:**

1. In Chrome, log in to Nexus.
2. F12 → Application → Cookies → `https://www.nexusmods.com` → find `nexusmods_session`.
3. Copy the value (32-char hex string).
4. Write to `~/.nexus_session_cookie`.

`unex refresh -s <cookie>` would also work, but it requires a still-valid cookie to refresh — which we don't have, so the manual extraction step is the only path.

**When to refresh:** Before the next release if you want to also automate the changelog entry. Optional otherwise — file uploads work without it.

---

## Files

| File | Purpose |
|------|---------|
| `release-notes/nexus-update.mjs` | **Working** description + version updater — Playwright driving a dedicated logged-in Chrome profile |
| `release-notes/build-console-snippet.mjs` | Generates a clipboard-ready DevTools snippet for the description (manual fallback path) |
| `release-notes/update-nexus-mod-page.mjs` | Superseded by `nexus-update.mjs`; kept as reference (old DevTools-snippet attempt) |
| `release-notes/dump-firefox-cookies.py` | Reference: extract Firefox cookies (works but Cloudflare blocks the resulting request) |
| `release-notes/dump-chrome-cookies.py` | Reference: extract Chrome cookies (blocked by app-bound encryption since Chrome 127+) |
| `.github/workflows/publish-nexus.yml` | The actual working publish workflow (V2 GraphQL upload action) |
| `~/.nexus_api_key` | API key for V2 uploads |
| `~/.nexus_session_cookie` | Session cookie for `unex changelog` (currently expired) |

The Python and JS files in `release-notes/` that don't currently work are kept as **reference for future automation attempts** — each represents a path that was investigated and ruled out, and the notes inline help avoid re-investigating them.

---

## Mod identifiers

- **Mod ID:** 41869
- **Game:** stardewvalley
- **`unex` CLI:** installed and usable for CDN uploads; final POST to "add to mod" gets Cloudflare-403'd, so it's not a complete solution

## Status of Nexus API closed beta

Nexus's closed-beta API would fix automated publishing, but requires "verified" status (1000 unique downloads). Not yet eligible.
