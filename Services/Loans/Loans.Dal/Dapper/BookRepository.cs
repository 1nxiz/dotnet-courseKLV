using Dapper;
using Loans.Dal.Abstractions;
using Loans.Domain.Exceptions;
using Loans.Domain.Models;
using Microsoft.Data.SqlClient;

namespace Loans.Dal.Dapper;

/// <summary>Репозиторій локальної копії книг на ADO.NET + Dapper.</summary>
public sealed class BookRepository : BaseDapperRepository<Book>, IBookRepository
{
    public BookRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
        : base(connection, currentTransaction, "dbo.Books") { }

    protected override string SelectColumns =>
        "Id, Title, Isbn, Price, AvailableCopies, SourceUpdatedAt, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted, RowVersion";

    // Id задається явно — це Id книги з Каталогу (BooksDb), а не IDENTITY
    protected override string InsertSql => """
        INSERT INTO dbo.Books (Id, Title, Isbn, Price, AvailableCopies, SourceUpdatedAt, CreatedBy)
        OUTPUT INSERTED.Id
        VALUES (@Id, @Title, @Isbn, @Price, @AvailableCopies, @SourceUpdatedAt, @CreatedBy)
        """;

    public Task<IReadOnlyList<Book>> GetByIdsAsync(IEnumerable<long> ids, CancellationToken ct = default) => GuardAsync(async () =>
    {
        var idArray = ids.Distinct().ToArray();
        if (idArray.Length == 0) return (IReadOnlyList<Book>)Array.Empty<Book>();

        await EnsureOpenAsync(ct);
        var rows = await Connection.QueryAsync<Book>(Command(
            $"SELECT {SelectColumns} FROM dbo.Books WHERE Id IN @Ids AND IsDeleted = 0",   // Dapper розгортає IN у параметри
            new { Ids = idArray }, ct));
        return (IReadOnlyList<Book>)rows.AsList();
    });

    public Task ReserveCopiesAsync(long bookId, int quantity, CancellationToken ct = default) => GuardAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        var affected = await Connection.ExecuteAsync(Command("""
            UPDATE dbo.Books
            SET AvailableCopies = AvailableCopies - @Quantity, UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id AND IsDeleted = 0 AND AvailableCopies >= @Quantity
            """, new { Id = bookId, Quantity = quantity }, ct));

        if (affected == 1) return;

        var available = await Connection.QuerySingleOrDefaultAsync<int?>(Command(
            "SELECT AvailableCopies FROM dbo.Books WHERE Id = @Id AND IsDeleted = 0", new { Id = bookId }, ct));
        if (available is null)
            throw new NotFoundException($"Book {bookId} not found");
        throw new BusinessConflictException(
            $"Not enough copies of book {bookId}: requested {quantity}, available {available}");
    });

    public Task ReturnCopiesAsync(long bookId, int quantity, CancellationToken ct = default) => GuardAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        var affected = await Connection.ExecuteAsync(Command("""
            UPDATE dbo.Books
            SET AvailableCopies = AvailableCopies + @Quantity, UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id AND IsDeleted = 0
            """, new { Id = bookId, Quantity = quantity }, ct));
        if (affected == 0)
            throw new NotFoundException($"Book {bookId} not found");
    });

    public Task<bool> UpdateAsync(Book book, CancellationToken ct = default) => GuardAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        var affected = await Connection.ExecuteAsync(Command("""
            UPDATE dbo.Books
            SET Title = @Title, Isbn = @Isbn, Price = @Price, AvailableCopies = @AvailableCopies,
                UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
            WHERE Id = @Id AND IsDeleted = 0
            """, new { book.Id, book.Title, book.Isbn, book.Price, book.AvailableCopies, book.UpdatedBy }, ct));
        return affected > 0;
    });

    // ---------------- Лаб. 08: копія довідника за подіями Каталогу ----------------
    private const string SourceIsOlder = "(SourceUpdatedAt IS NULL OR SourceUpdatedAt < @OccurredAt)";

    public Task<bool> UpsertFromCatalogAsync(long id, string title, string isbn, decimal price, DateTime occurredAt,
        CancellationToken ct = default) => GuardAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        var affected = await Connection.ExecuteAsync(Command($"""
            MERGE dbo.Books WITH (HOLDLOCK) AS target
            USING (SELECT @Id AS Id) AS source ON target.Id = source.Id
            WHEN MATCHED AND {SourceIsOlder.Replace("SourceUpdatedAt", "target.SourceUpdatedAt")} THEN
                UPDATE SET Title = @Title, Isbn = @Isbn, Price = @Price, IsDeleted = 0, SourceUpdatedAt = @OccurredAt,
                           UpdatedAt = SYSUTCDATETIME(), UpdatedBy = N'books-events'
            WHEN NOT MATCHED THEN
                INSERT (Id, Title, Isbn, Price, AvailableCopies, SourceUpdatedAt, CreatedBy)
                VALUES (@Id, @Title, @Isbn, @Price, 0, @OccurredAt, N'books-events');
            """, new { Id = id, Title = title, Isbn = isbn, Price = price, OccurredAt = occurredAt }, ct));
        return affected > 0;
    });

    public Task<bool> ApplyPriceAsync(long id, string title, decimal price, DateTime occurredAt, CancellationToken ct = default)
        => ApplyAsync("Title = @Title, Price = @Price", new { Id = id, Title = title, Price = price, OccurredAt = occurredAt }, ct);

    public Task<bool> ApplyTitleAsync(long id, string title, DateTime occurredAt, CancellationToken ct = default)
        => ApplyAsync("Title = @Title", new { Id = id, Title = title, OccurredAt = occurredAt }, ct);

    public Task<bool> MarkDeletedFromCatalogAsync(long id, DateTime occurredAt, CancellationToken ct = default)
        => ApplyAsync("IsDeleted = 1", new { Id = id, OccurredAt = occurredAt }, ct);

    /// <summary>setClause — константа з коду, не вхідні дані; значення — лише параметри.</summary>
    private Task<bool> ApplyAsync(string setClause, object parameters, CancellationToken ct) => GuardAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        var affected = await Connection.ExecuteAsync(Command($"""
            UPDATE dbo.Books
            SET {setClause}, SourceUpdatedAt = @OccurredAt, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = N'books-events'
            WHERE Id = @Id AND {SourceIsOlder}
            """, parameters, ct));
        return affected > 0;
    });
}
