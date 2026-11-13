/* =====================================================================
   Проєкт №1 — LoansDb (контекст «Видачі», SQL Server + ADO.NET & Dapper)
   Онлайн-бібліотека: читач оформлює видачу книг, бібліотека резервує
   примірники, читач сплачує оренду, отримує книги й повертає їх.

   Скрипт ідемпотентний: повторний запуск нічого не дублює.
   Зв'язки всередині бази:
     1:1  Loans  <-> LoanDetails        (PK LoanDetails = FK на Loans)
     1:N  Readers -> Loans, Loans -> Payments, Loans -> LoanStatusHistory
     M:N  Loans  <-> Books через LoanItems (проміжна таблиця з власними полями)
   Між базами — ЖОДНОГО FOREIGN KEY: Books.Id — той самий Id, що в BooksDb,
   але це звичайний стовпець (локальна копія довідника Каталогу).
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_ID(N'LoansDb') IS NULL
    CREATE DATABASE LoansDb;
GO

USE LoansDb;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* Послідовність для людиночитного номера видачі (LN-1000, LN-1001, ...) */
IF OBJECT_ID(N'dbo.LoanNumberSeq', N'SO') IS NULL
    CREATE SEQUENCE dbo.LoanNumberSeq AS BIGINT START WITH 1000 INCREMENT BY 1;
GO

