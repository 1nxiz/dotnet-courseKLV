using System.Data;
using Loans.Dal.Abstractions;
using Loans.Dal.Ado;
using Loans.Dal.Dapper;
using Microsoft.Data.SqlClient;

namespace Loans.Dal;

/// <summary>
/// Unit of Work на ADO.NET: одне з'єднання на всю операцію (Scoped — одне на HTTP-запит),
/// транзакція з потрібним рівнем ізоляції, спільна для всіх репозиторіїв; гарантує Commit або Rollback.
///
/// Рівень ізоляції за замовчуванням — READ COMMITTED: не читає незафіксовані дані й тримає мінімум
/// блокувань. Від втрачених оновлень захищають UPDLOCK/HOLDLOCK у процедурах і RowVersion,
/// а від нестачі примірників — умовний UPDATE (AvailableCopies >= @Quantity). Вищі рівні
/// (REPEATABLE READ, SERIALIZABLE) зменшують аномалії читання, але збільшують кількість блокувань
/// і ризик взаємних блокувань (deadlock) — компроміс deadlocks vs. consistency.
/// Таймаут кожної команди — 30 с (RepositoryBase.CommandTimeoutSeconds).
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly SqlConnection _connection;
    private SqlTransaction? _transaction;

    public UnitOfWork(string connectionString)
    {
        _connection = new SqlConnection(connectionString);
        // усі репозиторії отримують ОДНЕ з'єднання і доступ до ПОТОЧНОЇ транзакції
        Readers = new ReaderRepository(_connection, () => _transaction);
        Loans = new LoanRepository(_connection, () => _transaction);
        Books = new BookRepository(_connection, () => _transaction);
        ProcessedEvents = new ProcessedEventRepository(_connection, () => _transaction);
    }

    public IReaderRepository Readers { get; }
    public ILoanRepository Loans { get; }
    public IBookRepository Books { get; }
    public IProcessedEventRepository ProcessedEvents { get; }

    public bool HasActiveTransaction => _transaction is not null;

    public async Task BeginTransactionAsync(IsolationLevel level = IsolationLevel.ReadCommitted, CancellationToken ct = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("A transaction is already in progress");

        if (_connection.State != ConnectionState.Open)
            await _connection.OpenAsync(ct);
        _transaction = (SqlTransaction)await _connection.BeginTransactionAsync(level, ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("There is no active transaction to commit");

        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is null) return;
        try
        {
            // якщо процедура з XACT_ABORT ON уже відкотила транзакцію на сервері,
            // SqlTransaction стає «зомбі» (Connection == null) — повторний Rollback не потрібен
            if (_transaction.Connection is not null)
                await _transaction.RollbackAsync(ct);
        }
        catch (InvalidOperationException)
        {
            // транзакція вже завершена сервером — нічого відкочувати
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();       // незафіксована транзакція відкочується
        await _connection.DisposeAsync();            // з'єднання повертається в пул SqlClient
    }
}
