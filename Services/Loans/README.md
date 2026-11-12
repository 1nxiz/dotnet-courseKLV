# Loans — сервіс видач (Лаб. 02: тришарова архітектура на ADO.NET та Dapper)

Мікросервіс контексту **«Видачі»** як самостійна вертикаль: база `LoansDb` з Лаб. 01 → DAL → BLL → Web/API.

## Структура рішення

| Проєкт | Призначення |
|---|---|
| `Loans.Api` | Web/API: тонкі контролери (`LoansController`, `ReadersController`, `BookCopiesController`), глобальний обробник помилок `DomainExceptionHandler` (ProblemDetails), Swagger, Serilog, DI |
| `Loans.Bll` | Сервіси `ILoanService` / `IReaderService` / `IBookCopyService`, DTO (records + DataAnnotations), профілі AutoMapper |
| `Loans.Dal` | Інтерфейси репозиторіїв (`IGenericRepository<T>`, `ILoanRepository`, `IBookRepository`, `IReaderRepository`), реалізації на ADO.NET і Dapper, `UnitOfWork`, `SqlExceptionMapper`, `DatabaseInitializer` |
| `Loans.Domain` | Моделі (`Loan`, `LoanItem`, `LoanDetails`, `Reader`, `Book`, `Payment`), статуси, доменні винятки — спільні для DAL і BLL |
| `Loans.Tests` | Unit-тести BLL (Moq), перевірка AutoMapper (`AssertConfigurationIsValid`), інтеграційний тест відкату UoW, API-тести (WebApplicationFactory) |
| `Loans.AppHost` | .NET Aspire: API + SQL Server у контейнері однією командою |

```text
Loans.Api ──► Loans.Bll ──► Loans.Dal ──► SQL Server (LoansDb)
    │             │             │
    └─────────────┴──► Loans.Domain ◄──┘     (контролер ніколи не звертається до репозиторію напряму)
```

## DAL

