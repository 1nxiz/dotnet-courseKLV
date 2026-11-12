using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Loans.Dal.Initialization;

/// <summary>
/// Мінімальний механізм міграцій для ADO.NET-сервісу (аналог EF Migrations, Лаб. 08): скрипти Migrations/NNN_*.sql
/// застосовуються по порядку, кожен один раз — застосовані записуються в dbo.SchemaMigrations.
/// Виконується під час старту в усіх середовищах (на відміну від DatabaseInitializer, що лише для Development).
/// </summary>
public static partial class SqlMigrator
{
    public static async Task MigrateAsync(string connectionString, string migrationsDirectory, ILogger logger, CancellationToken ct = default)
    {
        if (!Directory.Exists(migrationsDirectory))
        {
            logger.LogWarning("Migrations directory {Directory} not found", migrationsDirectory);
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(ct);
                await ExecuteAsync(connection, """
                    IF OBJECT_ID(N'dbo.SchemaMigrations', N'U') IS NULL
                        CREATE TABLE dbo.SchemaMigrations (
                            Id        NVARCHAR(200) NOT NULL CONSTRAINT PK_SchemaMigrations PRIMARY KEY,
                            AppliedAt DATETIME2(3)  NOT NULL CONSTRAINT DF_SchemaMigrations_AppliedAt DEFAULT SYSUTCDATETIME());
                    """, ct);

                foreach (var file in Directory.GetFiles(migrationsDirectory, "*.sql").Order(StringComparer.Ordinal))
                {
                    var id = Path.GetFileNameWithoutExtension(file);
                    await using (var check = new SqlCommand("SELECT COUNT(*) FROM dbo.SchemaMigrations WHERE Id = @Id", connection))
                    {
                        check.Parameters.AddWithValue("@Id", id);
                        if ((int)(await check.ExecuteScalarAsync(ct))! > 0) continue;
                    }

                    await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
                    foreach (var batch in GoSeparator().Split(await File.ReadAllTextAsync(file, ct)).Where(b => !string.IsNullOrWhiteSpace(b)))
                        await ExecuteAsync(connection, batch, ct, tx);
                    await using (var journal = new SqlCommand("INSERT INTO dbo.SchemaMigrations (Id) VALUES (@Id)", connection, tx))
                    {
                        journal.Parameters.AddWithValue("@Id", id);
                        await journal.ExecuteNonQueryAsync(ct);
                    }
                    await tx.CommitAsync(ct);                           // скрипт і запис у журналі — атомарно
                    logger.LogInformation("Migration {Migration} applied", id);
                }
                return;
            }
            catch (SqlException ex) when (attempt < 10)
            {
                logger.LogWarning(ex, "Database is not ready for migrations (attempt {Attempt}), retrying in 3 s", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken ct, SqlTransaction? tx = null)
    {
        await using var command = new SqlCommand(sql, connection, tx) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(ct);
    }

    [GeneratedRegex(@"^\s*GO\s*(?:--[^\r\n]*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