/* ---------------------------------------------------------------------
   Readers — читачі бібліотеки (аналог Customer / Buyer в eShop)
   PK: BIGINT IDENTITY — записи створює лише цей сервіс, вставка послідовна,
   ключ компактний (8 байт) і не фрагментує кластерний індекс (на відміну від GUID).
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Readers', N'U') IS NULL
CREATE TABLE dbo.Readers (
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Readers PRIMARY KEY,
    Email       NVARCHAR(256) NOT NULL,                          -- унікальність серед активних: UX_Readers_Email_Active
    FullName    NVARCHAR(200) NOT NULL,
    Phone       NVARCHAR(30)  NULL,
    CardNumber  NVARCHAR(20)  NOT NULL CONSTRAINT UQ_Readers_CardNumber UNIQUE,   -- номер читацького квитка
    -- аудит і м'яке видалення
    CreatedAt   DATETIME2(3)  NOT NULL CONSTRAINT DF_Readers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy   NVARCHAR(100) NULL,
    UpdatedAt   DATETIME2(3)  NULL,
    UpdatedBy   NVARCHAR(100) NULL,
    IsDeleted   BIT           NOT NULL CONSTRAINT DF_Readers_IsDeleted DEFAULT 0,
    RowVersion  ROWVERSION    NOT NULL,                          -- оптимістична конкурентність
    CONSTRAINT CK_Readers_Email    CHECK (Email LIKE N'%_@_%._%'),
    CONSTRAINT CK_Readers_FullName CHECK (LEN(FullName) > 0)
);
GO

/* ---------------------------------------------------------------------
   Books — ЛОКАЛЬНА КОПІЯ довідника книг Каталогу (BooksDb).
   Id = Id книги в BooksDb (без IDENTITY і без FOREIGN KEY на іншу базу).
   Title, Isbn, Price — копія для читання, оновлюється подіями з Каталогу (Лаб. 08).
   AvailableCopies — ВЛАСНІ дані контексту «Видачі»: скільки примірників
   зараз на полиці (зменшується при резервуванні, збільшується при поверненні).
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Books', N'U') IS NULL
CREATE TABLE dbo.Books (
    Id              BIGINT        NOT NULL CONSTRAINT PK_Books PRIMARY KEY,
    Title           NVARCHAR(300) NOT NULL,
    Isbn            NVARCHAR(20)  NULL,
    Price           DECIMAL(18,2) NOT NULL CONSTRAINT CK_Books_Price CHECK (Price > 0),           -- плата за видачу
    AvailableCopies INT           NOT NULL CONSTRAINT DF_Books_AvailableCopies DEFAULT 0
                                           CONSTRAINT CK_Books_AvailableCopies CHECK (AvailableCopies >= 0),
    SourceUpdatedAt DATETIME2(3)  NULL,      -- час останньої застосованої події Каталогу (захист від out-of-order)
    CreatedAt       DATETIME2(3)  NOT NULL CONSTRAINT DF_Books_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       NVARCHAR(100) NULL,
    UpdatedAt       DATETIME2(3)  NULL,
    UpdatedBy       NVARCHAR(100) NULL,
    IsDeleted       BIT           NOT NULL CONSTRAINT DF_Books_IsDeleted DEFAULT 0,
    RowVersion      ROWVERSION    NOT NULL
);
GO

/* ---------------------------------------------------------------------
   Loans — видачі (агрегат «Видача» = Loans + LoanDetails + LoanItems).
   Статуси за зразком OrderStatus з eShop:
     Submitted -> AwaitingValidation -> Reserved -> Paid -> Issued -> Returned
     Cancelled — лише до оплати (Submitted / AwaitingValidation / Reserved).
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Loans', N'U') IS NULL
CREATE TABLE dbo.Loans (
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Loans PRIMARY KEY,
    Number      NVARCHAR(20)  NOT NULL
                CONSTRAINT DF_Loans_Number DEFAULT (N'LN-' + CAST(NEXT VALUE FOR dbo.LoanNumberSeq AS NVARCHAR(19)))
                CONSTRAINT UQ_Loans_Number UNIQUE,
    ReaderId    BIGINT        NOT NULL
                CONSTRAINT FK_Loans_Readers REFERENCES dbo.Readers(Id),      -- 1:N; без каскаду: читача з історією не видаляємо
    Status      NVARCHAR(30)  NOT NULL CONSTRAINT DF_Loans_Status DEFAULT N'Submitted'
                CONSTRAINT CK_Loans_Status CHECK (Status IN (N'Submitted', N'AwaitingValidation', N'Reserved',
                                                             N'Paid', N'Issued', N'Returned', N'Cancelled')),
    LoanDate    DATETIME2(3)  NOT NULL CONSTRAINT DF_Loans_LoanDate DEFAULT SYSUTCDATETIME(),
    DueDate     DATE          NULL,                                          -- до якої дати повернути
    CreatedAt   DATETIME2(3)  NOT NULL CONSTRAINT DF_Loans_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy   NVARCHAR(100) NULL,
    UpdatedAt   DATETIME2(3)  NULL,
    UpdatedBy   NVARCHAR(100) NULL,
    IsDeleted   BIT           NOT NULL CONSTRAINT DF_Loans_IsDeleted DEFAULT 0,
    RowVersion  ROWVERSION    NOT NULL,                                      -- два бібліотекарі не затруть зміни один одного
    CONSTRAINT CK_Loans_DueDate CHECK (DueDate IS NULL OR DueDate >= CAST(LoanDate AS DATE))
);
GO

/* LoanDetails — 1:1 з Loans: PK водночас є FK; каскадне видалення разом із видачею */
IF OBJECT_ID(N'dbo.LoanDetails', N'U') IS NULL
CREATE TABLE dbo.LoanDetails (
    LoanId          BIGINT         NOT NULL CONSTRAINT PK_LoanDetails PRIMARY KEY
                    CONSTRAINT FK_LoanDetails_Loans REFERENCES dbo.Loans(Id) ON DELETE CASCADE,
    DeliveryType    NVARCHAR(20)   NOT NULL CONSTRAINT DF_LoanDetails_DeliveryType DEFAULT N'Pickup'
                    CONSTRAINT CK_LoanDetails_DeliveryType CHECK (DeliveryType IN (N'Pickup', N'Courier')),
    DeliveryAddress NVARCHAR(400)  NULL,
    Comment         NVARCHAR(1000) NULL,
    CONSTRAINT CK_LoanDetails_Address CHECK (DeliveryType = N'Pickup' OR DeliveryAddress IS NOT NULL)
);
GO

/* LoanItems — M:N Loans <-> Books; власні поля: знімок назви й ціни, знижка, кількість примірників */
IF OBJECT_ID(N'dbo.LoanItems', N'U') IS NULL
CREATE TABLE dbo.LoanItems (
    LoanId     BIGINT        NOT NULL CONSTRAINT FK_LoanItems_Loans REFERENCES dbo.Loans(Id) ON DELETE CASCADE,
    BookId     BIGINT        NOT NULL CONSTRAINT FK_LoanItems_Books REFERENCES dbo.Books(Id),  -- FK на ЛОКАЛЬНУ таблицю
    BookTitle  NVARCHAR(300) NOT NULL,                       -- знімок на момент оформлення (не оновлюється)
    UnitPrice  DECIMAL(18,2) NOT NULL CONSTRAINT CK_LoanItems_UnitPrice CHECK (UnitPrice > 0),   -- знімок ціни
    Discount   DECIMAL(18,2) NOT NULL CONSTRAINT DF_LoanItems_Discount DEFAULT 0,             -- знижка на 1 примірник
    Quantity   INT           NOT NULL CONSTRAINT CK_LoanItems_Quantity CHECK (Quantity BETWEEN 1 AND 50),
    CONSTRAINT PK_LoanItems PRIMARY KEY (LoanId, BookId),
    CONSTRAINT CK_LoanItems_Discount CHECK (Discount >= 0 AND Discount <= UnitPrice)
);
GO

