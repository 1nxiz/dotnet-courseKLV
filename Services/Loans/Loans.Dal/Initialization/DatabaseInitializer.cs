using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Loans.Dal.Initialization;

/// <summary>
/// Розгортає LoansDb скриптами з Лаб. 01 (db/p1/schema.sql, procedures.sql, seed.sql), які підключені
/// до Loans.Api як посилання (без копіювання). Використовується в Development і в Aspire, де контейнер SQL Server
/// створюється порожнім. Скрипти ідемпотентні, тому повторний старт нічого не дублює.
/// </summary>
public static partial class DatabaseInitializer
{
    private static readonly string[] Scripts = ["schema.sql", "procedures.sql", "seed.sql"];

    public static async Task InitializeAsync(string connectionString, string scriptsDirectory, ILogger logger,
        CancellationToken ct = default)
    {
        // скрипт сам створює базу (IF DB_ID(...) IS NULL CREATE DATABASE), тому підключаємось до master
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(master.ConnectionString);
                await connection.OpenAsync(ct);

                foreach (var file in Scripts)
                {
                    var script = await File.ReadAllTextAsync(Path.Combine(scriptsDirectory, file), ct);
                    foreach (var batch in SplitBatches(script))
                    {
                        await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };
                        await command.ExecuteNonQueryAsync(ct);
                    }
                    logger.LogInformation("Database script {Script} applied", file);
                }
                return;
            }
            catch (SqlException ex) when (attempt < 10)
            {
                // контейнер SQL Server може ще стартувати — повторюємо
                logger.LogWarning(ex, "Database is not ready (attempt {Attempt}), retrying in 3 s", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

    /// <summary>Ділить скрипт sqlcmd на пакети за рядками «GO».</summary>
    private static IEnumerable<string> SplitBatches(string script) =>
        GoSeparator().Split(script).Where(batch => !string.IsNullOrWhiteSpace(batch));

    [GeneratedRegex(@"^\s*GO\s*(?:--[^\r\n]*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
