#!/usr/bin/env bash
# Lab 09: Auth Service (Identity + JWT), Duende IdentityServer (Client Credentials, Authorization Code + PKCE),
# Keycloak + Pulumi (realm, resource servers, roles, users), permission-based authorization by matrix.
set -euo pipefail
source "$CI_DIR/common.sh"
GW=http://localhost:5000
IS=http://localhost:5010
KC=http://localhost:8080
export ConnectionStrings__IdentityDb="Server=localhost,1433;Database=IdentityDb;User Id=sa;Password=$SA_PASSWORD;TrustServerCertificate=True;Encrypt=False"
export ConnectionStrings__IdentityServerDb="Server=localhost,1433;Database=IdentityServerDb;User Id=sa;Password=$SA_PASSWORD;TrustServerCertificate=True;Encrypt=False"
ID=(IdentityServer__Authority=$IS services__keycloak__http__0=$KC)
SD=(services__loans-api__http__0=http://localhost:5101 services__books-api__http__0=http://localhost:5102
    services__feedback-api__http__0=http://localhost:5103 services__aggregator__http__0=http://localhost:5090
    services__loans-api__grpc__0=http://localhost:5111 services__books-api__grpc__0=http://localhost:5112
    services__feedback-api__grpc__0=http://localhost:5113 services__auth-api__http__0=http://localhost:5104
    services__payments-api__http__0=http://localhost:5105)
jwt() { python3 -c "import base64,json,sys;p=sys.argv[1].split('.')[1];p+='='*(-len(p)%4);print(json.dumps(json.loads(base64.urlsafe_b64decode(p)),ensure_ascii=False))" "$1"; }
field() { python3 -c "import json,sys;print(json.load(open('/tmp/resp.json'))$1)"; }
code() { curl -s -o /tmp/resp.json -w "%{http_code}" "$@"; }

echo ">> Keycloak container (quay.io/keycloak/keycloak 26, start-dev)"
docker run -d --name keycloak -p 8080:8080 -e KC_BOOTSTRAP_ADMIN_USERNAME=admin -e KC_BOOTSTRAP_ADMIN_PASSWORD=Admin_Pass123! \
  quay.io/keycloak/keycloak:26.0 start-dev >/dev/null

start_service identityserver Platform.IdentityServer 5010
start_service auth Services/Auth/Auth.Api 5104 "${ID[@]}"
start_service loans Services/Loans/Loans.Api 5101 "${ID[@]}"
start_service books Services/Books/Books.Api 5102 "${ID[@]}"
start_service feedback Services/Feedback/Feedback.WebApi 5103 "${ID[@]}"
start_service payments Services/Payments/Payments.Api 5105 "${ID[@]}"
start_service aggregator Platform.Aggregator 5090 "${ID[@]}" "${SD[@]}"
start_service gateway Platform.ApiGateway 5000 "${ID[@]}" "${SD[@]}"

echo; echo "================ Part 1: ASP.NET Identity + JWT (Auth Service) ================"
U="ciuser$RANDOM"; E="$U@library.local"
expect 201 -X POST $GW/auth/register -H 'Content-Type: application/json' -d "{\"userName\":\"$U\",\"email\":\"$E\",\"password\":\"Passw0rd_1\"}"
UID_=$(field "['id']")
expect 403 -X POST $GW/auth/login -H 'Content-Type: application/json' -d "{\"userNameOrEmail\":\"$U\",\"password\":\"Passw0rd_1\"}"
echo "   login before email confirmation -> 403"
curl -s http://localhost:5104/auth/dev/mailbox/$E > /tmp/mail.json
VT=$(python3 -c "import json,re;m=json.load(open('/tmp/mail.json'));print(re.search(r'token: (\S+)',m[0]['body']).group(1))")
python3 -c "import json;print(json.dumps({'userId':'$UID_','token':'$VT'}))" > /tmp/v.json
expect 204 -X POST $GW/auth/verify-email -H 'Content-Type: application/json' -d @/tmp/v.json
expect 400 -X POST $GW/auth/verify-email -H 'Content-Type: application/json' -d @/tmp/v.json
echo "   verification token is single-use"
rm -f /tmp/jar
expect 200 -c /tmp/jar -X POST $GW/auth/login -H 'Content-Type: application/json' -d "{\"userNameOrEmail\":\"$U\",\"password\":\"Passw0rd_1\"}"
AT=$(field "['accessToken']"); RT1=$(field "['refreshToken']")
jwt "$AT" | python3 -c "import json,sys;c=json.load(sys.stdin);print('   JWT claims:',{k:c[k] for k in ('sub','unique_name','role','iss','aud')},'lifetime',c['exp']-c['iat'],'s');assert c['exp']-c['iat']==900 and c['role'] in ('User',['User'])"
grep -i "refresh_token" /tmp/jar | grep -q "#HttpOnly" && echo "   refresh token is in an HttpOnly cookie (path /auth)"

expect 401 $GW/api/books
echo "   no token through the gateway -> 401"
expect 200 -H "Authorization: Bearer $AT" "$GW/api/books?page=1&pageSize=2"
expect 403 -H "Authorization: Bearer $AT" $GW/users
expect 403 -H "Authorization: Bearer $AT" $GW/api/payments
expect 403 -H "Authorization: Bearer $AT" -X DELETE $GW/api/loans/999999
expect 200 -H "Authorization: Bearer $AT" $GW/users/$UID_
expect 200 -H "Authorization: Bearer $AT" $GW/users/$UID_/refresh-tokens

echo ">> refresh rotation + reuse detection"
expect 200 -b /tmp/jar -c /tmp/jar -X POST $GW/auth/refresh-token
RT2=$(field "['refreshToken']"); test "$RT1" != "$RT2" && echo "   new refresh token issued"
expect 401 -X POST $GW/auth/refresh-token -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$RT1\"}"
expect 401 -X POST $GW/auth/refresh-token -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$RT2\"}"
echo "   reuse of a rotated token revoked the whole family"

echo ">> admin: users and roles"
expect 200 -X POST $GW/auth/login -H 'Content-Type: application/json' -d '{"userNameOrEmail":"admin","password":"Admin_Pass123!"}'
ADM=$(field "['accessToken']")
expect 200 -H "Authorization: Bearer $ADM" $GW/users
expect 204 -H "Authorization: Bearer $ADM" -X POST $GW/users/$UID_/roles -H 'Content-Type: application/json' -d '{"role":"Manager"}'
expect 200 -H "Authorization: Bearer $ADM" $GW/users/$UID_/roles; echo "   roles: $(cat /tmp/resp.json)"
expect 204 -H "Authorization: Bearer $ADM" -X DELETE $GW/users/$UID_/roles/Manager
expect 200 -H "Authorization: Bearer $ADM" $GW/api/payments
expect 404 -H "Authorization: Bearer $ADM" -X DELETE $GW/api/loans/999999

echo ">> forgot / reset password (single-use token, refresh tokens revoked) and change password"
expect 200 -c /tmp/jar -X POST $GW/auth/login -H 'Content-Type: application/json' -d "{\"userNameOrEmail\":\"$E\",\"password\":\"Passw0rd_1\"}"
AT=$(field "['accessToken']"); RT3=$(field "['refreshToken']")
expect 202 -X POST $GW/auth/forgot-password -H 'Content-Type: application/json' -d "{\"email\":\"$E\"}"
curl -s http://localhost:5104/auth/dev/mailbox/$E > /tmp/mail.json
python3 -c "import json,re;m=json.load(open('/tmp/mail.json'));t=re.search(r'token: (\S+)',m[0]['body']).group(1);print(json.dumps({'email':'$E','token':t,'newPassword':'NewPassw0rd_2'}))" > /tmp/r.json
expect 204 -X POST $GW/auth/reset-password -H 'Content-Type: application/json' -d @/tmp/r.json
expect 400 -X POST $GW/auth/reset-password -H 'Content-Type: application/json' -d @/tmp/r.json
expect 401 -X POST $GW/auth/refresh-token -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$RT3\"}"
expect 200 -X POST $GW/auth/login -H 'Content-Type: application/json' -d "{\"userNameOrEmail\":\"$U\",\"password\":\"NewPassw0rd_2\"}"
AT=$(field "['accessToken']"); RT4=$(field "['refreshToken']")
expect 204 -H "Authorization: Bearer $AT" -X POST $GW/auth/change-password -H 'Content-Type: application/json' -d '{"currentPassword":"NewPassw0rd_2","newPassword":"Changed_Passw0rd3"}'
expect 204 -H "Authorization: Bearer $AT" -X POST $GW/auth/revoke-token -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$RT4\"}"
expect 204 -H "Authorization: Bearer $AT" -X POST $GW/auth/logout
for i in 1 2 3 4 5; do code -X POST $GW/auth/login -H 'Content-Type: application/json' -d "{\"userNameOrEmail\":\"$U\",\"password\":\"wrong\"}" >/dev/null; done
expect 403 -X POST $GW/auth/login -H 'Content-Type: application/json' -d "{\"userNameOrEmail\":\"$U\",\"password\":\"Changed_Passw0rd3\"}"
echo "   locked out after 5 failed attempts"

echo; echo "================ Part 2: Duende IdentityServer ================"
curl -s $IS/.well-known/openid-configuration | python3 -c "import json,sys;d=json.load(sys.stdin);print('   issuer',d['issuer'],'| grants',d['grant_types_supported']);assert d['issuer']=='https://identity.library.local'"
curl -s $IS/.well-known/openid-configuration | python3 -c "import json,sys;d=json.load(sys.stdin);print('   jwks_uri',d['jwks_uri'])"
expect 200 -X POST $IS/connect/token -d grant_type=client_credentials -d client_id=aggregator_service -d client_secret=aggregator_secret -d "scope=loans:read books:read feedback:read"
CC=$(field "['access_token']")
curl -s $IS/.well-known/openid-configuration/jwks | python3 -c "import json,sys;d=json.load(sys.stdin);print('   jwks keys:',[{k:v for k,v in x.items() if k in ('kty','use','kid','alg')} for x in d['keys']])"
jwt_header() { python3 -c "import base64,json,sys;p=sys.argv[1].split('.')[0];p+='='*(-len(p)%4);print(json.loads(base64.urlsafe_b64decode(p)))" "$1"; }
echo "   token header: $(jwt_header "$CC")"
jwt "$CC" | python3 -c "import json,sys;c=json.load(sys.stdin);print('   client credentials token:',{k:c.get(k) for k in ('iss','aud','client_id','scope','sub')});assert 'sub' not in c"
expect 200 -H "Authorization: Bearer $CC" "http://localhost:5101/api/loans?page=1&pageSize=1"
expect 403 -H "Authorization: Bearer $CC" -X DELETE http://localhost:5101/api/loans/999999
expect 401 -H "Authorization: Bearer $CC" http://localhost:5105/api/payments
echo "   token without payments-api audience is rejected by payments-api (aud check) -> 401"
expect 400 -X POST $IS/connect/token -d grant_type=client_credentials -d client_id=aggregator_service -d client_secret=wrong
echo ">> Aggregator -> services via gRPC with its own Client Credentials token"
expect 200 -H "Authorization: Bearer $ADM" "$GW/api/aggregator/loans/1/full?cache=false"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   aggregated: books',len(d['books']),'warnings',d['warnings']);assert d['warnings']==[]"
grep -q '"client_id":"aggregator_service"\|aggregator_service' /tmp/svc-identityserver.log && echo "   aggregator obtained a token from IdentityServer"

echo ">> Authorization Code + PKCE (Postman client) as mike (manager)"
python3 $CI_DIR/oauth_code_flow.py $IS postman postman_secret mike Pass_123456 "openid profile roles books:read books:update books:delete loans:read" > /tmp/code.json
MT=$(python3 -c "import json;print(json.load(open('/tmp/code.json'))['access_token'])")
jwt "$MT" | python3 -c "import json,sys;c=json.load(sys.stdin);print('   code-flow token:',{k:c.get(k) for k in ('sub','role','scope','aud')})"
expect 200 -H "Authorization: Bearer $MT" "$GW/api/books/3"
P=$(field "['price']")
expect 200 -H "Authorization: Bearer $MT" -X PUT $GW/api/books/3/price -H 'Content-Type: application/json' -d "{\"price\":$P}"
expect 403 -H "Authorization: Bearer $MT" -X DELETE $GW/api/books/999999
echo ">> public client without PKCE is rejected"
LOC=$(curl -s -o /dev/null -w "%{redirect_url}" "$IS/connect/authorize?client_id=swagger&response_type=code&redirect_uri=http%3A%2F%2Flocalhost%3A5000%2Fswagger%2Foauth2-redirect.html&scope=openid")
echo "   redirect: $(echo "$LOC" | cut -c1-120)"
echo "$LOC" | grep -qi "error" && echo "   authorize request without code_challenge -> IdentityServer error page (PKCE required)"

echo; echo "================ Part 3: Keycloak + Pulumi ================"
for i in $(seq 1 60); do curl -sf $KC/realms/master >/dev/null && break; sleep 3; done
curl -s https://get.pulumi.com | sh -s -- --silent >/dev/null; export PATH=$HOME/.pulumi/bin:$PATH
pulumi version
export PULUMI_CONFIG_PASSPHRASE="ci" PULUMI_SKIP_UPDATE_CHECK=true
pushd infra/keycloak >/dev/null
pulumi login --local >/dev/null
pulumi stack select dev --create >/dev/null 2>&1 || pulumi stack init dev
pulumi config set library:temporaryPasswords false      # у CI паролі не тимчасові (password grant)
pulumi preview --non-interactive --diff 2>&1 | grep -E "^\s+\+ |Resources:|create" | head -20
pulumi up --yes --non-interactive --skip-preview 2>&1 | tail -8
pulumi stack output --json | python3 -c "import json,sys;d=json.load(sys.stdin);print('   outputs:',{k:d[k] for k in ('RealmUrl','AdminConsoleUrl','ClientIds')})"
pulumi preview --non-interactive 2>&1 | grep -E "unchanged|Resources" | tail -2
popd >/dev/null

kc_token() { curl -s -X POST $KC/realms/library/protocol/openid-connect/token -d grant_type=password -d client_id=library-cli -d username=$1 -d password=Pass_123456 | python3 -c "import json,sys;print(json.load(sys.stdin)['access_token'])"; }
for u in alice mike uma gary; do declare "KT_$u=$(kc_token $u)"; done
jwt "$KT_mike" | python3 -c "import json,sys;c=json.load(sys.stdin);print('   keycloak mike:',{'realm_roles':c['realm_access']['roles'],'aud':c['aud'],'loans-api':c['resource_access'].get('loans-api')})"
echo ">> RPT (UMA): authorization.permissions for mike on loans-api"
RPT=$(curl -s -X POST $KC/realms/library/protocol/openid-connect/token -H "Authorization: Bearer $KT_mike" \
  -d grant_type=urn:ietf:params:oauth:grant-type:uma-ticket -d audience=loans-api | python3 -c "import json,sys;print(json.load(sys.stdin)['access_token'])")
jwt "$RPT" | python3 -c "import json,sys;c=json.load(sys.stdin);print('   authorization:',json.dumps(c['authorization']));s=[x for p in c['authorization']['permissions'] for x in p.get('scopes',[])];assert set(s)=={'loans:read','loans:update','loans:approve'}"
expect 200 -H "Authorization: Bearer $RPT" "$GW/api/loans?page=1&pageSize=1"
expect 403 -H "Authorization: Bearer $RPT" -X DELETE $GW/api/loans/999999
echo "   manager: GET 200, DELETE 403 (permission from authorization.permissions)"

echo ">> permission matrix (Keycloak tokens via gateway): allowed = not 401/403"
export KT_alice KT_mike KT_uma KT_gary
python3 - <<'PY'
import json, os, subprocess
gw = "http://localhost:5000"
tok = {"admin": os.environ["KT_alice"], "manager": os.environ["KT_mike"], "user": os.environ["KT_uma"], "guest": os.environ["KT_gary"]}
rows = [  # (endpoint, method, url, body, allowed roles)
 ("GET /loans", "GET", "/api/loans?page=1&pageSize=1", None, "admin manager user"),
 ("POST /loans", "POST", "/api/loans", {"readerId": 0, "items": []}, "admin user"),
 ("PUT /loans/{id}", "PUT", "/api/loans/999999/details", {"deliveryType": "Pickup", "rowVersion": "AAAAAAAAAAA="}, "admin manager"),
 ("DELETE /loans/{id}", "DELETE", "/api/loans/999999", None, "admin"),
 ("POST /loans/{id}/approve", "POST", "/api/loans/999999/confirm", None, "admin manager"),
 ("GET /books", "GET", "/api/books?page=1&pageSize=1", None, "admin manager user guest"),
 ("POST /books", "POST", "/api/books", {}, "admin"),
 ("PUT /books/{id}", "PUT", "/api/books/999999", {"title": "x", "authorId": 1}, "admin manager"),
 ("DELETE /books/{id}", "DELETE", "/api/books/999999", None, "admin"),
 ("POST /books/{id}/publish", "POST", "/api/books/999999/publish", None, "admin manager"),
 ("GET /payments", "GET", "/api/payments", None, "admin"),
 ("POST /payments/process", "POST", "/api/payments/process", {"loanId": 1, "amount": 10, "method": "Card"}, "admin"),
 ("POST /payments/refund", "POST", "/api/payments/refund", {"paymentId": "00000000-0000-0000-0000-000000000001", "reason": "ci"}, "admin"),
]
def call(method, url, body, token):
    args = ["curl", "-s", "-o", "/dev/null", "-w", "%{http_code}", "-X", method, gw + url, "-H", f"Authorization: Bearer {token}"]
    if body is not None: args += ["-H", "Content-Type: application/json", "-d", json.dumps(body)]
    return int(subprocess.run(args, capture_output=True, text=True).stdout)
print(f"   {'Endpoint':28} {'admin':>6} {'manager':>8} {'user':>6} {'guest':>6}")
bad = []
for name, method, url, body, allowed in rows:
    codes = {}
    for role in ("admin", "manager", "user", "guest"):
        c = call(method, url, body, tok[role]); codes[role] = c
        ok = (c not in (401, 403)) == (role in allowed.split())
        if not ok or c >= 500: bad.append((name, role, c))
    print(f"   {name:28} " + " ".join(f"{codes[r]:>{w}}" for r, w in (("admin",6),("manager",8),("user",6),("guest",6))))
assert not bad, f"matrix mismatch: {bad}"
print("   matrix matches Tables 3.3.1-3.3.3 (403 instead of 500 for denied)")
PY

pkill -f "Platform.ApiGateway" || true; pkill -f "Platform.Aggregator" || true; pkill -f "Loans.Api" || true
pkill -f "Books.Api" || true; pkill -f "Feedback.WebApi" || true; pkill -f "Payments.Api" || true
pkill -f "Auth.Api" || true; pkill -f "Platform.IdentityServer" || true
docker rm -f keycloak >/dev/null
sleep 2

echo; echo ">> Aspire AppHost: IdentityServer (own SQL Server), Auth Service, Keycloak, Payments"
ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development \
  dotnet run -c Release --no-build --project Platform.AppHost --launch-profile http > /tmp/svc-apphost.log 2>&1 &
for i in $(seq 1 150); do
  if curl -s -o /dev/null -w "%{http_code}" -X POST $GW/auth/login -H 'Content-Type: application/json' -d '{"userNameOrEmail":"admin","password":"Admin_Pass123!"}' | grep -q 200; then break; fi
  sleep 5
done
expect 200 -X POST $GW/auth/login -H 'Content-Type: application/json' -d '{"userNameOrEmail":"admin","password":"Admin_Pass123!"}'
ADM=$(field "['accessToken']")
expect 200 -H "Authorization: Bearer $ADM" "$GW/api/aggregator/loans/1/full?cache=false"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   via Aspire: warnings',d['warnings']);assert d['warnings']==[]"
expect 200 -H "Authorization: Bearer $ADM" $GW/api/payments
expect 200 http://localhost:5010/.well-known/openid-configuration
docker ps --format '{{.Names}} {{.Image}}' | grep -Ei "keycloak|sql"
echo "Lab 09 smoke checks passed"
