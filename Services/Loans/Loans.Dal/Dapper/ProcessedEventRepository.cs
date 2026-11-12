using Dapper;
using Loans.Dal.Abstractions;
using Loans.Dal.Ado;
using Microsoft.Data.SqlClient;

namespace Loans.Dal.Dapper;

/// <summary>
/// Ідемпотентність споживача: INSERT, лише якщо EventId ще немає. UPDLOCK + HOLDLOCK блокують діапазон ключа до кінця
/// транзакції, тому два паралельні обробники тієї самої події не вставлять її обидва (другий чекає й отримає 0 рядків).
/// Запис виконується в ТІЙ САМІЙ транзакції, що й оновлення копії даних.
/// </summary>
public sealed class ProcessedEventRepository : RepositoryBase, IProcessedEventRepository
{
    public ProcessedEventRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
        : base(connection, currentTransaction) { }

    public Task<bool> TryAddAsync(Guid eventId, string eventType, CancellationToken ct = default) => GuardAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        var inserted = await Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.ProcessedEvents (EventId, EventType)
            SELECT @EventId, @EventType
            WHERE NOT EXISTS (SELECT 1 FROM dbo.ProcessedEvents WITH (UPDLOCK, HOLDLOCK) WHERE EventId = @EventId);
            """, new { EventId = eventId, EventType = eventType }, Transaction, CommandTimeoutSeconds, cancellationToken: ct));
        return inserted == 1;
    });
}
