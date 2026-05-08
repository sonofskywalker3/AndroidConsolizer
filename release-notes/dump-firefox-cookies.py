"""Dump nexusmods.com cookies from Firefox in Playwright-friendly JSON."""
import browser_cookie3
import json
import sys

cj = browser_cookie3.firefox(domain_name='nexusmods.com')
out = []
for c in cj:
    cookie = {
        'name': c.name,
        'value': c.value,
        'domain': c.domain,
        'path': c.path or '/',
        'secure': bool(c.secure),
        # Playwright wants sameSite as 'Strict'/'Lax'/'None'.
        'sameSite': 'Lax',
    }
    if c.expires:
        cookie['expires'] = int(c.expires)
    out.append(cookie)
print(json.dumps(out, indent=2))
print(f'\nDumped {len(out)} cookies to stdout.', file=sys.stderr)
