using Loans.Domain.Models;

namespace Loans.Dal.Abstractions;

/// <summary>
/// Репозиторій читачів на ЧИСТОМУ ADO.NET. Свідомо НЕ успадковує IGenericRepository:
/// демонструє ручну роботу з підключенням, параметрами, читанням і транзакцією без сторонніх бібліотек,
/// а його контракт містить операції, яких немає в узагальненому CRUD (оновлення з RowVersion, пошук за email).
/// </summary>
public interface IReaderRepository
{
    Task<Reader?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<Reader>> GetAllAsync(CancellationToken ct = default);
    Task<Reader?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<bool> ExistsAsync(long id, CancellationToken ct = default);
    Task<long> AddAsync(Reader reader, CancellationToken ct = default);

    /// <summary>Оновлення з перевіркою RowVersion. Повертає новий RowVersion; конфлікт -> ConcurrencyException.</summary>
    Task<byte[]> UpdateAsync(Reader reader, CancellationToken ct = default);

    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}
