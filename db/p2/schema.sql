/* =====================================================================
   Проєкт №2 — BooksDb (контекст «Каталог книг», SQL Server + EF Core Code First)
   Цей DDL — результат проєктування схеми (Лаб. 01). У Лаб. 03 та сама схема
   описується класами-сутностями й Fluent API, а база створюється міграціями EF Core
   (тому структура тут уже «готова до міграцій»: усі ключі/обмеження мають імена,
   довідник Genres має фіксовані Id для HasData).

   6 сутностей і всі типи зв'язків:
     1:1  Books   <-> BookDetails  (PK BookDetails = FK на Books)
     1:N  Authors -> Books,  Books -> BookImages
     M:N  Books   <-> Genres через BookGenres (проміжна сутність із власним полем AddedAt)
   Скрипт ідемпотентний.
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_ID(N'BooksDb') IS NULL
    CREATE DATABASE BooksDb;
GO

USE BooksDb;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* Authors — автори (1:N до Books). PK INT IDENTITY: каталог — єдине джерело
   книг і авторів, ідентифікатори генеруються послідовно в одному місці. */
IF OBJECT_ID(N'dbo.Authors', N'U') IS NULL
CREATE TABLE dbo.Authors (
    Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Authors PRIMARY KEY,
    FullName  NVARCHAR(200) NOT NULL,
    Country   NVARCHAR(100) NULL,
    BirthYear SMALLINT      NULL CONSTRAINT CK_Authors_BirthYear CHECK (BirthYear BETWEEN 1000 AND 2100)
);
GO

/* Genres — довідник жанрів із фіксованими Id (статичний сідінг HasData у Лаб. 03) */
IF OBJECT_ID(N'dbo.Genres', N'U') IS NULL
CREATE TABLE dbo.Genres (
    Id   INT           NOT NULL CONSTRAINT PK_Genres PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL CONSTRAINT UQ_Genres_Name UNIQUE,
    Slug NVARCHAR(100) NOT NULL CONSTRAINT UQ_Genres_Slug UNIQUE,
    CONSTRAINT CK_Genres_Slug CHECK (Slug NOT LIKE N'% %')
);
GO

/* Books — книги каталогу (аналог CatalogItem в eShop).
   Price — плата за видачу; TotalCopies — скільки примірників має фонд бібліотеки
   (змінюється лише методами сутності AddCopies / WriteOffCopies у Лаб. 03). */
IF OBJECT_ID(N'dbo.Books', N'U') IS NULL
CREATE TABLE dbo.Books (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Books PRIMARY KEY,
    Title         NVARCHAR(300) NOT NULL,
    Isbn          NVARCHAR(20)  NOT NULL,
    Price         DECIMAL(18,2) NOT NULL CONSTRAINT CK_Books_Price CHECK (Price > 0),
    TotalCopies   INT           NOT NULL CONSTRAINT DF_Books_TotalCopies DEFAULT 0
                                         CONSTRAINT CK_Books_TotalCopies CHECK (TotalCopies >= 0),
    PublishedYear SMALLINT      NULL CONSTRAINT CK_Books_PublishedYear CHECK (PublishedYear BETWEEN 1450 AND 2100),
    IsPublished   BIT           NOT NULL CONSTRAINT DF_Books_IsPublished DEFAULT 0,   -- видно читачам після публікації
    AuthorId      INT           NOT NULL CONSTRAINT FK_Books_Authors_AuthorId REFERENCES dbo.Authors(Id),  -- 1:N, RESTRICT
    CreatedAt     DATETIME2(3)  NOT NULL CONSTRAINT DF_Books_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt     DATETIME2(3)  NULL,
    RowVersion    ROWVERSION    NOT NULL
);
GO

/* BookDetails — 1:1: розширений опис, який потрібен лише на сторінці книги */
IF OBJECT_ID(N'dbo.BookDetails', N'U') IS NULL
CREATE TABLE dbo.BookDetails (
    BookId      INT            NOT NULL CONSTRAINT PK_BookDetails PRIMARY KEY
                CONSTRAINT FK_BookDetails_Books_BookId REFERENCES dbo.Books(Id) ON DELETE CASCADE,
    Description NVARCHAR(4000) NOT NULL,
    Publisher   NVARCHAR(200)  NULL,
    Pages       INT            NULL CONSTRAINT CK_BookDetails_Pages CHECK (Pages > 0),
    Language    NVARCHAR(10)   NOT NULL CONSTRAINT DF_BookDetails_Language DEFAULT N'uk'
);
GO

