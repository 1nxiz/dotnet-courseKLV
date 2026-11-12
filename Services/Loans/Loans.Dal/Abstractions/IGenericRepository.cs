namespace Loans.Dal.Abstractions;

/// <summary>
/// Узагальнений CRUD-контракт. Конкретні інтерфейси успадковують його й додають лише
/// вузькоспеціалізовані запити (JOIN, агрегації, виклики процедур).
/// Методи повертають моделі — ніколи SqlDataReader, DataTable чи «сирі» SQL-рядки.
/// </summary>
public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task<long> AddAsync(T entity, CancellationToken ct = default);

    /// <summary>М'яке видалення (IsDeleted = 1). Повертає false, якщо запис не знайдено.</summary>
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}
