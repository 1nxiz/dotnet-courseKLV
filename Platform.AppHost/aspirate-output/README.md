# aspirate-output (Лаб. 10, ч. 1)

Маніфести Kubernetes, згенеровані **Aspir8** з моделі `Platform.AppHost` командою
`aspirate generate --output-format kustomize --namespace library-aspirate --include-dashboard true --image-pull-policy IfNotPresent`.

Секретів тут немає:
* рядки підключення проєктів aspirate виносить у `secretGenerator` з файлами `.<ресурс>.secrets` — вони не комітяться
  (`.gitignore`), а `aspirate apply` створює їх, розшифровуючи `aspirate-state.json` (теж не в репозиторії) паролем;
* паролі контейнерів (Mongo, Redis, RabbitMQ, Keycloak), які aspirate записує в ConfigMap, тут замінено на `<set-by-aspirate-generate>`.

Тому для розгортання маніфести треба згенерувати у себе (`cd Platform.AppHost && aspirate generate && aspirate apply`),
а ця папка — результат для перегляду та порівняння з ручними маніфестами `deploy/k8s`.
