# Ручне розгортання в Kubernetes (Лаб. 10, ч. 2–3)

Власні маніфести для **books-api** (Каталог, аналог catalog-api методички) і **gateway** + SQL Server для каталогу.
Повна платформа розгортається з AppHost інструментом Aspir8 — див. `Platform.AppHost/aspirate-output` і кореневий README.

| Файл | Що описує |
|---|---|
| `00-namespace.yaml` | namespace `library` |
| `sqlserver.yaml` | SQL Server (Deployment + Service `sql`), пароль SA з Secret `sql-secrets` |
| `books-api-configmap.yaml` | ConfigMap `books-config`: середовище, Kestrel-endpoints у контейнері (8080 REST, 8081 gRPC), режим міграцій |
| `books-api-migrate-job.yaml` | Job `books-db-migrate`: EF-міграції BooksDb одним процесом до запуску реплік |
| `books-api-deployment.yaml` | Deployment (2 репліки, RollingUpdate без простою), probes `/health/live` і `/health/ready` (Лаб. 07), `envFrom` ConfigMap + Secret |
| `books-api-service.yaml` | Service `books-api` (ClusterIP, 80 -> 8080, 81 -> 8081) — DNS-ім'я для Gateway |
| `gateway-configmap.yaml` | ConfigMap `gateway-config`: `services__books-api__http__0=http://books-api` (service discovery, як в Aspire) |
| `gateway-deployment.yaml`, `gateway-service.yaml` | Gateway (2 репліки), Secret `gateway-secrets` з ключем JWT (Лаб. 09) |
| `kustomization.yaml` | `kubectl apply -k deploy/k8s` |

## Кроки (Docker Desktop з увімкненим Kubernetes; для kind — `kind load docker-image ...`)

```bash
# 1. Образи (багатоетапні Dockerfile: SDK лише збирає, у фінальний aspnet-образ копіюється publish; restore кешується окремим шаром)
docker build -t books-api:v1 -f Services/Books/Books.Api/Dockerfile .
docker build -t gateway:v1   -f Platform.ApiGateway/Dockerfile .

# 2. Namespace і Secrets — командами kubectl, щоб значення НЕ потрапили в YAML у репозиторії
kubectl apply -f deploy/k8s/00-namespace.yaml
export SA_PASSWORD='Library_Pass123!'
export JWT_SECRET='<ключ підпису JWT з Лаб. 09, >= 32 символи>'
export BOOKS_DB="Server=sql,1433;Database=BooksDb;User Id=sa;Password=$SA_PASSWORD;TrustServerCertificate=True;Encrypt=False"
kubectl -n library create secret generic sql-secrets --from-literal=MSSQL_SA_PASSWORD="$SA_PASSWORD"
kubectl -n library create secret generic books-secrets \
  --from-literal=ConnectionStrings__BooksDb="$BOOKS_DB" --from-literal=Jwt__SecretKey="$JWT_SECRET"
kubectl -n library create secret generic gateway-secrets --from-literal=Jwt__SecretKey="$JWT_SECRET"

# 3. Маніфести
kubectl apply -k deploy/k8s
kubectl -n library wait --for=condition=complete job/books-db-migrate --timeout=300s
kubectl -n library get pods,services
kubectl -n library describe pod -l app=books-api        # події, стан liveness / readiness

# 4. Запит через Gateway -> books-api (DNS Kubernetes: books-api -> адреса Service)
kubectl -n library port-forward service/gateway 8080:80
TOKEN=$(python3 deploy/k8s/dev-token.py "$JWT_SECRET" admin)
curl -H "Authorization: Bearer $TOKEN" "http://localhost:8080/api/books?page=1&pageSize=5"
kubectl -n library logs deploy/gateway | grep books-api
```

## Що перевірити на захисті
* `kubectl -n library delete pod <books-api-pod>` — Deployment одразу створює новий под (бажаний стан = 2 репліки).
* `kubectl -n library scale deploy/sql --replicas=0` — поди books-api лишаються **Running**, але стають **NotReady**
  (readiness: `books-db` Unhealthy) і не отримують трафіку; liveness (`self`) їх не перезапускає. Повернути: `--replicas=1`.
* Змінити значення в ConfigMap -> контейнер бачить його лише після `kubectl -n library rollout restart deploy/gateway`.
* `kubectl -n library get secret gateway-secrets -o yaml` — значення лише закодоване Base64 (не зашифроване); у production —
  шифрування etcd, External Secrets / HashiCorp Vault / Azure Key Vault.

## Відмінності від маніфестів Aspirate
| | Aspirate (`aspirate-output`) | Ручні маніфести |
|---|---|---|
| Образи | `dotnet publish /t:PublishContainer` (без Dockerfile) | власні багатоетапні Dockerfile |
| Конфігурація | ConfigMap на кожен ресурс з усіма змінними з моделі AppHost (рядки підключення, service discovery) | лише потрібні ключі; секрети окремо через `kubectl create secret` |
| Probes | не генеруються | liveness / readiness / startup з endpoint-ів Лаб. 07 |
| Репліки, стратегія | 1 репліка | 2 репліки, RollingUpdate `maxUnavailable: 0`, requests / limits |
| Міграції | кожен под на старті | окремий Job до запуску реплік |
