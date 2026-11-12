# Books — сервіс каталогу (Лаб. 03: тришарова архітектура на EF Core)

Мікросервіс контексту **«Каталог книг»** на базі реляційної БД Проєкту №2 з Лаб. 01 (`BooksDb`):
модель описана кодом (Code First + Fluent API), а база створюється **міграціями** EF Core.

| Проєкт | Призначення |
|---|---|
| `Books.Domain` | Сутності `Book`, `BookDetail`, `BookImage`, `Author`, `Genre`, `BookGenre` з інкапсульованими правилами; доменні винятки |
| `Books.Dal` | `AppDbContext`, `Configurations/` (`IEntityTypeConfiguration<T>` на кожну сутність), `Migrations/` (InitialCreate + SQL-скрипт), репозиторії, Unit of Work, `Seeding/DbInitializer` |
| `Books.Bll` | Сервіси, DTO, профіль AutoMapper, валідатори FluentValidation, `Specifications/` (Ardalis.Specification) |
| `Books.Api` | Контролери, глобальний обробник ProblemDetails, Serilog (console + file), Swagger, міграції й сідінг під час старту, Dockerfile |
| `Books.Tests` | Тести сутностей, валідаторів, специфікацій, AutoMapper; API-тести (WebApplicationFactory) |

## Модель і Fluent API

* Сутності не мають відкритих сетерів: ціна задається в конструкторі й перевіряється (`Price > 0`), кількість примірників
  змінюється лише методами `AddCopies` / `WriteOffCopies` (аналог `AddStock` / `RemoveStock` у eShop), а також `ChangePrice`,
  `Rename`, `Publish`, `SetDetail`, `AddImage`, `AddToGenre`.
