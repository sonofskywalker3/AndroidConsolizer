"""Dump nexusmods.com cookies from Chrome in Playwright-friendly JSON.

Chrome 127+ uses app-bound encryption on Windows, which requires the cookie
extractor to run as administrator. If you get
'This operation requires admin', re-launch your terminal elevated:
  Right-click PowerShell -> Run as administrator
Then run again.
"""
import browser_cookie3
import json
import sys

cj = browser_cookie3.chrome(domain_name='nexusmods.com')
out = []
for c in cj:
    cookie = {
        'name': c.name,
        'value': c.value,
        'domain': c.domain,
        'path': c.path or '/',
        'secure': bool(c.secure),
        'sameSite': 'Lax',
    }
    if c.expires:
        cookie['expires'] = int(c.expires)
    out.append(cookie)
print(json.dumps(out, indent=2))
print(f'Dumped {len(out)} cookies (Chrome).', file=sys.stderr)
