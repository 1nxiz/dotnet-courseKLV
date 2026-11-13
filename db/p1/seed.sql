/* =====================================================================
   Проєкт №1 — LoansDb: тестові дані.
   ІДЕМПОТЕНТНО: кожна вставка перевіряє наявність запису за природним
   ключем (CardNumber, Id книги, Number видачі, ExternalReference платежу),
   тому повторний запуск не створює дублікатів.
   ===================================================================== */
USE LoansDb;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

/* ---- Читачі ---- */
INSERT INTO dbo.Readers (Email, FullName, Phone, CardNumber, CreatedBy)
SELECT v.Email, v.FullName, v.Phone, v.CardNumber, N'seed'
FROM (VALUES
    (N'olena.kovalenko@example.com', N'Олена Коваленко', N'+380501112233', N'LIB-0001'),
    (N'andrii.shevchenko@example.com', N'Андрій Шевченко', N'+380671234567', N'LIB-0002'),
    (N'iryna.bondar@example.com',     N'Ірина Бондар',     NULL,            N'LIB-0003'),
    (N'taras.melnyk@example.com',     N'Тарас Мельник',    N'+380931110000', N'LIB-0004'),
    (N'sean.obrien@example.com',      N'Sean O''Brien',    NULL,            N'LIB-0005')   -- апостроф для перевірки параметризації (Лаб. 02)
) AS v(Email, FullName, Phone, CardNumber)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Readers r WHERE r.CardNumber = v.CardNumber);
GO

/* ---- Локальна копія довідника книг (Id збігаються з BooksDb, Проєкт №2) ---- */
INSERT INTO dbo.Books (Id, Title, Isbn, Price, AvailableCopies, SourceUpdatedAt, CreatedBy)
SELECT v.Id, v.Title, v.Isbn, v.Price, v.Copies, '2026-09-01T00:00:00', N'seed'
FROM (VALUES
    (1, N'Кобзар',                          N'978-966-03-4000-1', 25.00, 5),
    (2, N'Тигролови',                       N'978-966-03-4000-2', 30.00, 3),
    (3, N'Місто',                           N'978-966-03-4000-3', 28.50, 4),
    (4, N'Clean Architecture',              N'978-0-13-449416-6', 60.00, 2),
    (5, N'Domain-Driven Design',            N'978-0-321-12521-7', 65.00, 2),
    (6, N'Designing Data-Intensive Applications', N'978-1-4493-7332-0', 70.00, 3),
    (7, N'Лісова пісня',                    N'978-966-03-4000-7', 20.00, 6),
    (8, N'Intermezzo',                      N'978-966-03-4000-8', 15.00, 0)          -- немає на полиці
) AS v(Id, Title, Isbn, Price, Copies)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Books b WHERE b.Id = v.Id);
GO

/* ---- Видачі в різних статусах (Number — природний ключ для ідемпотентності) ---- */
INSERT INTO dbo.Loans (Number, ReaderId, Status, LoanDate, DueDate, CreatedBy)
SELECT v.Number, r.Id, v.Status, v.LoanDate, v.DueDate, N'seed'
FROM (VALUES
    (N'LN-SEED-001', N'LIB-0001', N'Submitted', CAST('2026-10-01T09:00:00' AS DATETIME2(3)), CAST('2026-10-15' AS DATE)),
    (N'LN-SEED-002', N'LIB-0002', N'Reserved',  CAST('2026-10-02T10:00:00' AS DATETIME2(3)), CAST('2026-10-16' AS DATE)),
    (N'LN-SEED-003', N'LIB-0003', N'Paid',      CAST('2026-10-03T11:00:00' AS DATETIME2(3)), CAST('2026-10-17' AS DATE)),
    (N'LN-SEED-004', N'LIB-0001', N'Issued',    CAST('2026-09-01T12:00:00' AS DATETIME2(3)), CAST('2026-09-15' AS DATE)),  -- прострочена
    (N'LN-SEED-005', N'LIB-0004', N'Returned',  CAST('2026-08-10T13:00:00' AS DATETIME2(3)), CAST('2026-08-24' AS DATE)),
    (N'LN-SEED-006', N'LIB-0005', N'Cancelled', CAST('2026-10-05T14:00:00' AS DATETIME2(3)), NULL)
) AS v(Number, CardNumber, Status, LoanDate, DueDate)
JOIN dbo.Readers r ON r.CardNumber = v.CardNumber
WHERE NOT EXISTS (SELECT 1 FROM dbo.Loans l WHERE l.Number = v.Number);
GO

