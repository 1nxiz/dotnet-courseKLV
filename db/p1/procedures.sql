/* =====================================================================
   Проєкт №1 — LoansDb: збережувані процедури (6 шт.)
   Коди керованих помилок (THROW), які DAL (Лаб. 02) перетворює на доменні винятки:
     50001 — сутність не знайдено           -> NotFoundException          -> HTTP 404
     50002 — недопустимий бізнес-перехід     -> BusinessConflictException  -> HTTP 409
     50003 — конфлікт версій (rowversion)    -> ConcurrencyException       -> HTTP 409
     50004 — некоректні вхідні дані          -> ValidationException        -> HTTP 400
   ===================================================================== */
USE LoansDb;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- обов'язково: таблиці мають фільтровані індекси
GO

/* ---------------------------------------------------------------------
   1. usp_CreateLoan — CREATE: оформлення видачі одним викликом.
   Назва й ціна книги копіюються з локального довідника Books у LoanItems
   як ЗНІМОК (політика дублювання: не оновлюється після оформлення).
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_CreateLoan
    @ReaderId        BIGINT,
    @Items           dbo.LoanItemList READONLY,
    @DeliveryType    NVARCHAR(20)   = N'Pickup',
    @DeliveryAddress NVARCHAR(400)  = NULL,
    @Comment         NVARCHAR(1000) = NULL,
    @DueDate         DATE           = NULL,
    @CreatedBy       NVARCHAR(100)  = NULL,
    @LoanId          BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;                       -- будь-яка помилка відкочує транзакцію

    IF NOT EXISTS (SELECT 1 FROM dbo.Readers WHERE Id = @ReaderId AND IsDeleted = 0)
        THROW 50001, N'Reader not found', 1;
    IF NOT EXISTS (SELECT 1 FROM @Items)
        THROW 50004, N'Loan must contain at least one book', 1;
    IF EXISTS (SELECT 1
               FROM @Items i
               LEFT JOIN dbo.Books b ON b.Id = i.BookId AND b.IsDeleted = 0
               WHERE b.Id IS NULL)
        THROW 50001, N'One or more books not found', 1;

    BEGIN TRANSACTION;

        INSERT INTO dbo.Loans (ReaderId, DueDate, CreatedBy)
        VALUES (@ReaderId, @DueDate, @CreatedBy);

        SET @LoanId = CAST(SCOPE_IDENTITY() AS BIGINT);

        INSERT INTO dbo.LoanDetails (LoanId, DeliveryType, DeliveryAddress, Comment)
        VALUES (@LoanId, @DeliveryType, @DeliveryAddress, @Comment);

        INSERT INTO dbo.LoanItems (LoanId, BookId, BookTitle, UnitPrice, Quantity)
        SELECT @LoanId, b.Id, b.Title, b.Price, i.Quantity          -- знімок назви та ціни
        FROM @Items i
        JOIN dbo.Books b ON b.Id = i.BookId;

        INSERT INTO dbo.LoanStatusHistory (LoanId, FromStatus, ToStatus, ChangedBy)
        VALUES (@LoanId, NULL, N'Submitted', @CreatedBy);

    COMMIT TRANSACTION;
END
GO

/* ---------------------------------------------------------------------
   2. usp_GetLoanById — READ: картка видачі двома наборами результатів
   (заголовок + деталі, позиції). У Dapper читається через QueryMultipleAsync.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetLoanById
    @LoanId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  l.Id, l.Number, l.ReaderId, l.Status, l.LoanDate, l.DueDate, l.RowVersion,
            d.DeliveryType, d.DeliveryAddress, d.Comment
    FROM dbo.Loans l
    LEFT JOIN dbo.LoanDetails d ON d.LoanId = l.Id
    WHERE l.Id = @LoanId AND l.IsDeleted = 0;

    SELECT  i.LoanId, i.BookId, i.BookTitle, i.UnitPrice, i.Discount, i.Quantity
    FROM dbo.LoanItems i
    JOIN dbo.Loans l ON l.Id = i.LoanId
    WHERE i.LoanId = @LoanId AND l.IsDeleted = 0
    ORDER BY i.BookId;
END
GO

/* ---------------------------------------------------------------------
   3. usp_UpdateLoanDetails — UPDATE з оптимістичною конкурентністю.
   Клієнт передає RowVersion, який він бачив; якщо запис уже змінено —
   керована помилка 50003 замість «тихого» затирання чужих змін.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_UpdateLoanDetails
    @LoanId             BIGINT,
    @DeliveryType       NVARCHAR(20),
    @DeliveryAddress    NVARCHAR(400)  = NULL,
    @Comment            NVARCHAR(1000) = NULL,
    @ExpectedRowVersion BINARY(8),
    @UpdatedBy          NVARCHAR(100)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

        DECLARE @Status NVARCHAR(30), @CurrentVersion BINARY(8);

        SELECT @Status = Status, @CurrentVersion = RowVersion
        FROM dbo.Loans WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @LoanId AND IsDeleted = 0;

        IF @Status IS NULL
            THROW 50001, N'Loan not found', 1;
        IF @CurrentVersion <> @ExpectedRowVersion
            THROW 50003, N'Loan was modified by another user. Reload it and try again', 1;
        IF @Status IN (N'Issued', N'Returned', N'Cancelled')
            THROW 50002, N'Delivery details can be changed only before the loan is issued', 1;

        UPDATE dbo.LoanDetails
        SET DeliveryType = @DeliveryType, DeliveryAddress = @DeliveryAddress, Comment = @Comment
        WHERE LoanId = @LoanId;

        UPDATE dbo.Loans                       -- зміна рядка оновлює RowVersion
        SET UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
        WHERE Id = @LoanId;

    COMMIT TRANSACTION;

    SELECT RowVersion FROM dbo.Loans WHERE Id = @LoanId;   -- нова версія для клієнта
END
GO

/* ---------------------------------------------------------------------
   4. usp_ChangeLoanStatus — ТРАНЗАКЦІЙНА зміна статусу з перевіркою переходу.
   Допустимі переходи (решта — керована помилка 50002):
     Submitted          -> AwaitingValidation | Reserved | Cancelled
     AwaitingValidation -> Reserved | Cancelled
     Reserved           -> Paid | Cancelled
     Paid               -> Issued              (не можна видати неоплачене!)
     Issued             -> Returned
   Скасування (Cancelled) — лише до оплати.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_ChangeLoanStatus
    @LoanId    BIGINT,
    @NewStatus NVARCHAR(30),
    @ChangedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @NewStatus NOT IN (N'Submitted', N'AwaitingValidation', N'Reserved', N'Paid', N'Issued', N'Returned', N'Cancelled')
        THROW 50004, N'Unknown loan status', 1;

    BEGIN TRANSACTION;

        -- UPDLOCK + HOLDLOCK блокують рядок до кінця транзакції: паралельна зміна статусу чекатиме
        DECLARE @Current NVARCHAR(30) =
            (SELECT Status FROM dbo.Loans WITH (UPDLOCK, HOLDLOCK) WHERE Id = @LoanId AND IsDeleted = 0);

        IF @Current IS NULL
            THROW 50001, N'Loan not found', 1;

        IF NOT EXISTS (
            SELECT 1
            FROM (VALUES (N'Submitted',          N'AwaitingValidation'),
                         (N'Submitted',          N'Reserved'),
                         (N'Submitted',          N'Cancelled'),
                         (N'AwaitingValidation', N'Reserved'),
                         (N'AwaitingValidation', N'Cancelled'),
                         (N'Reserved',           N'Paid'),
                         (N'Reserved',           N'Cancelled'),
                         (N'Paid',               N'Issued'),
                         (N'Issued',             N'Returned')) AS t(FromStatus, ToStatus)
            WHERE t.FromStatus = @Current AND t.ToStatus = @NewStatus)
        BEGIN
            DECLARE @Message NVARCHAR(400) = CONCAT(N'Transition ', @Current, N' -> ', @NewStatus, N' is not allowed');
            THROW 50002, @Message, 1;
        END

        UPDATE dbo.Loans
        SET Status = @NewStatus, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @ChangedBy
        WHERE Id = @LoanId;

        INSERT INTO dbo.LoanStatusHistory (LoanId, FromStatus, ToStatus, ChangedBy)
        VALUES (@LoanId, @Current, @NewStatus, @ChangedBy);

    COMMIT TRANSACTION;
END
GO

/* ---------------------------------------------------------------------
   5. usp_ConfirmLoan — бізнес-операція «підтвердити видачу» (аналог
   usp_ConfirmOrder / StockConfirmed в eShop): Submitted|AwaitingValidation -> Reserved.
   Використовується DAL у Лаб. 02 у транзакції разом зі списанням примірників.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_ConfirmLoan
    @LoanId    BIGINT,
    @ChangedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

        DECLARE @Status NVARCHAR(30) =
            (SELECT Status FROM dbo.Loans WITH (UPDLOCK, HOLDLOCK) WHERE Id = @LoanId AND IsDeleted = 0);

        IF @Status IS NULL
            THROW 50001, N'Loan not found', 1;
        IF @Status NOT IN (N'Submitted', N'AwaitingValidation')
            THROW 50002, N'Copies can be reserved only for a new loan', 1;

        UPDATE dbo.Loans
        SET Status = N'Reserved', UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @ChangedBy
        WHERE Id = @LoanId;

        INSERT INTO dbo.LoanStatusHistory (LoanId, FromStatus, ToStatus, ChangedBy)
        VALUES (@LoanId, @Status, N'Reserved', @ChangedBy);

    COMMIT TRANSACTION;
END
GO

/* ---------------------------------------------------------------------
   6. usp_RegisterPayment — оплата видачі: платіж + перехід Reserved -> Paid
   в одній транзакції. Ідемпотентна за @ExternalReference: повторний виклик
   з тим самим ідентифікатором повертає вже створений платіж.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_RegisterPayment
    @LoanId            BIGINT,
    @Amount            DECIMAL(18,2),
    @Method            NVARCHAR(20),
    @ExternalReference NVARCHAR(100) = NULL,
    @CreatedBy         NVARCHAR(100) = NULL,
    @PaymentId         BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

        IF @ExternalReference IS NOT NULL
        BEGIN
            SELECT @PaymentId = Id
            FROM dbo.Payments WITH (UPDLOCK, HOLDLOCK)
            WHERE ExternalReference = @ExternalReference;

            IF @PaymentId IS NOT NULL
            BEGIN
                COMMIT TRANSACTION;               -- повтор запиту: нічого не змінюємо
                RETURN;
            END
        END

        DECLARE @Status NVARCHAR(30) =
            (SELECT Status FROM dbo.Loans WITH (UPDLOCK, HOLDLOCK) WHERE Id = @LoanId AND IsDeleted = 0);

        IF @Status IS NULL
            THROW 50001, N'Loan not found', 1;
        IF @Status <> N'Reserved'
            THROW 50002, N'Only a loan with reserved copies can be paid', 1;

        DECLARE @Total DECIMAL(18,2) =
            (SELECT SUM(Quantity * (UnitPrice - Discount)) FROM dbo.LoanItems WHERE LoanId = @LoanId);

        IF @Amount <> @Total
        BEGIN
            DECLARE @Message NVARCHAR(400) = CONCAT(N'Payment amount must be equal to loan total ', @Total);
            THROW 50004, @Message, 1;
        END

        INSERT INTO dbo.Payments (LoanId, Amount, Method, ExternalReference, CreatedBy)
        VALUES (@LoanId, @Amount, @Method, @ExternalReference, @CreatedBy);

        SET @PaymentId = CAST(SCOPE_IDENTITY() AS BIGINT);

        UPDATE dbo.Loans
        SET Status = N'Paid', UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @CreatedBy
        WHERE Id = @LoanId;

        INSERT INTO dbo.LoanStatusHistory (LoanId, FromStatus, ToStatus, ChangedBy)
        VALUES (@LoanId, N'Reserved', N'Paid', @CreatedBy);

    COMMIT TRANSACTION;
END
GO
