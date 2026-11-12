using Loans.Domain.Models;

namespace Loans.Dal.Abstractions;

/// <summary>Репозиторій локальної копії довідника книг (Dapper).</summary>
public interface IBookRepository : IGenericRepository<Book>
{
    Task<IReadOnlyList<Book>> GetByIdsAsync(IEnumerable<long> ids, CancellationToken ct = default);

    /// <summary>Зарезервувати примірники (зменшити AvailableCopies). Нестача -> BusinessConflictException.</summary>
    Task ReserveCopiesAsync(long bookId, int quantity, CancellationToken ct = default);

    /// <summary>Повернути примірники на полицю (збільшити AvailableCopies).</summary>
    Task ReturnCopiesAsync(long bookId, int quantity, CancellationToken ct = default);

    /// <summary>Оновити копію даних книги (назва, ISBN, ціна, кількість на полиці).</summary>
    Task<bool> UpdateAsync(Book book, CancellationToken ct = default);

    // ---- Лаб. 08: оновлення копії довідника подіями Каталогу ----
    // Кожен метод змінює рядок лише якщо подія НОВІША за останню застосовану (SourceUpdatedAt < @OccurredAt):
    // подія, що прийшла пізніше за новішу (out-of-order), не перезапише свіжі дані. Повертає true, якщо рядок змінено.

    /// <summary>book.created: додати книгу в копію (AvailableCopies = 0 — власні дані видач) або оновити, якщо вона вже є.</summary>
    Task<bool> UpsertFromCatalogAsync(long id, string title, string isbn, decimal price, DateTime occurredAt, CancellationToken ct = default);

    /// <summary>book.price_changed: нова ціна (і актуальна назва) у копії. LoanItems.UnitPrice не змінюються — це знімок.</summary>
    Task<bool> ApplyPriceAsync(long id, string title, decimal price, DateTime occurredAt, CancellationToken ct = default);

    /// <summary>book.renamed: нова назва в копії. LoanItems.BookTitle не змінюються — це знімок.</summary>
    Task<bool> ApplyTitleAsync(long id, string title, DateTime occurredAt, CancellationToken ct = default);

    /// <summary>book.deleted: м'яко видалити копію.</summary>
    Task<bool> MarkDeletedFromCatalogAsync(long id, DateTime occurredAt, CancellationToken ct = default);
}
