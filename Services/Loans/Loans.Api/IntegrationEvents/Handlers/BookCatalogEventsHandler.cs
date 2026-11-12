using Loans.Api.IntegrationEvents.Events;
using Loans.Dal.Abstractions;
using Platform.EventBus;

namespace Loans.Api.IntegrationEvents.Handlers;

/// <summary>
/// Черга loans.books-sync: реплікація локальної копії довідника книг (dbo.Books у LoansDb) за політикою дублювання Лаб. 01.
/// Для кожної події — ОДНА транзакція: запис EventId у dbo.ProcessedEvents + оновлення копії. Повторна доставка
/// (EventId уже є) пропускається. SourceUpdatedAt не дає старішій події перезаписати новіші дані.
/// Знімки в оформлених видачах (LoanItems.BookTitle / UnitPrice) НЕ змінюються.
/// </summary>
public sealed class BookCatalogEventsHandler :
    IIntegrationEventHandler<BookCreatedEvent>,
    IIntegrationEventHandler<BookPriceChangedEvent>,
    IIntegrationEventHandler<BookRenamedEvent>,
    IIntegrationEventHandler<BookDeletedEvent>
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<BookCatalogEventsHandler> _logger;

    public BookCatalogEventsHandler(IUnitOfWork uow, ILogger<BookCatalogEventsHandler> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public Task HandleAsync(BookCreatedEvent e, CancellationToken ct)
        => ApplyOnceAsync(e, e.BookId, () => _uow.Books.UpsertFromCatalogAsync(e.BookId, e.Title, e.Isbn, e.Price, e.OccurredAt, ct), ct);

    public Task HandleAsync(BookPriceChangedEvent e, CancellationToken ct)
    {
        if (e.NewPrice <= 0)                                   // порушене бізнес-правило: повтор не допоможе -> DLQ
            throw new PermanentEventException($"Invalid price {e.NewPrice} for book {e.BookId}");
        return ApplyOnceAsync(e, e.BookId, () => _uow.Books.ApplyPriceAsync(e.BookId, e.Title, e.NewPrice, e.OccurredAt, ct), ct);
    }

    public Task HandleAsync(BookRenamedEvent e, CancellationToken ct)
        => ApplyOnceAsync(e, e.BookId, () => _uow.Books.ApplyTitleAsync(e.BookId, e.NewTitle, e.OccurredAt, ct), ct);

    public Task HandleAsync(BookDeletedEvent e, CancellationToken ct)
        => ApplyOnceAsync(e, e.BookId, () => _uow.Books.MarkDeletedFromCatalogAsync(e.BookId, e.OccurredAt, ct), ct);

    private async Task ApplyOnceAsync(IntegrationEvent e, int bookId, Func<Task<bool>> apply, CancellationToken ct)
    {
        var eventType = e.GetType().Name;
        await _uow.BeginTransactionAsync(ct: ct);
        try
        {
            if (!await _uow.ProcessedEvents.TryAddAsync(e.EventId, eventType, ct))
            {
                await _uow.RollbackAsync(ct);
                _logger.LogInformation("Duplicate event {EventId} ({EventType}) for book {BookId} skipped", e.EventId, eventType, bookId);
                return;
            }

            var changed = await apply();
            await _uow.CommitAsync(ct);                        // EventId + копія — атомарно
            if (changed)
                _logger.LogInformation("Book copy {BookId} updated by {EventType} {EventId}", bookId, eventType, e.EventId);
            else
                _logger.LogWarning("Book copy {BookId} not changed by {EventType} {EventId}: no such book or a newer change already applied",
                    bookId, eventType, e.EventId);
        }
        catch
        {
            if (_uow.HasActiveTransaction) await _uow.RollbackAsync(ct);
            throw;                                             // consumer вирішить: повтор чи DLQ
        }
    }
}
