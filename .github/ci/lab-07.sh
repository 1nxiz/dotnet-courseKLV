#!/usr/bin/env bash
# Lab 07: resilience policies, business metrics, health checks (live / ready, Degraded Redis, gRPC dependencies).
set -euo pipefail
source "$CI_DIR/common.sh"
GW=http://localhost:5000
SD=(services__loans-api__http__0=http://localhost:5101 services__books-api__http__0=http://localhost:5102
    services__feedback-api__http__0=http://localhost:5103 services__aggregator__http__0=http://localhost:5090
    services__loans-api__grpc__0=http://localhost:5111 services__books-api__grpc__0=http://localhost:5112
    services__feedback-api__grpc__0=http://localhost:5113)

# health <url> <expected http code> <expected status>
health() {
  local code; code=$(curl -s -o /tmp/h.json -w "%{http_code}" "$1")
  local st; st=$(python3 -c "import json;print(json.load(open('/tmp/h.json'))['status'])" 2>/dev/null || cat /tmp/h.json)
  echo "   [$code] $1 -> $st  $(python3 -c "import json;print(', '.join(c['name']+'='+c['status'] for c in json.load(open('/tmp/h.json'))['checks']))" 2>/dev/null)"
  [ "$code" = "$2" ] && [ "$st" = "$3" ] || { cat /tmp/h.json; echo "!! expected $2 $3"; exit 1; }
}

start_service loans Services/Loans/Loans.Api 5101
start_service books Services/Books/Books.Api 5102
start_service feedback Services/Feedback/Feedback.WebApi 5103
start_service aggregator Platform.Aggregator 5090 "${SD[@]}"
start_service gateway Platform.ApiGateway 5000 "${SD[@]}"

echo ">> liveness / readiness with per-check details"
for p in 5101 5102 5103 5090; do health http://localhost:$p/health/live 200 Healthy; health http://localhost:$p/health/ready 200 Healthy; done
grep -q '"loans-db"' <(curl -s http://localhost:5101/health/ready)
grep -q '"feedback-db"' <(curl -s http://localhost:5103/health/ready)
grep -q '"books-api"' <(curl -s http://localhost:5090/health/ready)
docker run --rm --network host fullstorydev/grpcurl:latest -plaintext localhost:5112 grpc.health.v1.Health/Check

echo ">> business operations (metrics are recorded around them)"
expect 200 -X POST $GW/api/books/3/copies -H 'Content-Type: application/json' -d '{"quantity":2}'
expect 200 -X POST $GW/api/books/3/write-off -H 'Content-Type: application/json' -d '{"quantity":1}'
expect 200 "$GW/api/aggregator/loans/1/full?cache=false"

echo ">> Redis down -> readiness Degraded (200), services keep answering"
docker stop redis >/dev/null
sleep 2
health http://localhost:5102/health/ready 200 Degraded
health http://localhost:5101/health/ready 200 Degraded
expect 200 $GW/api/books/4
expect 200 "$GW/api/aggregator/loans/1/full?cache=false"
docker start redis >/dev/null
sleep 5

echo ">> feedback-api down -> partial responses, retries, circuit breaker opens (HTTP transport)"
pkill -f "Feedback.WebApi" || true
sleep 2
for i in $(seq 1 15); do curl -s -o /tmp/resp.json "http://localhost:5090/api/aggregator/loans/1/full?transport=http&cache=false"; done
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   warnings',d['warnings']);assert any('feedback-api' in w for w in d['warnings'])"
# логи — CompactJson: шаблон у "@mt", значення у властивостях
grep '"@mt":"Retry {Attempt} to {Service}' /tmp/svc-aggregator.log | grep -q '"Service":"feedback-api"' && echo "   retries logged"
grep '"@mt":"Circuit to {Service} OPENED' /tmp/svc-aggregator.log | grep -m1 '"Service":"feedback-api"' | cut -c1-260
T=$(curl -s -o /dev/null -w "%{time_total}" "http://localhost:5090/api/aggregator/loans/1/full?transport=http&cache=false")
echo "   with open circuit the call takes ${T}s (no waiting for timeouts)"
python3 -c "assert $T < 2"
echo "   gRPC transport:"
expect 200 "$GW/api/aggregator/loans/1/full?cache=false"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   warnings',d['warnings']);assert any('feedback-api' in w for w in d['warnings'])"
health http://localhost:5090/health/ready 200 Degraded

echo ">> books-api down -> aggregator readiness Unhealthy (503)"
pkill -f "Books.Api" || true
sleep 2
health http://localhost:5090/health/ready 503 Unhealthy
health http://localhost:5090/health/live 200 Healthy

echo ">> SQL Server down -> loans live 200, ready 503"
docker stop mssql >/dev/null
sleep 3
health http://localhost:5101/health/live 200 Healthy
health http://localhost:5101/health/ready 503 Unhealthy
docker start mssql >/dev/null
for i in $(seq 1 30); do curl -s -o /dev/null -w "%{http_code}" http://localhost:5101/health/ready | grep -q 200 && break; sleep 3; done
health http://localhost:5101/health/ready 200 Healthy

pkill -f "Platform.ApiGateway" || true; pkill -f "Platform.Aggregator" || true; pkill -f "Loans.Api" || true
sleep 2

echo ">> Aspire AppHost: resources become healthy (WithHttpHealthCheck)"
ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development \
  dotnet run -c Release --no-build --project Platform.AppHost --launch-profile http > /tmp/svc-apphost.log 2>&1 &
for i in $(seq 1 120); do
  if curl -s -o /dev/null -w "%{http_code}" "$GW/api/aggregator/loans/1/full" | grep -q 200; then break; fi
  sleep 5
done
expect 200 "$GW/api/aggregator/loans/1/full?cache=false"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   via Aspire: warnings',d['warnings']);assert d['warnings']==[]"
health $GW/health/ready 200 Healthy
echo "Lab 07 smoke checks passed"
