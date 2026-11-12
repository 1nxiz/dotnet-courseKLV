namespace Loans.Dal.Abstractions;

/// <summary>Журнал оброблених integration events (Лаб. 08, таблиця dbo.ProcessedEvents).</summary>
public interface IProcessedEventRepository
{
    /// <summary>
    /// Записати EventId у поточній транзакції. false — подію вже оброблено (повторна доставка): обробку треба пропустити.
    /// </summary>
    Task<bool> TryAddAsync(Guid eventId, string eventType, CancellationToken ct = default);
}
