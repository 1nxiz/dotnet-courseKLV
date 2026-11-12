#!/usr/bin/env bash
# Lab 10: Kubernetes (kind) — manual manifests (books-api + gateway, ConfigMap, Secret, probes) and Aspir8 (aspirate).
set -euo pipefail
source "$CI_DIR/common.sh"
JWT_SECRET="ci-k8s-jwt-signing-key-0123456789-abcdefghijklmnop"
K="kubectl -n library"

echo ">> kind cluster"
kind create cluster --name library --wait 120s >/dev/null
kubectl cluster-info --context kind-library | head -1

echo; echo "================ Part 2-3: manual manifests ================"
docker build -q -t books-api:v1 -f Services/Books/Books.Api/Dockerfile .
docker build -q -t gateway:v1 -f Platform.ApiGateway/Dockerfile .
docker images --format '{{.Repository}}:{{.Tag}} {{.Size}}' | grep -E "^(books-api|gateway):v1"
docker pull -q mcr.microsoft.com/mssql/server:2022-latest >/dev/null
kind load docker-image books-api:v1 gateway:v1 mcr.microsoft.com/mssql/server:2022-latest --name library >/dev/null

kubectl apply -f deploy/k8s/00-namespace.yaml
SA='K8s_Pass123!'
$K create secret generic sql-secrets --from-literal=MSSQL_SA_PASSWORD="$SA"
$K create secret generic books-secrets --from-literal=ConnectionStrings__BooksDb="Server=sql,1433;Database=BooksDb;User Id=sa;Password=$SA;TrustServerCertificate=True;Encrypt=False" --from-literal=Jwt__SecretKey="$JWT_SECRET"
$K create secret generic gateway-secrets --from-literal=Jwt__SecretKey="$JWT_SECRET"
kubectl apply -k deploy/k8s
$K wait --for=condition=complete job/books-db-migrate --timeout=420s || { $K logs job/books-db-migrate --tail=50; exit 1; }
$K logs job/books-db-migrate | grep -o '"@mt":"[^"]*"' | tail -3
$K rollout status deploy/books-api --timeout=300s
$K rollout status deploy/gateway --timeout=300s
$K get pods,services -o wide

$K port-forward service/gateway 8080:80 >/tmp/pf.log 2>&1 &
PF=$!; sleep 4
TOKEN=$(python3 deploy/k8s/dev-token.py "$JWT_SECRET" admin)
expect 401 "http://localhost:8080/api/books"
expect 200 -H "Authorization: Bearer $TOKEN" "http://localhost:8080/api/books?page=1&pageSize=3"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   books via gateway -> books-api:',[b['title'] for b in d['items']]);assert len(d['items'])>0"
$K logs deploy/gateway | grep -c books-api | xargs echo "   gateway log lines mentioning books-api:"

echo ">> pod deleted -> Deployment recreates it"
POD=$($K get pod -l app=books-api -o jsonpath='{.items[0].metadata.name}')
$K delete pod $POD --wait=false
sleep 3
$K rollout status deploy/books-api --timeout=180s
$K get pods -l app=books-api
test "$($K get pods -l app=books-api --field-selector=status.phase=Running -o name | wc -l)" -ge 2

echo ">> database down -> books-api Running but NotReady (readiness), not restarted (liveness)"
RESTARTS_BEFORE=$($K get pods -l app=books-api -o jsonpath='{range .items[*]}{.status.containerStatuses[0].restartCount}{" "}{end}')
$K scale deploy/sql --replicas=0
for i in $(seq 1 40); do
  READY=$($K get pods -l app=books-api -o jsonpath='{range .items[*]}{.status.containerStatuses[0].ready}{" "}{end}')
  [[ "$READY" != *true* ]] && break; sleep 3
done
$K get pods -l app=books-api
echo "   ready flags: $READY  restarts before: $RESTARTS_BEFORE after: $($K get pods -l app=books-api -o jsonpath='{range .items[*]}{.status.containerStatuses[0].restartCount}{" "}{end}')"
[[ "$READY" != *true* ]]
$K get endpoints books-api
$K scale deploy/sql --replicas=1
$K rollout status deploy/sql --timeout=180s
for i in $(seq 1 60); do $K get pods -l app=books-api -o jsonpath='{range .items[*]}{.status.containerStatuses[0].ready}{" "}{end}' | grep -q "true true" && break; sleep 5; done
$K get pods -l app=books-api
$K get pods -l app=books-api -o jsonpath='{range .items[*]}{.status.containerStatuses[0].ready}{" "}{end}' | grep -q "true true"
echo "   database back (data kept in PVC) -> pods Ready again without restarts"

echo ">> ConfigMap change is applied after rollout restart; Secret is only Base64-encoded"
$K patch configmap gateway-config --type merge -p '{"data":{"Serilog__MinimumLevel__Default":"Warning"}}'
$K rollout restart deploy/gateway && $K rollout status deploy/gateway --timeout=180s
$K exec deploy/gateway -- printenv Serilog__MinimumLevel__Default
$K get secret gateway-secrets -o jsonpath='{.data.Jwt__SecretKey}' | base64 -d | cut -c1-12; echo "... (decoded from Base64)"
kill $PF || true
kubectl delete namespace library --wait=false

