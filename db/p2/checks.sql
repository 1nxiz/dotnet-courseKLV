/* Проєкт №2 — BooksDb: запити перевірки (схема, M:N з власним полем, пошук, пагінація, обмеження) */
USE BooksDb;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO
PRINT '1) Зовнішні ключі — лише всередині BooksDb';
SELECT fk.name, OBJECT_NAME(fk.parent_object_id) AS FromTable, OBJECT_NAME(fk.referenced_object_id) AS ToTable,
       fk.delete_referential_action_desc AS OnDelete
FROM sys.foreign_keys fk ORDER BY fk.name;
GO
PRINT '2) M:N через проміжну сутність і її власне поле AddedAt: «нещодавно додані в жанр Архітектура ПЗ»';
SELECT b.Id, b.Title, bg.AddedAt
FROM dbo.BookGenres bg
JOIN dbo.Genres g ON g.Id = bg.GenreId
JOIN dbo.Books  b ON b.Id = bg.BookId
WHERE g.Slug = N'software-architecture' AND bg.AddedAt >= '2026-09-01'
ORDER BY bg.AddedAt DESC;
GO
PRINT '3) Пошук + фільтр ціни + пагінація (page=2, pageSize=3) і загальна кількість';
DECLARE @Page INT = 2, @PageSize INT = 3;
SELECT COUNT(*) AS TotalCount FROM dbo.Books WHERE Price BETWEEN 10 AND 100;
SELECT Id, Title, Price FROM dbo.Books
WHERE Price BETWEEN 10 AND 100
ORDER BY Title
OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
GO
PRINT '4) Картка книги: 1:1 деталі, 1:N зображення, N:1 автор';
SELECT b.Title, a.FullName AS Author, d.Publisher, d.Pages,
       (SELECT COUNT(*) FROM dbo.BookImages i WHERE i.BookId = b.Id) AS Images
FROM dbo.Books b
JOIN dbo.Authors a ON a.Id = b.AuthorId
LEFT JOIN dbo.BookDetails d ON d.BookId = b.Id
WHERE b.Id = 1;
GO
PRINT '5) Обмеження: ціна > 0 і унікальний ISBN';
DECLARE @E1 INT = 0, @E2 INT = 0;
BEGIN TRANSACTION;
BEGIN TRY INSERT INTO dbo.Books (Title, Isbn, Price, AuthorId) VALUES (N'Bad', N'X-1', 0, 1); END TRY
BEGIN CATCH SET @E1 = ERROR_NUMBER(); END CATCH;
BEGIN TRY INSERT INTO dbo.Books (Title, Isbn, Price, AuthorId) VALUES (N'Dup', N'978-966-03-4000-1', 10, 1); END TRY
BEGIN CATCH SET @E2 = ERROR_NUMBER(); END CATCH;
IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
IF @E1 <> 547 THROW 60000, 'CHECK Price > 0 must fail', 1;
IF @E2 NOT IN (2601, 2627) THROW 60000, 'Unique ISBN must fail', 1;
PRINT '   OK';
GO
PRINT 'BooksDb checks passed';
GO
