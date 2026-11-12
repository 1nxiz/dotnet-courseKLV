# Feedback — відгуки й обговорення (Лаб. 04: Clean Architecture + CQRS/MediatR + MongoDB)

Мікросервіс контексту **«Відгуки та обговорення»** на базі MongoDB-бази Проєкту №3 з Лаб. 01 (`FeedbackDb`).

## Шари та залежності

```text
Feedback.WebApi ──► Feedback.Application ──► Feedback.Domain
       │                    ▲                      ▲
       └──► Feedback.Infrastructure ───────────────┘   (Infrastructure реалізує інтерфейси внутрішніх шарів)
```

| Шар | Вміст | Залежності |
|---|---|---|
| `Feedback.Domain` | `Common/BaseEntity` (ObjectId Id, CreatedAt, UpdatedAt, Version), `Common/ValueObject`; сутності `Review` (+ `Comment`), `Discussion` (+ `MessagePreview`), `Message`; value objects `Author`, `Rating`, `ReadingProgress`; `DomainException`; інтерфейси `IReviewRepository`, `IDiscussionRepository` | лише `MongoDB.Bson` (тип ObjectId), **жодного** проєкту й драйвера |
| `Feedback.Application` | `Commands/` (Create/Update/Delete review, AddComment, StartDiscussion, PostMessage), `Queries/` (GetReviewById, GetReviewsList, GetDiscussion), `Behaviors/` (Logging, UnhandledException, Validation, Performance), валідатори FluentValidation, `Common/Interfaces/IReviewQueries`, DTO, профіль AutoMapper, `AddApplication()` | Domain |
| `Feedback.Infrastructure` | `Persistence/` — `MongoDbContext` (IOptions), `MongoMappings` (BsonClassMap, camelCase), `RatingSerializer`, `ReviewRepository` (optimistic concurrency), `ReviewQueries` (aggregation pipeline, проєкція в DTO); `Seeding/` — `IDataSeeder`, сідери, `IndexCreationService`; `AddInfrastructure()` | Application, Domain |
| `Feedback.WebApi` | `Controllers/` (лише `ISender`), `ErrorHandling/FeedbackExceptionHandler`, `Program.cs`, `appsettings.{Environment}.json` | Application, Infrastructure (лише для DI) |

## CQRS і pipeline

* Базові інтерфейси `ICommand`, `ICommand<T>`, `IQuery<T>`; кожна операція — окремий об'єкт-запит і окремий обробник.
* Обробники команд працюють через `IReviewRepository` / `IDiscussionRepository`; обробники запитів — лише через
  `IReviewQueries` / `IDiscussionQueries` (реалізація в Infrastructure робить `$match`, `$sort`, `$skip`, `$limit`, `$project`).
* `AddMediatR` сканує збірку й підключає behaviors у порядку: **Logging → UnhandledException → Validation → Performance → обробник**.
* `ValidationBehavior` збирає помилки всіх валідаторів і кидає `ValidationException` — обробник не викликається
  (у лозі є `Handling CreateReviewCommand` і `failed`, але немає `Handled`).
* Валідатори перевіряють `ObjectId.TryParse` для Id і містять асинхронне правило з запитом до MongoDB
  («користувач уже залишав відгук на цю книгу»).

## MongoDB

* `BsonClassMap` у Infrastructure (без атрибутів у домені): конвенція camelCase, приватне поле `_comments` → `comments`,
  value objects створюються через конструктор (`MapCreator`), `Rating` серіалізується числом власним `RatingSerializer`,
  невідомі поля документа (наприклад `quote`) зберігаються через extra elements і не губляться при оновленні.
* Optimistic concurrency: `ReplaceOne` з фільтром `{_id, version: expected}`; 0 збігів → `ConflictException` → 409.
  `GET` повертає версію в `ETag`, `PUT` приймає очікувану версію в `If-Match`.
* Індекси створює `IndexCreationService` під час старту (ті самі імена, що в `db/p3/collections.js`):
  складені `{bookId:1, createdAt:-1}`, `{"author.userId":1, createdAt:-1}`, unique `{bookId:1, "author.userId":1}`,
  text `{text:"text"}`, для обговорень і повідомлень — свої.
* Сідінг: `IDataSeeder` для кожної колекції, `InsertManyAsync`, пропуск, якщо дані вже є (ідемпотентно).
* Конфігурація — секція `MongoDb` в `appsettings.json` / `.Development` / `.Staging` / `.Production`
  (`maxPoolSize`, `serverSelectionTimeoutMS` у рядку підключення); `ConnectionStrings:FeedbackDb` (Aspire) має пріоритет.
* Курсорна пагінація повідомлень обговорення: `GET /api/reviews/discussions/{id}?after=<id>&pageSize=20`.

## Помилки

| Виняток | HTTP |
|---|---|
| `NotFoundException` | 404 |
| `ValidationException` (FluentValidation) | 400 (ValidationProblemDetails) |
| `DomainException` | 400 |
| `ConflictException` (у т. ч. конфлікт версій) | 409 |
| `MongoWriteException` з `DuplicateKey` | 409 (інші помилки запису — 400) |
| `MongoConnectionException`, timeout вибору сервера | 503 |

## Запуск

```bash
docker run -d --name library-mongo -p 27017:27017 mongo:7      # якщо ще не запущено (Лаб. 01)
dotnet run --project Services/Feedback/Feedback.WebApi           # http://localhost:5103/swagger
```

Приклади запитів — `Feedback.WebApi/Feedback.WebApi.http`. Перевірки в mongosh: `db.reviews.find()`, `db.reviews.getIndexes()`.
