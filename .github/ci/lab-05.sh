#!/usr/bin/env bash
# Lab 05: ServiceDefaults, API Gateway (YARP), Aggregator, CorrelationId, Aspire AppHost.
set -euo pipefail
source "$CI_DIR/common.sh"
GW=http://localhost:5000
SD=(services__loans-api__http__0=http://localhost:5101 services__books-api__http__0=http://localhost:5102
    services__feedback-api__http__0=http://localhost:5103 services__aggregator__http__0=http://localhost:5090)

# ---------- 1) Сервіси напряму (service discovery через конфігурацію) ----------
start_service loans Services/Loans/Loans.Api 5101
start_service books Services/Books/Books.Api 5102
start_service feedback Services/Feedback/Feedback.WebApi 5103
start_service aggregator Platform.Aggregator 5090 "${SD[@]}"
start_service gateway Platform.ApiGateway 5000 "${SD[@]}"

for p in 5101 5102 5103 5090 5000; do expect 200 http://localhost:$p/health; expect 200 http://localhost:$p/alive; done

echo ">> routing through the gateway"
expect 200 "$GW/api/books?page=1&pageSize=3"
expect 200 "$GW/api/loans?page=1&pageSize=3"
expect 200 "$GW/api/readers"
expect 200 "$GW/api/reviews?bookId=4"
expect 404 "$GW/api/unknown"

echo ">> CorrelationId: client value is echoed and propagated gateway -> aggregator -> services"
CID="ci-$(date +%s)"
curl -s -D /tmp/h.txt -o /tmp/resp.json -H "X-Correlation-Id: $CID" "$GW/api/aggregator/loans/1/full"
grep -i "x-correlation-id: $CID" /tmp/h.txt
python3 - <<'PY'
import json
d=json.load(open('/tmp/resp.json'))
print('   loan',d['number'],'books',len(d['books']),'warnings',d['warnings'])
assert d['loanId']==1 and len(d['books'])>=1 and d['warnings']==[]
assert all('catalog' in b for b in d['books']), 'catalog data missing'
PY
sleep 1
for s in gateway aggregator loans books feedback; do grep -q "$CID" /tmp/svc-$s.log && echo "   $s log has $CID" || { echo "!! $CID not in $s log"; exit 1; }; done
grep -q '"@mt"' /tmp/svc-loans.log && echo "   structured JSON logs (CompactJson)"

echo ">> generated CorrelationId when the client sends none"
curl -s -D /tmp/h.txt -o /dev/null "$GW/api/books?page=1"
grep -i "x-correlation-id:" /tmp/h.txt

expect 404 "$GW/api/aggregator/loans/999999/full"

echo ">> partial failure: feedback-api is down -> 200 with warnings"
pkill -f "Feedback.WebApi" || true
sleep 2
expect 200 "$GW/api/aggregator/loans/1/full"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   warnings',d['warnings']);assert any('feedback-api' in w for w in d['warnings']) and len(d['books'])>=1"
echo ">> gateway reports an unavailable upstream as 5xx"
expect 502 "$GW/api/reviews?bookId=4"
pkill -f "Platform.ApiGateway" || true; pkill -f "Platform.Aggregator" || true
pkill -f "Loans.Api" || true; pkill -f "Books.Api" || true
sleep 2

# ---------- 2) Уся платформа однією командою через Aspire AppHost ----------
echo ">> dotnet run Platform.AppHost (containers SQL Server + MongoDB, services, aggregator, gateway)"
dotnet dev-certs https >/dev/null 2>&1 || true
ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development \
  dotnet run -c Release --no-build --project Platform.AppHost --launch-profile http > /tmp/svc-apphost.log 2>&1 &
for i in $(seq 1 120); do
  if curl -s -o /dev/null -w "%{http_code}" "$GW/api/aggregator/loans/1/full" | grep -q 200; then break; fi
  sleep 5
done
expect 200 "$GW/api/aggregator/loans/1/full"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   via Aspire: books',len(d['books']),'warnings',d['warnings']);assert d['warnings']==[]"
expect 200 "$GW/api/reviews?bookId=4"
expect 200 "$GW/api/books?page=1"
docker ps --format '{{.Names}} {{.Image}}' | grep -Ei "sql|mongo"
echo "Lab 05 smoke checks passed"
