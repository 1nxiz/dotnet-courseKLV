using Ardalis.Specification;
using Books.Domain.Entities;

namespace Books.Dal.Repositories;

/// <summary>Async Generic Repository: той самий контракт, що в Лаб. 02, але реалізований EF Core через DbSet&lt;T&gt;.</summary>
public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);                                   // лише змінює стан у контексті — синхронно
    void Delete(T entity);
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Вибірка за специфікацією (критерії, Include, сортування, пагінація) — один SQL-запит.</summary>
    Task<IReadOnlyList<T>> ListAsync(ISpecification<T> specification, CancellationToken ct = default);

    /// <summary>Кількість записів за критеріями специфікації (без Skip/Take) — рахує БД.</summary>
    Task<int> CountAsync(ISpecification<T> specification, CancellationToken ct = default);
}

/// <summary>Книги: EAGER LOADING — картка книги з усім пов'язаним ОДНИМ запитом (Include/ThenInclude).</summary>
public interface IBookRepository : IGenericRepository<Book>
{
    Task<Book?> GetWithDetailsAsync(int id, CancellationToken ct = default);
    Task<bool> ExistsByIsbnAsync(string isbn, CancellationToken ct = default);
}

/// <summary>Жанри: EXPLICIT LOADING — жанр окремим запитом, його книги — другим, лише коли потрібні.</summary>
public interface IGenreRepository : IGenericRepository<Genre>
{
    Task<Genre?> GetWithBooksAsync(int id, CancellationToken ct = default);
}

/// <summary>Зв'язки книга-жанр: LINQ to Entities через проміжну сутність M:N та її власне поле AddedAt.</summary>
public interface IBookGenreRepository
{
    Task<IReadOnlyList<Book>> GetRecentlyAddedAsync(string genreSlug, DateTime since, CancellationToken ct = default);
    Task<IReadOnlyList<(Genre Genre, int BooksCount)>> GetGenreStatisticsAsync(CancellationToken ct = default);
}

public interface IAuthorRepository : IGenericRepository<Author>
{
}

/// <summary>Unit of Work: набір репозиторіїв + єдиний SaveChangesAsync (одна транзакція на бізнес-операцію).</summary>
public interface IUnitOfWork
{
    IBookRepository Books { get; }
    IGenreRepository Genres { get; }
    IBookGenreRepository BookGenres { get; }
    IAuthorRepository Authors { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
