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
}
