using System.Data;
using Dapper;
using Loans.Dal.Abstractions;
using Loans.Dal.Ado;
using Microsoft.Data.SqlClient;

namespace Loans.Dal.Dapper;

/// <summary>
/// Спільний CRUD для Dapper-репозиторіїв, написаний ОДИН раз. Конкретні репозиторії додають
/// лише специфічні запити. Ім'я таблиці — константа з коду (не вхідні дані користувача),
/// тому його підстановка в текст запиту безпечна; усі значення передаються лише параметрами.
/// </summary>
public abstract class BaseDapperRepository<T> : RepositoryBase, IGenericRepository<T> where T : class
{
    protected readonly string TableName;

    protected BaseDapperRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction, string tableName)
        : base(connection, currentTransaction)
    {
        TableName = tableName;
    }

    /// <summary>Перелік стовпців для SELECT (без «SELECT *»).</summary>
    protected abstract string SelectColumns { get; }

    /// <summary>INSERT ... OUTPUT INSERTED.Id VALUES (@...) — параметри беруться з властивостей сутності.</summary>
    protected abstract string InsertSql { get; }

    protected CommandDefinition Command(string sql, object? parameters, CancellationToken ct,
        CommandType commandType = CommandType.Text)
        => new(sql, parameters, Transaction, CommandTimeoutSeconds, commandType, cancellationToken: ct);

    public virtual Task<T?> GetByIdAsync(long id, CancellationToken ct = default) => GuardAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        return await Connection.QuerySingleOrDefaultAsync<T>(Command(
            $"SELECT {SelectColumns} FROM {TableName} WHERE Id = @Id AND IsDeleted = 0", new { Id = id }, ct));
    });

    public virtual Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default) => GuardAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        var rows = await Connection.QueryAsync<T>(Command(
            $"SELECT {SelectColumns} FROM {TableName} WHERE IsDeleted = 0 ORDER BY Id", null, ct));
        return (IReadOnlyList<T>)rows.AsList();
    });

    public virtual Task<long> AddAsync(T entity, CancellationToken ct = default) => GuardAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteScalarAsync<long>(Command(InsertSql, entity, ct));
    });

    public virtual Task<bool> DeleteAsync(long id, CancellationToken ct = default) => GuardAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        var affected = await Connection.ExecuteAsync(Command(
            $"UPDATE {TableName} SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id AND IsDeleted = 0",
            new { Id = id }, ct));
        return affected > 0;
    });
}