* Кожна сутність має окремий клас `IEntityTypeConfiguration<T>` (таблиця, ключі, довжини, `decimal(18,2)`, унікальні індекси,
  `HasCheckConstraint`, зв'язки); усі підключаються в `OnModelCreating` через `ApplyConfigurationsFromAssembly`.
* Зв'язки: **1:1** `Book ↔ BookDetail` (PK = FK), **1:N** `Author → Books`, `Book → BookImages`,
  **M:N** `Book ↔ Genre` через **явну проміжну сутність** `BookGenre` зі складеним ключем `(BookId, GenreId)` і власним полем `AddedAt`.
* `RowVersion` — оптимістична конкурентність; `UpdatedAt` заповнюється в `SaveChangesAsync`.

## Міграції

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate --project Services/Books/Books.Dal --startup-project Services/Books/Books.Api
dotnet ef database update --project Services/Books/Books.Dal --startup-project Services/Books/Books.Api
dotnet ef migrations script --idempotent --project Services/Books/Books.Dal --startup-project Services/Books/Books.Api
```

Початкова міграція вже в репозиторії: `Books.Dal/Migrations/*_InitialCreate.cs`, знімок моделі `AppDbContextModelSnapshot.cs`,
згенерований ідемпотентний SQL-скрипт `Books.Dal/Migrations/InitialCreate.sql`. Під час старту застосунок сам виконує
`MigrateAsync()`. Якщо `BooksDb` раніше створено скриптом `db/p2/schema.sql` (Лаб. 01, без історії міграцій), у Development
вона перестворюється міграціями (`Database:RecreateIfNotMigrated = true`).

## Репозиторії та Unit of Work

* `GenericRepository<T>` — асинхронні `AddAsync`, `GetByIdAsync`, `GetAllAsync`; `Update` / `Delete` лише змінюють стан у контексті;
  `ListAsync(spec)` / `CountAsync(spec)` застосовують специфікацію.
* `BookRepository.GetWithDetailsAsync` — **Eager Loading** (`Include` / `ThenInclude`, один SQL-запит).
* `GenreRepository.GetWithBooksAsync` — **Explicit Loading** (`Entry(...).Collection(...).Query().Include(...).LoadAsync()`, два запити).
* `BookGenreRepository.GetRecentlyAddedAsync` — **LINQ to Entities** через проміжну сутність M:N та її поле `AddedAt`.
* `UnitOfWork` — набір репозиторіїв і єдиний `SaveChangesAsync()` наприкінці бізнес-операції.

У лозі (категорія `Microsoft.EntityFrameworkCore.Database.Command`) видно: `GET /api/books/1` — один `SELECT` з JOIN,
`GET /api/books/genres/5` — два окремі `SELECT`.

## Сідінг (ідемпотентний)

* **Статичний**: довідник `Genres` з фіксованими Id — `HasData` у `GenreConfiguration` (потрапляє в міграцію).
* **Програмний**: `DbInitializer.SeedAsync` після `MigrateAsync()` — автори, книги, деталі, зображення, жанри; якщо книги
  вже є — нічого не додає. Вимикається в Production (`!app.Environment.IsProduction()`).

## Пагінація, фільтрація, сортування

`GET /api/books?page=2&pageSize=5&search=...&minPrice=&maxPrice=&authorId=&genreId=&isPublished=&sortBy=price&sortDir=desc`

* `page` з 1, `pageSize` за замовчуванням 20, максимум 100; сортування лише за дозволеними полями (`title`, `price`, `year`, `createdAt`).
* Фільтри, `OrderBy`, `Skip/Take` описані специфікацією `BooksFilterSpec` і виконуються в SQL **до** `ToListAsync()`
  (`OFFSET ... FETCH NEXT`); `totalCount` рахує `COUNT(*)` у БД за тими самими критеріями без пагінації.
* Відповідь: `items`, `totalCount`, `page`, `pageSize`, `totalPages`, `hasNext`, `hasPrevious`; ті самі метадані — у заголовках
  `X-Pagination-TotalCount`, `X-Pagination-Page`, `X-Pagination-PageSize`, `X-Pagination-TotalPages`.
* Друга специфікація `PublishedBooksByAuthorSpec` (`GET /api/books/authors/{id}/books`) працює через той самий `ListAsync(spec)`.

## Валідація і помилки

* DataAnnotations (`[Required]`, `[StringLength]`) на DTO — прості правила; `[ApiController]` відхиляє запит автоматично.
* FluentValidation (`Books.Bll/Validation`) — складні правила; валідатори зареєстровані `AddValidatorsFromAssemblyContaining`
  і викликаються **явно** (`validator.ValidateAsync`) з відповіддю `ValidationProblem(...)` (без застарілого `AddFluentValidation`).
* `ExceptionToProblemDetailsHandler` (IExceptionHandler + AddProblemDetails): `EntityNotFoundException` → 404,
  `ValidationException` → 400, `ConflictException` / `DomainException` / конфлікт версій / дублікат ключа → 409, інше → 500;
  деталі 500 — лише в Development; кожен виняток логується один раз.

## Serilog

Секція `Serilog` в `appsettings.json`: рівні (Information/Warning/Error), sinks **Console** і **File** (`logs/books-api-*.log`,
CompactJson), `Enrich.FromLogContext`; `LogContextMiddleware` додає `CorrelationId` і `UserId`, `RequestPath` додає ASP.NET Core;
`UseSerilogRequestLogging()`; SQL-команди EF Core — на рівні Information.

## Запуск

```bash
# SQL Server з Лаб. 01 (docker run ... library-sql) уже запущено
dotnet run --project Services/Books/Books.Api        # http://localhost:5102/swagger
```

Приклади — `Books.Api/Books.Api.http`.

| Запит | Результат |
|---|---|
| `GET /api/books?page=2&pageSize=5` | 200, `totalCount` = кількість усіх книг у БД |
| `POST /api/books` з порожнім `title` | 400 ValidationProblemDetails з помилками по полях |
| `POST /api/books` вдруге з тим самим ISBN | 409 ProblemDetails |
| `GET /api/books/999999` | 404 ProblemDetails |
| `POST /api/books/8/write-off` `{ "quantity": 100 }` | 409 (правило сутності) |
