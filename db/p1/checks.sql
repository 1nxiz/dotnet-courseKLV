/* =====================================================================
   Проєкт №1 — LoansDb: запити перевірки (CRUD, пошук, пагінація,
   транзакційні сценарії, обмеження). Усі зміни виконуються в транзакції
   й відкочуються наприкінці — скрипт можна запускати скільки завгодно разів.
   Будь-яка невдала перевірка зупиняє скрипт помилкою 60000.
   ===================================================================== */
USE LoansDb;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

PRINT '1) У базі немає зовнішніх ключів на інші бази (усі FK посилаються на таблиці LoansDb)';
SELECT fk.name AS ForeignKey,
       OBJECT_NAME(fk.parent_object_id)     AS FromTable,
       OBJECT_NAME(fk.referenced_object_id) AS ToTable
FROM sys.foreign_keys fk
ORDER BY fk.name;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE OBJECT_NAME(referenced_object_id) IS NULL)
    THROW 60000, 'Found a foreign key that points outside LoansDb', 1;
GO

PRINT '0) Кирилиця збережена коректно (скрипти в UTF-8)';
IF (SELECT UNICODE(FullName) FROM dbo.Readers WHERE CardNumber = N'LIB-0001') <> 1054
    THROW 60000, 'Seed text encoding is broken: expected Cyrillic letter O (U+041E)', 1;
GO

PRINT '2) Пошук + пагінація: черга видач у статусі Submitted (IX_Loans_Status_LoanDate)';
DECLARE @Page INT = 1, @PageSize INT = 5;
SELECT Id, Number, ReaderId, Status, LoanDate
FROM dbo.Loans
WHERE Status = N'Submitted' AND IsDeleted = 0
ORDER BY LoanDate
OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;

PRINT '   Видачі читача з певним статусом (IX_Loans_ReaderId_Status)';
SELECT l.Number, l.Status, l.LoanDate
FROM dbo.Loans l
JOIN dbo.Readers r ON r.Id = l.ReaderId
WHERE r.CardNumber = N'LIB-0001' AND l.Status = N'Issued' AND l.IsDeleted = 0
ORDER BY l.LoanDate DESC;

PRINT '   Прострочені видачі (IX_Loans_Overdue)';
SELECT Number, DueDate FROM dbo.Loans
WHERE Status = N'Issued' AND IsDeleted = 0 AND DueDate < CAST(SYSUTCDATETIME() AS DATE);

PRINT '   Денормалізоване представлення для читання';
SELECT TOP (10) * FROM dbo.vw_LoanSummary ORDER BY Id;
GO

PRINT '3a) CREATE + READ через процедури, знімок ціни (відкочується)';
BEGIN TRANSACTION;
DECLARE @ReaderId BIGINT = (SELECT Id FROM dbo.Readers WHERE CardNumber = N'LIB-0002');
DECLARE @Items dbo.LoanItemList;
INSERT INTO @Items (BookId, Quantity) VALUES (1, 1), (7, 2);
DECLARE @LoanId BIGINT;
EXEC dbo.usp_CreateLoan @ReaderId = @ReaderId, @Items = @Items, @DeliveryType = N'Pickup',
                        @CreatedBy = N'checks', @LoanId = @LoanId OUTPUT;
PRINT CONCAT('   created loan ', @LoanId);
EXEC dbo.usp_GetLoanById @LoanId = @LoanId;
-- зміна ціни в довіднику НЕ змінює позицію вже оформленої видачі (знімок)
UPDATE dbo.Books SET Price = Price + 100 WHERE Id = 1;
DECLARE @SnapshotPrice DECIMAL(18,2) = (SELECT UnitPrice FROM dbo.LoanItems WHERE LoanId = @LoanId AND BookId = 1);
ROLLBACK TRANSACTION;
IF @SnapshotPrice <> 25.00 THROW 60000, 'Snapshot price in LoanItems must not change', 1;
PRINT '   OK: snapshot price kept';
GO

PRINT '3b) Оптимістична конкурентність: застарілий RowVersion -> 50003';
DECLARE @Err INT = 0;
BEGIN TRANSACTION;
DECLARE @LoanId BIGINT = (SELECT Id FROM dbo.Loans WHERE Number = N'LN-SEED-001');
DECLARE @Rv BINARY(8) = (SELECT RowVersion FROM dbo.Loans WHERE Id = @LoanId);
EXEC dbo.usp_UpdateLoanDetails @LoanId = @LoanId, @DeliveryType = N'Courier',
     @DeliveryAddress = N'Київ, вул. Січових Стрільців, 3', @ExpectedRowVersion = @Rv, @UpdatedBy = N'checks';
BEGIN TRY
    EXEC dbo.usp_UpdateLoanDetails @LoanId = @LoanId, @DeliveryType = N'Pickup',
         @ExpectedRowVersion = @Rv, @UpdatedBy = N'checks';     -- @Rv уже застарів
END TRY
BEGIN CATCH
    SET @Err = ERROR_NUMBER();
    PRINT CONCAT('   caught ', ERROR_NUMBER(), ': ', ERROR_MESSAGE());