/* LoanStatusHistory — 1:N журнал переходів статусу (пише процедура зміни статусу) */
IF OBJECT_ID(N'dbo.LoanStatusHistory', N'U') IS NULL
CREATE TABLE dbo.LoanStatusHistory (
    Id         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoanStatusHistory PRIMARY KEY,
    LoanId     BIGINT        NOT NULL CONSTRAINT FK_LoanStatusHistory_Loans REFERENCES dbo.Loans(Id) ON DELETE CASCADE,
    FromStatus NVARCHAR(30)  NULL,
    ToStatus   NVARCHAR(30)  NOT NULL,
    ChangedAt  DATETIME2(3)  NOT NULL CONSTRAINT DF_LoanStatusHistory_ChangedAt DEFAULT SYSUTCDATETIME(),
    ChangedBy  NVARCHAR(100) NULL
);
GO

/* Payments — 1:N оплати видачі; без каскаду: фінансові записи не видаляються разом із видачею */
IF OBJECT_ID(N'dbo.Payments', N'U') IS NULL
CREATE TABLE dbo.Payments (
    Id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Payments PRIMARY KEY,
    LoanId            BIGINT        NOT NULL CONSTRAINT FK_Payments_Loans REFERENCES dbo.Loans(Id),
    Amount            DECIMAL(18,2) NOT NULL CONSTRAINT CK_Payments_Amount CHECK (Amount > 0),
    Method            NVARCHAR(20)  NOT NULL CONSTRAINT CK_Payments_Method CHECK (Method IN (N'Card', N'Cash', N'Online')),
    ExternalReference NVARCHAR(100) NULL,            -- ідентифікатор платіжної системи / idempotency key
    PaidAt            DATETIME2(3)  NOT NULL CONSTRAINT DF_Payments_PaidAt DEFAULT SYSUTCDATETIME(),
    CreatedBy         NVARCHAR(100) NULL
);
GO

/* Табличний тип для передачі позицій видачі в процедуру usp_CreateLoan (TVP) */
IF TYPE_ID(N'dbo.LoanItemList') IS NULL
    CREATE TYPE dbo.LoanItemList AS TABLE (
        BookId   BIGINT NOT NULL PRIMARY KEY,
        Quantity INT    NOT NULL CHECK (Quantity > 0)
    );
GO

/* =====================================================================
   ІНДЕКСИ — кожен створено під конкретний запит (без «сліпого» індексування)
   ===================================================================== */

-- Q1. Кабінет читача: «мої видачі з певним статусом, нові згори»
--     SELECT ... FROM Loans WHERE ReaderId = @ReaderId AND Status = @Status AND IsDeleted = 0 ORDER BY LoanDate DESC
--     Фільтрований (лише живі записи) і покриваючий (INCLUDE) індекс.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Loans_ReaderId_Status' AND object_id = OBJECT_ID(N'dbo.Loans'))
    CREATE INDEX IX_Loans_ReaderId_Status ON dbo.Loans (ReaderId, Status, LoanDate DESC)
        INCLUDE (Number, DueDate) WHERE IsDeleted = 0;
GO

-- Q2. Черга бібліотекаря: «видачі в статусі X від найстаріших» з пагінацією OFFSET/FETCH
--     SELECT ... FROM Loans WHERE Status = @Status AND IsDeleted = 0 ORDER BY LoanDate OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Loans_Status_LoanDate' AND object_id = OBJECT_ID(N'dbo.Loans'))
    CREATE INDEX IX_Loans_Status_LoanDate ON dbo.Loans (Status, LoanDate)
        INCLUDE (ReaderId, Number) WHERE IsDeleted = 0;
GO

