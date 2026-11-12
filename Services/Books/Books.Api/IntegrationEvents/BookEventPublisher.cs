using Books.Api.IntegrationEvents.Events;
using Books.Bll.IntegrationEvents;
using Platform.EventBus;

namespace Books.Api.IntegrationEvents;

/// <summary>
/// Адаптер порту IBookEventPublisher: створює integration event (з CorrelationId поточного запиту) і публікує його
/// через IEventPublisher у exchange books-events. Збій брокера ПІСЛЯ збереження змін не скасовує операцію користувача —
/// він логуються як Error з бізнес-контекстом (гарантію доставки дав би Transactional Outbox — описано в README).
/// </summary>
public sealed class BookEventPublisher : IBookEventPublisher
{
    private readonly IEventPublisher _publisher;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<BookEventPublisher> _logger;

    public BookEventPublisher(IEventPublisher publisher, IHttpContextAccessor http, ILogger<BookEventPublisher> logger)
    {
        _publisher = publisher;
        _http = http;
        _logger = logger;
    }

    private string? CorrelationId => _http.HttpContext?.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();

    public Task BookCreatedAsync(int bookId, string title, string isbn, decimal price, CancellationToken ct)
        => PublishAsync(new BookCreatedEvent(bookId, title, isbn, price) { CorrelationId = CorrelationId },
            Topology.RoutingKeys.BookCreated, bookId, ct);

    public Task PriceChangedAsync(int bookId, string title, decimal oldPrice, decimal newPrice, CancellationToken ct)
        => PublishAsync(new BookPriceChangedEvent(bookId, title, oldPrice, newPrice) { CorrelationId = CorrelationId },
            Topology.RoutingKeys.BookPriceChanged, bookId, ct);

    public Task RenamedAsync(int bookId, string oldTitle, string newTitle, CancellationToken ct)
        => PublishAsync(new BookRenamedEvent(bookId, oldTitle, newTitle) { CorrelationId = CorrelationId },
            Topology.RoutingKeys.BookRenamed, bookId, ct);

    public Task DeletedAsync(int bookId, CancellationToken ct)
        => PublishAsync(new BookDeletedEvent(bookId) { CorrelationId = CorrelationId },
            Topology.RoutingKeys.BookDeleted, bookId, ct);

    private async Task PublishAsync<TEvent>(TEvent @event, string routingKey, int bookId, CancellationToken ct)
        where TEvent : IntegrationEvent
    {
        try
        {
            await _publisher.PublishAsync(@event, Topology.BooksExchange, routingKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish {EventType} {EventId} for book {BookId}: change is saved, event is lost",
                typeof(TEvent).Name, @event.EventId, bookId);
        }
    }
}
