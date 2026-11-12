using Loans.Dal;
using Loans.Domain.Constants;
using Loans.Domain.Exceptions;
using Loans.Domain.Models;

namespace Loans.Tests;

/// <summary>
/// Контрольна точка Лаб. 02: операція, що змінює дві таблиці, при помилці в другому репозиторії
/// відкочує зміни першого (статус видачі лишається Submitted).
/// </summary>
public class UnitOfWorkIntegrationTests
{
    [SkippableFact]
    public async Task Failure_in_second_repository_rolls_back_first_repository_changes()
    {
        TestDatabase.SkipIfUnavailable();
        const long bookWithoutCopies = 8;   // seed: AvailableCopies = 0

        // підготовка: нова видача на книгу без примірників
        long loanId;
        await using (var setup = new UnitOfWork(TestDatabase.ConnectionString!))
        {
            var readers = await setup.Readers.GetAllAsync();
            var book = (await setup.Books.GetByIdsAsync([bookWithoutCopies])).Single();
            await setup.BeginTransactionAsync();
            loanId = await setup.Loans.AddAsync(new Loan
            {
                ReaderId = readers[0].Id,
                DueDate = DateTime.UtcNow.Date.AddDays(7),
                CreatedBy = "integration-test",
                Items = [new LoanItem { BookId = book.Id, BookTitle = book.Title, UnitPrice = book.Price, Quantity = 1 }]
            });
            await setup.CommitAsync();
        }

        // дія: той самий сценарій, що й LoanService.ConfirmAsync
        await using (var uow = new UnitOfWork(TestDatabase.ConnectionString!))
        {
            await uow.BeginTransactionAsync();
            await uow.Loans.ConfirmAsync(loanId, "integration-test");                 // 1-й репозиторій: статус змінено
            var inside = await uow.Loans.GetByIdAsync(loanId);
            Assert.Equal(LoanStatus.Reserved, inside!.Status);                         // у межах транзакції — Reserved

            await Assert.ThrowsAsync<BusinessConflictException>(() =>
                uow.Books.ReserveCopiesAsync(bookWithoutCopies, 1));                  // 2-й репозиторій: нестача
            await uow.RollbackAsync();
        }

        // перевірка: зміни першого репозиторію відкочено
        await using (var check = new UnitOfWork(TestDatabase.ConnectionString!))
        {
            var loan = await check.Loans.GetByIdAsync(loanId);
            Assert.Equal(LoanStatus.Submitted, loan!.Status);
            var book = await check.Books.GetByIdAsync(bookWithoutCopies);
            Assert.Equal(0, book!.AvailableCopies);
        }
    }

    [SkippableFact]
    public async Task Reader_name_with_apostrophe_is_stored_safely()
    {
        TestDatabase.SkipIfUnavailable();
        await using var uow = new UnitOfWork(TestDatabase.ConnectionString!);
        var suffix = Random.Shared.Next(100000, 999999);
        var id = await uow.Readers.AddAsync(new Reader
        {
            Email = $"obrien.{suffix}@example.com",
            FullName = "Conan O'Brien",
            CardNumber = $"LIB-{suffix}"
        });

        var stored = await uow.Readers.GetByIdAsync(id);
        Assert.Equal("Conan O'Brien", stored!.FullName);
    }
}
