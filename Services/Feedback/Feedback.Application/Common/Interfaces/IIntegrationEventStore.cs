namespace Feedback.Application.Common.Interfaces;

/// <summary>
/// Журнал оброблених integration events (Лаб. 08, колекція processedEvents: _id = EventId).
/// MongoDB без replica set не має багатодокументних транзакцій, тому порядок такий: спершу позначка
/// (унікальний _id відсікає дублікат), потім оновлення; якщо оновлення впало — позначка знімається й подія повторюється.
/// Самі оновлення також ідемпотентні (захист за часом події).
/// </summary>
public interface IProcessedEventStore
{
    /// <summary>false — подію вже оброблено (повторна доставка).</summary>
    Task<bool> TryMarkProcessedAsync(Guid eventId, string eventType, CancellationToken ct);

    Task UnmarkAsync(Guid eventId, CancellationToken ct);
}

/// <summary>Оновлення денормалізованих копій даних Каталогу у FeedbackDb (політика дублювання Лаб. 01).</summary>
public interface IBookCopiesWriter
{
    /// <summary>
    /// Нова назва книги в reviews.bookTitle, discussions.bookTitle і bookRatings.bookTitle — лише в документах, де копію
    /// ще не оновлювала новіша подія (bookTitleUpdatedAt &lt; occurredAt). Повертає id змінених відгуків (для інвалідації кешу).
    /// </summary>
    Task<BookTitleSyncResult> UpdateBookTitleAsync(long bookId, string title, DateTime occurredAt, CancellationToken ct);
}

public sealed record BookTitleSyncResult(IReadOnlyList<string> ReviewIds, long DiscussionsUpdated, long RatingsUpdated);
