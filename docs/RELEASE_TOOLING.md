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

### What's NOT automated

- **Mod description** (the long-form BBCode in the page header)
- **Mod-level version field** (the version shown on the page header — separate from the per-file version)
- **Changelog entry** on the version-history page

These all require browser-side work because the Nexus V2 GraphQL API has no mutations for them, and the V1 REST API is read-only for mod metadata.

---

## Manual workflow per release

1. Run `release-notes/build-console-snippet.mjs` to generate the BBCode description for the new version.
2. PowerShell `Set-Clipboard` puts the BBCode on the clipboard automatically.
3. Open the mod-page edit URL in your already-logged-in Chrome.
4. Paste into the description field, save.
5. Manually edit the mod-level version field on the same page.
6. Manually post the changelog entry on the version-history page.

Total time: ~30 seconds per release.

---

## Why automation is hard

### Nexus V2 GraphQL mutations (73 total)

Surveyed exhaustively. Only `updateChangelog` is mod-related, and it's per-version-changelog only. **No mutations exist for description, summary, or mod-level version.**

### Nexus V1 REST API

Read-only for mod metadata. Can't update.

### `unex changelog`

Would post the per-version changelog entry on Nexus's version-history page. Requires a valid session cookie at `~/.nexus_session_cookie`. **Currently expired** (see "Cookie refresh" below).

### Python `requests` with Firefox cookies

Cloudflare 403's the request because of browser fingerprint mismatch. Doesn't matter how legitimate the cookie is.

### Playwright Firefox + Firefox cookies

Tried. The Nexus session cookies were stale and the signed-out fallback page rendered. Also fragile: Cloudflare can challenge new Firefox sessions even with valid cookies.

### Python `browser_cookie3.chrome`

Chrome 127+ has app-bound encryption that blocks even admin-elevated DPAPI access to the cookie store. Can't extract the cookie programmatically.

### DevTools Console snippet (auto-detect form fields)

Works in theory, but Nexus is an SPA — the description form fields aren't present at first paint, they hydrate later. Auto-detection times out before they appear. Live-paste of pre-built BBCode by the user is faster.

### Best deferred path: Playwright + Chrome remote debugging

Connect Playwright to user's already-running Chrome via remote-debugging port. Preserves auth state and Cloudflare clearance end-to-end.

Requires user to launch Chrome with:
```
--remote-debugging-port=9222 --user-data-dir=<their profile>
```

Catch: Chrome blocks debugging on the default profile for security. Needs a profile copy or `--remote-debugging-pipe`. Not implemented yet.

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
| `release-notes/build-console-snippet.mjs` | Generates clipboard-ready BBCode for the description |
| `release-notes/update-nexus-mod-page.mjs` | Reference automation attempt (DevTools snippet path; doesn't currently work because of SPA hydration) |
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