END CATCH;
IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
IF @Err <> 50003 THROW 60000, 'Stale RowVersion must be rejected with 50003', 1;
PRINT '   OK';
GO

PRINT '3c) Недопустимий перехід: видати (Issued) НЕОПЛАЧЕНУ видачу -> 50002';
DECLARE @Err INT = 0;
DECLARE @LoanId BIGINT = (SELECT Id FROM dbo.Loans WHERE Number = N'LN-SEED-001');   -- Submitted
BEGIN TRY
    EXEC dbo.usp_ChangeLoanStatus @LoanId = @LoanId, @NewStatus = N'Issued', @ChangedBy = N'checks';
END TRY
BEGIN CATCH
    SET @Err = ERROR_NUMBER();
    PRINT CONCAT('   caught ', ERROR_NUMBER(), ': ', ERROR_MESSAGE());
END CATCH;
IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
IF @Err <> 50002 THROW 60000, 'Issuing an unpaid loan must be rejected with 50002', 1;
IF (SELECT Status FROM dbo.Loans WHERE Id = @LoanId) <> N'Submitted'
    THROW 60000, 'Status must stay unchanged after rejected transition', 1;
PRINT '   OK: status unchanged';
GO

PRINT '3d) Допустимий ланцюжок Submitted -> Reserved -> Paid -> Issued -> Returned (відкочується)';
DECLARE @Err INT = 0, @FinalStatus NVARCHAR(30), @Same BIT = 0;
BEGIN TRANSACTION;
DECLARE @LoanId BIGINT = (SELECT Id FROM dbo.Loans WHERE Number = N'LN-SEED-001');
EXEC dbo.usp_ConfirmLoan @LoanId = @LoanId, @ChangedBy = N'checks';
DECLARE @Total DECIMAL(18,2) = (SELECT SUM(Quantity * (UnitPrice - Discount)) FROM dbo.LoanItems WHERE LoanId = @LoanId);
DECLARE @PaymentId BIGINT, @PaymentId2 BIGINT;
EXEC dbo.usp_RegisterPayment @LoanId = @LoanId, @Amount = @Total, @Method = N'Card',
     @ExternalReference = N'PAY-CHECK-1', @CreatedBy = N'checks', @PaymentId = @PaymentId OUTPUT;
EXEC dbo.usp_RegisterPayment @LoanId = @LoanId, @Amount = @Total, @Method = N'Card',   -- повтор: ідемпотентно
     @ExternalReference = N'PAY-CHECK-1', @CreatedBy = N'checks', @PaymentId = @PaymentId2 OUTPUT;
IF @PaymentId = @PaymentId2 SET @Same = 1;
EXEC dbo.usp_ChangeLoanStatus @LoanId = @LoanId, @NewStatus = N'Issued',   @ChangedBy = N'checks';
EXEC dbo.usp_ChangeLoanStatus @LoanId = @LoanId, @NewStatus = N'Returned', @ChangedBy = N'checks';
SELECT FromStatus, ToStatus, ChangedBy FROM dbo.LoanStatusHistory WHERE LoanId = @LoanId ORDER BY Id;
SET @FinalStatus = (SELECT Status FROM dbo.Loans WHERE Id = @LoanId);
BEGIN TRY
    -- скасування після оплати/повернення заборонене
    EXEC dbo.usp_ChangeLoanStatus @LoanId = @LoanId, @NewStatus = N'Cancelled', @ChangedBy = N'checks';
END TRY
BEGIN CATCH
    SET @Err = ERROR_NUMBER();
    PRINT CONCAT('   caught ', ERROR_NUMBER(), ': ', ERROR_MESSAGE());
END CATCH;
IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
IF @Same = 0 THROW 60000, 'Payment must be idempotent by ExternalReference', 1;
IF @FinalStatus <> N'Returned' THROW 60000, 'Loan must be Returned after valid transitions', 1;
IF @Err <> 50002 THROW 60000, 'Cancelling a returned loan must be rejected with 50002', 1;
PRINT '   OK';
GO

PRINT '4) Обмеження CHECK / UNIQUE працюють';
BEGIN TRANSACTION;
BEGIN TRY
    INSERT INTO dbo.Books (Id, Title, Price) VALUES (999, N'Bad price', -1);
    THROW 60000, 'CHECK Price > 0 must fail', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 547 THROW;
    PRINT '   OK: CHECK constraint (547)';
END CATCH;
IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
GO
BEGIN TRANSACTION;
BEGIN TRY
    INSERT INTO dbo.Readers (Email, FullName, CardNumber) VALUES (N'olena.kovalenko@example.com', N'Дублікат', N'LIB-9999');
    THROW 60000, 'Unique e-mail must fail', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
    PRINT CONCAT('   OK: unique index (', ERROR_NUMBER(), ')');
END CATCH;
IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
GO

PRINT '5) Індекси та обмеження схеми';
SELECT t.name AS TableName, i.name AS IndexName, i.is_unique, i.has_filter, i.filter_definition
FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id
WHERE i.name IS NOT NULL ORDER BY t.name, i.name;
SELECT OBJECT_NAME(parent_object_id) AS TableName, name, definition FROM sys.check_constraints ORDER BY 1, 2;
GO
PRINT 'LoansDb checks passed';
GO
