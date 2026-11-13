/* =====================================================================
   Проєкт №2 — BooksDb: довідникові та тестові дані (ідемпотентно).
   Порядок дотримується цілісності FK: довідники (Genres, Authors) ->
   Books -> залежні записи (BookDetails, BookImages, BookGenres).
   Id книг 1..8 збігаються з локальною копією Books у LoansDb.
   ===================================================================== */
USE BooksDb;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

/* ---- Довідник жанрів (фіксовані Id) ---- */
INSERT INTO dbo.Genres (Id, Name, Slug)
SELECT v.Id, v.Name, v.Slug
FROM (VALUES
    (1, N'Класика',              N'classic'),
    (2, N'Пригоди',              N'adventure'),
    (3, N'Роман',                N'novel'),
    (4, N'Програмування',        N'programming'),
    (5, N'Архітектура ПЗ',       N'software-architecture'),
    (6, N'Поезія',               N'poetry')
) AS v(Id, Name, Slug)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Genres g WHERE g.Id = v.Id);
GO

/* ---- Автори ---- */
SET IDENTITY_INSERT dbo.Authors ON;
INSERT INTO dbo.Authors (Id, FullName, Country, BirthYear)
SELECT v.Id, v.FullName, v.Country, v.BirthYear
FROM (VALUES
    (1, N'Тарас Шевченко',     N'Україна', 1814),
    (2, N'Іван Багряний',      N'Україна', 1906),
    (3, N'Валер''ян Підмогильний', N'Україна', 1901),
    (4, N'Robert C. Martin',   N'USA',     1952),
    (5, N'Eric Evans',         N'USA',     NULL),
    (6, N'Martin Kleppmann',   N'Germany', NULL),
    (7, N'Леся Українка',      N'Україна', 1871),
    (8, N'Михайло Коцюбинський', N'Україна', 1864)
) AS v(Id, FullName, Country, BirthYear)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Authors a WHERE a.Id = v.Id);
SET IDENTITY_INSERT dbo.Authors OFF;
GO

/* ---- Книги ---- */
SET IDENTITY_INSERT dbo.Books ON;
INSERT INTO dbo.Books (Id, Title, Isbn, Price, TotalCopies, PublishedYear, IsPublished, AuthorId)
SELECT v.Id, v.Title, v.Isbn, v.Price, v.Copies, v.PublishedYear, v.IsPublished, v.AuthorId
FROM (VALUES
    (1, N'Кобзар',                                N'978-966-03-4000-1', 25.00, 6, 1840, 1, 1),
    (2, N'Тигролови',                             N'978-966-03-4000-2', 30.00, 4, 1944, 1, 2),
    (3, N'Місто',                                 N'978-966-03-4000-3', 28.50, 6, 1928, 1, 3),
    (4, N'Clean Architecture',                    N'978-0-13-449416-6', 60.00, 3, 2017, 1, 4),
    (5, N'Domain-Driven Design',                  N'978-0-321-12521-7', 65.00, 3, 2003, 1, 5),
    (6, N'Designing Data-Intensive Applications', N'978-1-4493-7332-0', 70.00, 4, 2017, 1, 6),
    (7, N'Лісова пісня',                          N'978-966-03-4000-7', 20.00, 7, 1911, 1, 7),
    (8, N'Intermezzo',                            N'978-966-03-4000-8', 15.00, 2, 1908, 0, 8)   -- ще не опублікована
) AS v(Id, Title, Isbn, Price, Copies, PublishedYear, IsPublished, AuthorId)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Books b WHERE b.Id = v.Id);
SET IDENTITY_INSERT dbo.Books OFF;
GO

/* ---- 1:1 деталі ---- */
INSERT INTO dbo.BookDetails (BookId, Description, Publisher, Pages, Language)
SELECT v.BookId, v.Description, v.Publisher, v.Pages, v.Language
FROM (VALUES
    (1, N'Збірка поетичних творів Тараса Шевченка.',              N'Дніпро',        624, N'uk'),
    (2, N'Пригодницький роман про родину Сірків на Далекому Сході.', N'Фоліо',        352, N'uk'),
    (3, N'Урбаністичний роман про Київ 1920-х років.',            N'Фоліо',         288, N'uk'),
    (4, N'A Craftsman''s Guide to Software Structure and Design.', N'Prentice Hall', 432, N'en'),
    (5, N'Tackling Complexity in the Heart of Software.',         N'Addison-Wesley', 560, N'en'),
    (6, N'The Big Ideas Behind Reliable, Scalable, and Maintainable Systems.', N'O''Reilly', 616, N'en'),
    (7, N'Драма-феєрія у трьох діях.',                            N'Знання',        192, N'uk')
) AS v(BookId, Description, Publisher, Pages, Language)
WHERE NOT EXISTS (SELECT 1 FROM dbo.BookDetails d WHERE d.BookId = v.BookId);
GO

/* ---- 1:N зображення ---- */
INSERT INTO dbo.BookImages (BookId, Url, SortOrder)
SELECT v.BookId, v.Url, v.SortOrder
FROM (VALUES
    (1, N'https://cdn.library.example/books/1/cover.jpg', 0),
    (1, N'https://cdn.library.example/books/1/page-1.jpg', 1),
    (2, N'https://cdn.library.example/books/2/cover.jpg', 0),
    (4, N'https://cdn.library.example/books/4/cover.jpg', 0),
    (6, N'https://cdn.library.example/books/6/cover.jpg', 0)
) AS v(BookId, Url, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.BookImages i WHERE i.BookId = v.BookId AND i.SortOrder = v.SortOrder);
GO

/* ---- M:N книги <-> жанри (власне поле AddedAt) ---- */
INSERT INTO dbo.BookGenres (BookId, GenreId, AddedAt)
SELECT v.BookId, v.GenreId, v.AddedAt
FROM (VALUES
    (1, 1, CAST('2026-01-10T10:00:00' AS DATETIME2(3))),
    (1, 6, CAST('2026-01-10T10:00:00' AS DATETIME2(3))),
    (2, 2, CAST('2026-02-01T10:00:00' AS DATETIME2(3))),
    (2, 3, CAST('2026-02-01T10:00:00' AS DATETIME2(3))),
    (3, 3, CAST('2026-03-15T10:00:00' AS DATETIME2(3))),
    (3, 1, CAST('2026-03-15T10:00:00' AS DATETIME2(3))),
    (4, 4, CAST('2026-09-01T10:00:00' AS DATETIME2(3))),
    (4, 5, CAST('2026-09-01T10:00:00' AS DATETIME2(3))),
    (5, 5, CAST('2026-09-20T10:00:00' AS DATETIME2(3))),
    (6, 4, CAST('2026-10-05T10:00:00' AS DATETIME2(3))),
    (6, 5, CAST('2026-10-05T10:00:00' AS DATETIME2(3))),
    (7, 1, CAST('2026-04-01T10:00:00' AS DATETIME2(3))),
    (7, 6, CAST('2026-04-01T10:00:00' AS DATETIME2(3))),
    (8, 1, CAST('2026-10-30T10:00:00' AS DATETIME2(3)))
) AS v(BookId, GenreId, AddedAt)
WHERE NOT EXISTS (SELECT 1 FROM dbo.BookGenres bg WHERE bg.BookId = v.BookId AND bg.GenreId = v.GenreId);
GO

PRINT 'BooksDb seed completed';
GO