-- Q3. Прострочені видачі: «видані книги, у яких DueDate < сьогодні»
--     SELECT ... FROM Loans WHERE Status = 'Issued' AND IsDeleted = 0 AND DueDate < CAST(SYSUTCDATETIME() AS DATE)
--     Фільтрований індекс містить лише видані книги — він дуже малий.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Loans_Overdue' AND object_id = OBJECT_ID(N'dbo.Loans'))
    CREATE INDEX IX_Loans_Overdue ON dbo.Loans (DueDate)
        INCLUDE (ReaderId, Number) WHERE Status = N'Issued' AND IsDeleted = 0;
GO

-- Q4. «У яких видачах є книга / скільки примірників книги видано» + підтримка FK при видаленні книги
--     SELECT LoanId, Quantity FROM LoanItems WHERE BookId = @BookId
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LoanItems_BookId' AND object_id = OBJECT_ID(N'dbo.LoanItems'))
    CREATE INDEX IX_LoanItems_BookId ON dbo.LoanItems (BookId) INCLUDE (Quantity);
GO

-- Q5. Історія статусів видачі в хронологічному порядку
--     SELECT ... FROM LoanStatusHistory WHERE LoanId = @LoanId ORDER BY ChangedAt
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LoanStatusHistory_LoanId' AND object_id = OBJECT_ID(N'dbo.LoanStatusHistory'))
    CREATE INDEX IX_LoanStatusHistory_LoanId ON dbo.LoanStatusHistory (LoanId, ChangedAt);
GO

-- Q6. Платежі видачі (картка видачі, звіт про оплату)
--     SELECT Amount, PaidAt FROM Payments WHERE LoanId = @LoanId
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Payments_LoanId' AND object_id = OBJECT_ID(N'dbo.Payments'))
    CREATE INDEX IX_Payments_LoanId ON dbo.Payments (LoanId) INCLUDE (Amount, PaidAt);
GO

-- Q7. Ідемпотентність оплати: «чи вже зареєстровано платіж з цим зовнішнім ідентифікатором»
--     SELECT Id FROM Payments WHERE ExternalReference = @Ref   (унікальний фільтрований індекс: NULL дозволено багато разів)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Payments_ExternalReference' AND object_id = OBJECT_ID(N'dbo.Payments'))
    CREATE UNIQUE INDEX UX_Payments_ExternalReference ON dbo.Payments (ExternalReference)
        WHERE ExternalReference IS NOT NULL;
GO

-- Q8. Пошук активного читача за email (вхід, перевірка дубліката при реєстрації)
--     SELECT ... FROM Readers WHERE Email = @Email AND IsDeleted = 0
--     Унікальний фільтрований індекс: email видаленого читача можна використати повторно.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Readers_Email_Active' AND object_id = OBJECT_ID(N'dbo.Readers'))
    CREATE UNIQUE INDEX UX_Readers_Email_Active ON dbo.Readers (Email) WHERE IsDeleted = 0;
GO

-- Q9. Автодоповнення назви книги під час оформлення видачі
--     SELECT Id, Title, Price, AvailableCopies FROM Books WHERE Title LIKE @Prefix + '%' AND IsDeleted = 0
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Books_Title' AND object_id = OBJECT_ID(N'dbo.Books'))
    CREATE INDEX IX_Books_Title ON dbo.Books (Title) INCLUDE (Price, AvailableCopies) WHERE IsDeleted = 0;
GO

/* =====================================================================
   ПРЕДСТАВЛЕННЯ для читання (денормалізація без зміни нормалізованої схеми)
   ===================================================================== */
CREATE OR ALTER VIEW dbo.vw_LoanSummary
AS
SELECT  l.Id,
        l.Number,
        l.ReaderId,
        r.FullName                                              AS ReaderName,
        l.Status,
        l.LoanDate,
        l.DueDate,
        COUNT(i.BookId)                                         AS ItemsCount,
        COALESCE(SUM(i.Quantity * (i.UnitPrice - i.Discount)), 0) AS TotalAmount
FROM dbo.Loans l
JOIN dbo.Readers r        ON r.Id = l.ReaderId
LEFT JOIN dbo.LoanItems i ON i.LoanId = l.Id
WHERE l.IsDeleted = 0
GROUP BY l.Id, l.Number, l.ReaderId, r.FullName, l.Status, l.LoanDate, l.DueDate;
GO