/* BookImages — 1:N обкладинки та сторінки-прев'ю */
IF OBJECT_ID(N'dbo.BookImages', N'U') IS NULL
CREATE TABLE dbo.BookImages (
    Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BookImages PRIMARY KEY,
    BookId    INT           NOT NULL CONSTRAINT FK_BookImages_Books_BookId REFERENCES dbo.Books(Id) ON DELETE CASCADE,
    Url       NVARCHAR(500) NOT NULL,
    SortOrder INT           NOT NULL CONSTRAINT DF_BookImages_SortOrder DEFAULT 0
                                     CONSTRAINT CK_BookImages_SortOrder CHECK (SortOrder >= 0)
);
GO

/* BookGenres — M:N Books <-> Genres; власне поле AddedAt (коли книгу додано в жанрову добірку) */
IF OBJECT_ID(N'dbo.BookGenres', N'U') IS NULL
CREATE TABLE dbo.BookGenres (
    BookId  INT          NOT NULL CONSTRAINT FK_BookGenres_Books_BookId  REFERENCES dbo.Books(Id)  ON DELETE CASCADE,
    GenreId INT          NOT NULL CONSTRAINT FK_BookGenres_Genres_GenreId REFERENCES dbo.Genres(Id) ON DELETE CASCADE,
    AddedAt DATETIME2(3) NOT NULL CONSTRAINT DF_BookGenres_AddedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_BookGenres PRIMARY KEY (BookId, GenreId)
);
GO

/* =====================================================================
   ІНДЕКСИ під конкретні запити
   ===================================================================== */
-- Q1. Унікальність ISBN + пошук книги за ISBN (перевірка дубліката під час створення — 409 у Лаб. 03)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Books_Isbn' AND object_id = OBJECT_ID(N'dbo.Books'))
    CREATE UNIQUE INDEX IX_Books_Isbn ON dbo.Books (Isbn);
GO
-- Q2. Пошук і сортування за назвою: WHERE Title LIKE @Search + '%' ORDER BY Title
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Books_Title' AND object_id = OBJECT_ID(N'dbo.Books'))
    CREATE INDEX IX_Books_Title ON dbo.Books (Title);
GO
-- Q3. Фільтр за діапазоном ціни з сортуванням за ціною: WHERE Price BETWEEN @Min AND @Max ORDER BY Price
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Books_Price' AND object_id = OBJECT_ID(N'dbo.Books'))
    CREATE INDEX IX_Books_Price ON dbo.Books (Price);
GO
-- Q4. Книги автора (+ підтримка FK): WHERE AuthorId = @AuthorId
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Books_AuthorId' AND object_id = OBJECT_ID(N'dbo.Books'))
    CREATE INDEX IX_Books_AuthorId ON dbo.Books (AuthorId);
GO
-- Q5. «Нещодавно додані в жанр»: WHERE GenreId = @GenreId AND AddedAt >= @Since ORDER BY AddedAt DESC
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BookGenres_GenreId_AddedAt' AND object_id = OBJECT_ID(N'dbo.BookGenres'))
    CREATE INDEX IX_BookGenres_GenreId_AddedAt ON dbo.BookGenres (GenreId, AddedAt DESC);
GO
-- Q6. Галерея книги в заданому порядку + унікальна позиція зображення
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BookImages_BookId_SortOrder' AND object_id = OBJECT_ID(N'dbo.BookImages'))
    CREATE UNIQUE INDEX IX_BookImages_BookId_SortOrder ON dbo.BookImages (BookId, SortOrder);
GO
-- Q7. Пошук автора за ім'ям (фільтр каталогу «книги автора X»)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Authors_FullName' AND object_id = OBJECT_ID(N'dbo.Authors'))
    CREATE INDEX IX_Authors_FullName ON dbo.Authors (FullName);
GO
