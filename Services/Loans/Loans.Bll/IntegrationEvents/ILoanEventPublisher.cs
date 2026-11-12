using Loans.Domain.Models;

namespace Loans.Bll.IntegrationEvents;

/// <summary>
/// Порт публікації подій сервісу видач (Лаб. 08). Викликається ЛИШЕ після CommitAsync — якщо транзакція не вдалася,
/// подія не публікується. Реалізація (адаптер до RabbitMQ) — у Loans.Api/IntegrationEvents.
/// </summary>
public interface ILoanEventPublisher
{
    /// <summary>Статус видачі змінився: loan — стан ПІСЛЯ зміни (з позиціями), oldStatus — попередній.</summary>
    Task StatusChangedAsync(Loan loan, string oldStatus, CancellationToken ct);
}
