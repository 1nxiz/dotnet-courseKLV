using System.Data;

namespace Loans.Dal.Abstractions;

/// <summary>
/// Unit of Work: одне з'єднання на HTTP-запит (Scoped) і одна транзакція на бізнес-операцію,
/// спільні для всіх репозиторіїв. Гарантує Commit або Rollback усіх змін разом.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    IReaderRepository Readers { get; }
    ILoanRepository Loans { get; }
    IBookRepository Books { get; }
    IProcessedEventRepository ProcessedEvents { get; }

    bool HasActiveTransaction { get; }

    Task BeginTransactionAsync(IsolationLevel level = IsolationLevel.ReadCommitted, CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