* **Generic Repository:** `IGenericRepository<T>` — `GetByIdAsync`, `GetAllAsync`, `AddAsync`, `DeleteAsync` (м'яке видалення);
  спільний CRUD написано один раз у `BaseDapperRepository<T>`.
* **1 репозиторій на чистому ADO.NET:** `Ado/ReaderRepository` — `SqlCommand`, типізовані параметри, ручне читання
  `SqlDataReader`, оновлення з `RowVersion`; свідомо **не** успадковує Generic Repository.
* **2 репозиторії на ADO.NET + Dapper:** `Dapper/LoanRepository` (multi-mapping `Loan + LoanItem` одним JOIN-запитом,
  `QueryMultipleAsync` для `usp_GetLoanById`, виклики процедур `usp_ConfirmLoan`, `usp_ChangeLoanStatus`,
  `usp_UpdateLoanDetails`, `usp_RegisterPayment`, вставка всього агрегату) і `Dapper/BookRepository`
  (`IN @Ids`, умовний `UPDATE ... WHERE AvailableCopies >= @Quantity`).
* **Unit of Work:** одне `SqlConnection` на HTTP-запит (Scoped), транзакція з рівнем ізоляції, спільна для всіх
  репозиторіїв (`Func<SqlTransaction?>`), гарантований Commit/Rollback.
* **Безпека запитів:** усі значення — лише параметри (`@Id`, `new { Id = id }`), жодної конкатенації вхідних даних;
  ім'я таблиці в `BaseDapperRepository` — константа з коду. З'єднання й команди звільняються через `await using`,
  пул підключень SqlClient увімкнено за замовчуванням.
* **Помилки БД → доменні винятки** (`SqlExceptionMapper`): `50001` → `NotFoundException`, `50002` → `BusinessConflictException`,
  `50003` → `ConcurrencyException`, `50004`/`547` → `ValidationException`, `2601/2627` → `BusinessConflictException`.

### Рівень ізоляції і таймаути

* `READ COMMITTED` для всіх транзакційних операцій: не читає незафіксованих даних і тримає мінімум блокувань.
  Від втрачених оновлень захищають `UPDLOCK, HOLDLOCK` усередині процедур і `RowVersion`, від «від'ємного» залишку —
  умовний `UPDATE`. `REPEATABLE READ` / `SERIALIZABLE` дали б менше аномалій читання, але більше блокувань і ризик
  deadlock — компроміс **deadlocks vs. consistency** вирішено на користь коротких транзакцій.
* Таймаут кожної команди — 30 с (`RepositoryBase.CommandTimeoutSeconds`); deadlock (`1205`) повертається як 409 з порадою повторити.

## BLL

* DTO для кожного контракту (`LoanDto`, `CreateLoanDto`, `PayLoanDto`, ...), профілі `LoanProfile`, `ReaderProfile`,
  `BookCopyProfile` зареєстровані централізовано (`AddAutoMapper(typeof(LoanProfile).Assembly)`), конфігурацію перевіряє тест.
* **Транзакційна бізнес-операція** `ConfirmAsync`: `usp_ConfirmLoan` (статус → `Reserved`) + `ReserveCopiesAsync` для
  кожної позиції в одній транзакції; нестача примірників → `BusinessConflictException` → `Rollback` → статус лишається `Submitted`.
  Так само транзакційні `ReturnAsync` і `CancelAsync` (статус + повернення примірників).
* Під час створення видачі назва й ціна книги беруться з `dbo.Books` і записуються в позицію як **знімок**.
* Валідація на двох рівнях: DataAnnotations на DTO (API відхиляє запит до виклику сервісу) і бізнес-валідація в BLL
  (читач існує, книги існують, кур'єру потрібна адреса, дата повернення не в минулому).
* Ідемпотентність оплати: заголовок `Idempotency-Key` → `ExternalReference` → `usp_RegisterPayment` повертає той самий платіж.
* Конкурентність: optimistic — `RowVersion` у `PUT /api/loans/{id}/details` і `PUT /api/readers/{id}` (409 при конфлікті).

## Web/API

| Метод | Маршрут | Успіх | Помилки |
|---|---|---|---|
| GET | `/api/loans?readerId=&status=&page=&pageSize=` | 200 (пагінація) | 400 |
| GET | `/api/loans/{id}` | 200 | 404 |
| POST | `/api/loans` | **201 + Location** | 400 |
| PUT | `/api/loans/{id}/details` | 200 (новий RowVersion) | 400, 404, 409 |
| POST | `/api/loans/{id}/confirm` | 204 | 404, 409 (повторне підтвердження, нестача примірників) |
| POST | `/api/loans/{id}/pay` (+ `Idempotency-Key`) | 200 | 400, 404, 409 |
| GET | `/api/loans/{id}/payments` | 200 | 404 |
| POST | `/api/loans/{id}/issue` | 204 | 404, 409 (неоплачена) |
| POST | `/api/loans/{id}/return` | 204 | 404, 409 |
| POST | `/api/loans/{id}/cancel` | 204 | 404, 409 (після оплати) |
| DELETE | `/api/loans/{id}` | 204 | 404, 409 |
| GET/POST/PUT/DELETE | `/api/readers`, `/api/readers/{id}` | 200 / 201 / 204 | 400, 404, 409 |
| GET/PUT/DELETE | `/api/loans/books`, `/api/loans/books/{id}` | 200 / 201 / 204 | 400, 404 |

Атрибутна маршрутизація, усі дії асинхронні й передають `CancellationToken` до DAL; помилки — `application/problem+json`
з `traceId`. OpenAPI: `/swagger`.

## Запуск

**Через .NET Aspire (рекомендовано, потрібен Docker Desktop):**

```bash
dotnet run --project Services/Loans/Loans.AppHost
```

Відкриється Aspire Dashboard: ресурси `sql` (контейнер SQL Server) і `loans-api` мають статус **Running**.
Порожню базу в контейнері `Loans.Api` розгортає сам скриптами `db/p1` (`Database:InitializeOnStartup = true` у Development).
URL API (`http://localhost:5101`) видно в Dashboard → `/swagger`.

**Без Aspire** (база з Лаб. 01 у Docker, див. кореневий README): `dotnet run --project Services/Loans/Loans.Api`.
Рядок підключення — `ConnectionStrings:LoansDb` в `appsettings.json`, перевизначається змінною `ConnectionStrings__LoansDb`.

**Тести:** `dotnet test Services/Loans/Loans.Tests` (інтеграційні й API-тести виконуються, якщо задано `ConnectionStrings__LoansDb`).

## Приклади запитів

```bash
# список з пагінацією
curl "http://localhost:5101/api/loans?status=Submitted&page=1&pageSize=10"
# 404 у форматі ProblemDetails
curl -i http://localhost:5101/api/loans/999999
# читач з апострофом — синтаксичної помилки SQL немає (параметризація)
curl -X POST http://localhost:5101/api/readers -H "Content-Type: application/json" \
     -d "{\"email\":\"liam.obrien@example.com\",\"fullName\":\"Liam O'Brien\",\"cardNumber\":\"LIB-0100\"}"
# оформити і підтвердити видачу; повторне підтвердження -> 409
curl -X POST http://localhost:5101/api/loans -H "Content-Type: application/json" \
     -d "{\"readerId\":1,\"items\":[{\"bookId\":1,\"quantity\":1}]}"
curl -i -X POST http://localhost:5101/api/loans/7/confirm
curl -i -X POST http://localhost:5101/api/loans/7/confirm
# відкат транзакції: книги 8 немає на полиці -> 409, статус лишається Submitted
curl -X POST http://localhost:5101/api/loans -H "Content-Type: application/json" -d "{\"readerId\":2,\"items\":[{\"bookId\":8,\"quantity\":1}]}"
curl -i -X POST http://localhost:5101/api/loans/8/confirm
```

Більше прикладів — у `Loans.Api/Loans.Api.http` (запускаються прямо з VS Code / Rider / Visual Studio).
