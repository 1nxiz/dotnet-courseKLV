using System.Data;
using Loans.Dal.Abstractions;
using Loans.Domain.Exceptions;
using Loans.Domain.Models;
using Microsoft.Data.SqlClient;

namespace Loans.Dal.Ado;

/// <summary>
/// Репозиторій читачів на ЧИСТОМУ ADO.NET (Microsoft.Data.SqlClient): SqlCommand, типізовані параметри,
/// ручне читання SqlDataReader, участь у транзакції Unit of Work. Не використовує Dapper і не успадковує Generic Repository.
/// </summary>
public sealed class ReaderRepository : RepositoryBase, IReaderRepository
{
    private const string SelectColumns =
        "Id, Email, FullName, Phone, CardNumber, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted, RowVersion";

    public ReaderRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
        : base(connection, currentTransaction) { }

    public Task<Reader?> GetByIdAsync(long id, CancellationToken ct = default) => GuardAsync(async () =>
    {
        const string sql = $"SELECT {SelectColumns} FROM dbo.Readers WHERE Id = @Id AND IsDeleted = 0";

        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;                // лише параметр, без конкатенації

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    });

    public Task<IReadOnlyList<Reader>> GetAllAsync(CancellationToken ct = default) => GuardAsync(async () =>
    {
        const string sql = $"SELECT {SelectColumns} FROM dbo.Readers WHERE IsDeleted = 0 ORDER BY Id";

        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var result = new List<Reader>();
        while (await reader.ReadAsync(ct))
            result.Add(Map(reader));
        return (IReadOnlyList<Reader>)result;
    });

    public Task<Reader?> GetByEmailAsync(string email, CancellationToken ct = default) => GuardAsync(async () =>
    {
        const string sql = $"SELECT {SelectColumns} FROM dbo.Readers WHERE Email = @Email AND IsDeleted = 0";

        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = email;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    });

    public Task<bool> ExistsAsync(long id, CancellationToken ct = default) => GuardAsync(async () =>
    {
        const string sql = "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Readers WHERE Id = @Id AND IsDeleted = 0) THEN 1 ELSE 0 END";

        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1;
    });

    public Task<long> AddAsync(Reader reader, CancellationToken ct = default) => GuardAsync(async () =>
    {
        const string sql = """
            INSERT INTO dbo.Readers (Email, FullName, Phone, CardNumber, CreatedBy)
            OUTPUT INSERTED.Id
            VALUES (@Email, @FullName, @Phone, @CardNumber, @CreatedBy)
            """;

        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = reader.Email;
        command.Parameters.Add("@FullName", SqlDbType.NVarChar, 200).Value = reader.FullName;   // напр. O'Brien — безпечно
        command.Parameters.Add("@Phone", SqlDbType.NVarChar, 30).Value = (object?)reader.Phone ?? DBNull.Value;
        command.Parameters.Add("@CardNumber", SqlDbType.NVarChar, 20).Value = reader.CardNumber;
        command.Parameters.Add("@CreatedBy", SqlDbType.NVarChar, 100).Value = (object?)reader.CreatedBy ?? DBNull.Value;

        var id = (long)(await command.ExecuteScalarAsync(ct))!;
        reader.Id = id;
        return id;
    });

    public Task<byte[]> UpdateAsync(Reader reader, CancellationToken ct = default) => GuardAsync(async () =>
    {
        const string sql = """
            UPDATE dbo.Readers
            SET Email = @Email, FullName = @FullName, Phone = @Phone,
                UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
            OUTPUT INSERTED.RowVersion
            WHERE Id = @Id AND IsDeleted = 0 AND RowVersion = @RowVersion
            """;

        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = reader.Id;
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = reader.Email;
        command.Parameters.Add("@FullName", SqlDbType.NVarChar, 200).Value = reader.FullName;
        command.Parameters.Add("@Phone", SqlDbType.NVarChar, 30).Value = (object?)reader.Phone ?? DBNull.Value;
        command.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 100).Value = (object?)reader.UpdatedBy ?? DBNull.Value;
        command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = reader.RowVersion;

        if (await command.ExecuteScalarAsync(ct) is byte[] newVersion)
            return newVersion;

        // жоден рядок не оновлено: або читача немає, або хтось змінив його раніше
        if (!await ExistsAsync(reader.Id, ct))
            throw new NotFoundException($"Reader {reader.Id} not found");
        throw new ConcurrencyException($"Reader {reader.Id} was modified by another user. Reload it and try again");
    });

    public Task<bool> DeleteAsync(long id, CancellationToken ct = default) => GuardAsync(async () =>
    {
        const string sql = """
            UPDATE dbo.Readers SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id AND IsDeleted = 0
            """;

        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        return await command.ExecuteNonQueryAsync(ct) > 0;
    });

    private SqlCommand CreateCommand(string sql) =>
        new(sql, Connection, Transaction) { CommandTimeout = CommandTimeoutSeconds };

    private static Reader Map(SqlDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Email = r.GetString(1),
        FullName = r.GetString(2),
        Phone = r.IsDBNull(3) ? null : r.GetString(3),
        CardNumber = r.GetString(4),
        CreatedAt = r.GetDateTime(5),
        CreatedBy = r.IsDBNull(6) ? null : r.GetString(6),
        UpdatedAt = r.IsDBNull(7) ? null : r.GetDateTime(7),
        UpdatedBy = r.IsDBNull(8) ? null : r.GetString(8),
        IsDeleted = r.GetBoolean(9),
        RowVersion = r.GetFieldValue<byte[]>(10)
    };
}
