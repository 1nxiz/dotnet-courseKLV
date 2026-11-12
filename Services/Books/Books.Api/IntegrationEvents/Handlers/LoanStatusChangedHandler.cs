using Books.Api.IntegrationEvents.Events;
using Books.Bll.Caching;
using Books.Dal.Repositories;
using Platform.EventBus;

namespace Books.Api.IntegrationEvents.Handlers;

/// <summary>
/// Черга books.loans-sync: видача отримала статус Issued -> збільшити лічильник TimesLoaned книг (популярність у каталозі).
/// Ідемпотентність: EventId записується в ProcessedEvents тим самим SaveChangesAsync, що й лічильники — одна транзакція;
/// повторна доставка тієї самої події пропускається.
/// </summary>
public sealed class LoanStatusChangedHandler : IIntegrationEventHandler<LoanStatusChangedEvent>
{
    private const string Issued = "Issued";
    private readonly IUnitOfWork _uow;
    private readonly IBooksCache _cache;
    private readonly ILogger<LoanStatusChangedHandler> _logger;

    public LoanStatusChangedHandler(IUnitOfWork uow, IBooksCache cache, ILogger<LoanStatusChangedHandler> logger)
    {
        _uow = uow;
        _cache = cache;
        _logger = logger;
    }

    public async Task HandleAsync(LoanStatusChangedEvent e, CancellationToken ct)
    {
        if (await _uow.ProcessedEvents.ExistsAsync(e.EventId, ct))
        {
            _logger.LogInformation("Duplicate event {EventId} ({EventType}) for loan {LoanId} skipped",
                e.EventId, nameof(LoanStatusChangedEvent), e.LoanId);
            return;
        }

        var touched = new List<int>();
        if (e.NewStatus == Issued)
        {
            foreach (var line in e.Items)
            {
                var book = await _uow.Books.GetByIdAsync((int)line.BookId, ct);
                if (book is null)
                {
                    _logger.LogWarning("Loan {LoanId} references book {BookId} that is not in the catalog", e.LoanId, line.BookId);
                    continue;
                }
                book.RegisterLoan(line.Quantity);
                touched.Add(book.Id);
            }
        }

        _uow.ProcessedEvents.Add(e.EventId, nameof(LoanStatusChangedEvent));
        await _uow.SaveChangesAsync(ct);                       // лічильники + ProcessedEvents — одна транзакція

        if (touched.Count > 0)
            await _cache.InvalidateAsync("LoanIssued", ct, touched.Select(CacheKeys.Book).ToArray());
        _logger.LogInformation("Loan {LoanId} ({LoanNumber}) {OldStatus} -> {NewStatus}: {BooksCount} books updated",
            e.LoanId, e.Number, e.OldStatus, e.NewStatus, touched.Count);
    }
}
