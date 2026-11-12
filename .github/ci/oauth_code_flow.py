#!/usr/bin/env python3
"""Authorization Code + PKCE через сторінку логіну IdentityServer (без браузера). Друкує JSON відповіді /connect/token."""
import base64, hashlib, html, http.cookiejar, json, os, re, sys, urllib.parse, urllib.request

authority, client_id, secret, user, password, scope = sys.argv[1:7]
redirect = "http://localhost:5555/callback"
verifier = base64.urlsafe_b64encode(os.urandom(40)).rstrip(b"=").decode()
challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).rstrip(b"=").decode()

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k): return None
jar = http.cookiejar.CookieJar()
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar), NoRedirect)

def get(url):
    try: r = opener.open(url); return r.status, r.headers, r.read().decode()
    except urllib.error.HTTPError as e: return e.code, e.headers, e.read().decode()
def post(url, data):
    req = urllib.request.Request(url, urllib.parse.urlencode(data).encode(), method="POST")
    try: r = opener.open(req); return r.status, r.headers, r.read().decode()
    except urllib.error.HTTPError as e: return e.code, e.headers, e.read().decode()
def follow(url, limit=10):
    for _ in range(limit):
        status, headers, body = get(url)
        loc = headers.get("Location")
        if status in (301, 302, 303) and loc:
            url = urllib.parse.urljoin(url, loc)
            if url.startswith(redirect): return url
            continue
        return url, body
    raise SystemExit("too many redirects")

q = urllib.parse.urlencode({"client_id": client_id, "response_type": "code", "redirect_uri": redirect, "scope": scope,
    "code_challenge": challenge, "code_challenge_method": "S256", "state": "ci"})
login_url, page = follow(f"{authority}/connect/authorize?{q}")
token = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', page).group(1)
return_url = html.unescape(re.search(r'name="Input.ReturnUrl" value="([^"]*)"', page).group(1))
status, headers, body = post(login_url, {"Input.Username": user, "Input.Password": password, "Input.ReturnUrl": return_url,
    "Input.Button": "login", "__RequestVerificationToken": token})
assert status == 302, f"login failed: {status} {body[:300]}"
cb = follow(urllib.parse.urljoin(login_url, headers["Location"]))
code = urllib.parse.parse_qs(urllib.parse.urlparse(cb).query)["code"][0]
data = {"grant_type": "authorization_code", "code": code, "redirect_uri": redirect, "client_id": client_id, "code_verifier": verifier}
if secret != "-": data["client_secret"] = secret
status, headers, body = post(f"{authority}/connect/token", data)
assert status == 200, body
print(body)