echo; echo "================ Part 1: Aspir8 (aspirate) ================"
dotnet tool install -g aspirate --version 9.1.0 >/dev/null; export PATH=$PATH:$HOME/.dotnet/tools
echo ">> images for every project (as aspirate does: dotnet publish /t:PublishContainer, no Dockerfile)"
declare -A PROJ=( [identityserver]=Platform.IdentityServer/Platform.IdentityServer.csproj [auth-api]=Services/Auth/Auth.Api/Auth.Api.csproj
  [loans-api]=Services/Loans/Loans.Api/Loans.Api.csproj [books-api]=Services/Books/Books.Api/Books.Api.csproj
  [feedback-api]=Services/Feedback/Feedback.WebApi/Feedback.WebApi.csproj [payments-api]=Services/Payments/Payments.Api/Payments.Api.csproj
  [aggregator]=Platform.Aggregator/Platform.Aggregator.csproj [gateway]=Platform.ApiGateway/Platform.ApiGateway.csproj )
for name in "${!PROJ[@]}"; do
  dotnet publish "${PROJ[$name]}" -c Release -t:PublishContainer -p:ContainerRepository=$name -p:ContainerImageTag=latest -v q -nologo 2>&1 | grep -E "error|Pushed image" | sed "s/^/   /"
done
docker images --format '{{.Repository}}:{{.Tag}} {{.Size}}' | grep ":latest" | grep -E "$(echo ${!PROJ[@]} | tr ' ' '|')"
kind load docker-image $(for n in "${!PROJ[@]}"; do echo -n "$n:latest "; done) --name library >/dev/null

pushd Platform.AppHost >/dev/null
aspirate generate --non-interactive --secret-password "ci-aspirate-secret" --skip-build --include-dashboard true \
  --image-pull-policy IfNotPresent --namespace library-aspirate --container-image-tag latest --output-format kustomize \
  -pa sql-password='Library_Pass123!' -pa keycloak-admin=admin -pa keycloak-password='Admin_Pass123!' 2>&1 | sed 's/\x1b\[[0-9;]*m//g' | grep -E "Done|Error|error|Execution" | tail -40
echo "---- generated files"; find aspirate-output -type f | sort
echo "---- secrets are NOT in ConfigMaps:"; grep -rl "Password=\|password=" aspirate-output --include=kustomization.yaml || echo "   none"
aspirate apply --help 2>&1 | sed 's/\x1b\[[0-9;]*m//g' | grep -E "^\s+-" | cut -c1-120
kubectl apply -k aspirate-output
popd >/dev/null
A="kubectl -n library-aspirate"
for s in $($A get statefulset -o name); do $A rollout status $s --timeout=600s; done
$A wait --for=condition=available deploy --all --timeout=900s || true
$A get pods,services
NOT_RUNNING=$($A get pods --no-headers | awk '$3!="Running"' | wc -l)
$A get pods --no-headers | awk '$3!="Running"' || true
for d in $($A get deploy -o name); do echo "== logs $d (Error/Fatal)"; $A logs $d --tail=400 2>/dev/null | grep -E '"@l":"(Error|Fatal)"' | grep -o '"@mt":"[^"]*"' | sort | uniq -c | head -5; done
test "$NOT_RUNNING" -eq 0

GP=$($A get service gateway -o jsonpath='{.spec.ports[?(@.name=="http")].port}')
$A port-forward service/gateway 8080:$GP >/tmp/pf2.log 2>&1 &
PF=$!; sleep 5
expect 401 "http://localhost:8080/api/books"
for i in $(seq 1 30); do code=$(curl -s -o /tmp/resp.json -w "%{http_code}" -X POST http://localhost:8080/auth/login -H 'Content-Type: application/json' -d '{"userNameOrEmail":"admin","password":"Admin_Pass123!"}'); [ "$code" = 200 ] && break; sleep 5; done
echo "   [$code] POST /auth/login (auth-api in cluster)"
ADM=$(python3 -c "import json;print(json.load(open('/tmp/resp.json'))['accessToken'])")
expect 200 -H "Authorization: Bearer $ADM" "http://localhost:8080/api/books?page=1&pageSize=3"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   books (aspirate):',[b['title'] for b in d['items']]);assert len(d['items'])>0"
expect 200 -H "Authorization: Bearer $ADM" "http://localhost:8080/api/loans?page=1&pageSize=2"
expect 200 -H "Authorization: Bearer $ADM" "http://localhost:8080/api/aggregator/loans/1/full?cache=false"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   aggregator (gRPC inside cluster): books',len(d['books']),'warnings',d['warnings'])"
kill $PF || true

tar -czf /tmp/aspirate-output.tgz --exclude='*.secrets' --exclude='.*.secrets' -C Platform.AppHost aspirate-output
echo "=====BEGIN-ASPIRATE-TGZ====="; base64 -w0 /tmp/aspirate-output.tgz; echo; echo "=====END-ASPIRATE-TGZ====="
echo "Lab 10 smoke checks passed"
