using System.Data;
using Loans.Dal.Errors;
using Microsoft.Data.SqlClient;

namespace Loans.Dal.Ado;

/// <summary>
/// Спільна основа всіх репозиторіїв: вони отримують НЕ рядок підключення, а одне з'єднання
/// і доступ до поточної транзакції Unit of Work — тому всі репозиторії бізнес-операції
/// працюють в одній транзакції.
/// </summary>
public abstract class RepositoryBase
{
    /// <summary>Таймаут команди (с). Довгі запити не повинні тримати блокування довше за цей час.</summary>
    protected const int CommandTimeoutSeconds = 30;

    protected readonly SqlConnection Connection;
    private readonly Func<SqlTransaction?> _currentTransaction;

    protected RepositoryBase(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    {
        Connection = connection;
        _currentTransaction = currentTransaction;
    }

    /// <summary>Поточна транзакція Unit of Work (або null, якщо її не розпочато).</summary>
    protected SqlTransaction? Transaction => _currentTransaction();

    protected async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (Connection.State != ConnectionState.Open)
            await Connection.OpenAsync(ct);
    }

    /// <summary>Виконує доступ до БД і перетворює SqlException на доменні винятки.</summary>
    protected static async Task<T> GuardAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (SqlException ex) when (SqlExceptionMapper.TryMap(ex, out var mapped))
        {
            throw mapped;
        }
    }

    protected static async Task GuardAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (SqlException ex) when (SqlExceptionMapper.TryMap(ex, out var mapped))
        {
            throw mapped;
        }
    }
}