/* ---- 1:1 деталі видачі ---- */
INSERT INTO dbo.LoanDetails (LoanId, DeliveryType, DeliveryAddress, Comment)
SELECT l.Id, v.DeliveryType, v.Address, v.Comment
FROM (VALUES
    (N'LN-SEED-001', N'Pickup',  NULL,                                  N'Заберу після 18:00'),
    (N'LN-SEED-002', N'Courier', N'Київ, вул. Хрещатик, 1, кв. 5',     NULL),
    (N'LN-SEED-003', N'Pickup',  NULL,                                  NULL),
    (N'LN-SEED-004', N'Courier', N'Львів, пл. Ринок, 10',               N'Подзвонити заздалегідь'),
    (N'LN-SEED-005', N'Pickup',  NULL,                                  NULL),
    (N'LN-SEED-006', N'Pickup',  NULL,                                  N'Скасовано читачем')
) AS v(Number, DeliveryType, Address, Comment)
JOIN dbo.Loans l ON l.Number = v.Number
WHERE NOT EXISTS (SELECT 1 FROM dbo.LoanDetails d WHERE d.LoanId = l.Id);
GO

/* ---- M:N позиції (BookTitle / UnitPrice — знімок із Books) ---- */
INSERT INTO dbo.LoanItems (LoanId, BookId, BookTitle, UnitPrice, Discount, Quantity)
SELECT l.Id, b.Id, b.Title, b.Price, v.Discount, v.Quantity
FROM (VALUES
    (N'LN-SEED-001', 1, 0.00, 1),
    (N'LN-SEED-001', 2, 0.00, 1),
    (N'LN-SEED-002', 4, 5.00, 1),
    (N'LN-SEED-003', 6, 0.00, 1),
    (N'LN-SEED-004', 3, 0.00, 2),
    (N'LN-SEED-005', 7, 0.00, 1),
    (N'LN-SEED-006', 5, 0.00, 1)
) AS v(Number, BookId, Discount, Quantity)
JOIN dbo.Loans l ON l.Number = v.Number
JOIN dbo.Books b ON b.Id = v.BookId
WHERE NOT EXISTS (SELECT 1 FROM dbo.LoanItems i WHERE i.LoanId = l.Id AND i.BookId = v.BookId);
GO

/* ---- 1:N історія статусів ---- */
INSERT INTO dbo.LoanStatusHistory (LoanId, FromStatus, ToStatus, ChangedAt, ChangedBy)
SELECT l.Id, v.FromStatus, v.ToStatus, DATEADD(MINUTE, v.MinutesAfter, l.LoanDate), N'seed'
FROM (VALUES
    (N'LN-SEED-001', NULL,          N'Submitted', 0),
    (N'LN-SEED-002', NULL,          N'Submitted', 0),
    (N'LN-SEED-002', N'Submitted',  N'Reserved',  30),
    (N'LN-SEED-003', NULL,          N'Submitted', 0),
    (N'LN-SEED-003', N'Submitted',  N'Reserved',  30),
    (N'LN-SEED-003', N'Reserved',   N'Paid',      60),
    (N'LN-SEED-004', NULL,          N'Submitted', 0),
    (N'LN-SEED-004', N'Submitted',  N'Reserved',  30),
    (N'LN-SEED-004', N'Reserved',   N'Paid',      60),
    (N'LN-SEED-004', N'Paid',       N'Issued',    120),
    (N'LN-SEED-005', NULL,          N'Submitted', 0),
    (N'LN-SEED-005', N'Submitted',  N'Reserved',  30),
    (N'LN-SEED-005', N'Reserved',   N'Paid',      60),
    (N'LN-SEED-005', N'Paid',       N'Issued',    120),
    (N'LN-SEED-005', N'Issued',     N'Returned',  20000),
    (N'LN-SEED-006', NULL,          N'Submitted', 0),
    (N'LN-SEED-006', N'Submitted',  N'Cancelled', 15)
) AS v(Number, FromStatus, ToStatus, MinutesAfter)
JOIN dbo.Loans l ON l.Number = v.Number
WHERE NOT EXISTS (SELECT 1 FROM dbo.LoanStatusHistory h WHERE h.LoanId = l.Id AND h.ToStatus = v.ToStatus);
GO

/* ---- 1:N платежі (ExternalReference — природний ключ) ---- */
INSERT INTO dbo.Payments (LoanId, Amount, Method, ExternalReference, PaidAt, CreatedBy)
SELECT l.Id, t.Total, v.Method, v.ExternalReference, DATEADD(HOUR, 1, l.LoanDate), N'seed'
FROM (VALUES
    (N'LN-SEED-003', N'Card',   N'PAY-SEED-003'),
    (N'LN-SEED-004', N'Online', N'PAY-SEED-004'),
    (N'LN-SEED-005', N'Cash',   N'PAY-SEED-005')
) AS v(Number, Method, ExternalReference)
JOIN dbo.Loans l ON l.Number = v.Number
CROSS APPLY (SELECT SUM(i.Quantity * (i.UnitPrice - i.Discount)) AS Total
             FROM dbo.LoanItems i WHERE i.LoanId = l.Id) t
WHERE NOT EXISTS (SELECT 1 FROM dbo.Payments p WHERE p.ExternalReference = v.ExternalReference);
GO

PRINT 'LoansDb seed completed';
GO
