using Platform.EventBus;

namespace Loans.Api.IntegrationEvents.Events;

// Integration events сервісу видач (Лаб. 08). Незмінні record-и з EventId / CorrelationId (базовий IntegrationEvent).

/// <summary>Позиція видачі: яка книга і скільки примірників.</summary>
public sealed record LoanBookLine(long BookId, int Quantity);

/// <summary>Статус видачі змінився (routing key loan.status_changed) — Каталог рахує видачі книг (TimesLoaned).</summary>
public sealed record LoanStatusChangedEvent(long LoanId, string Number, string OldStatus, string NewStatus,
    IReadOnlyList<LoanBookLine> Items) : IntegrationEvent;

// ---- події, які сервіс видач СПОЖИВАЄ (контракт Каталогу; імена й поля збігаються з Books.Api/IntegrationEvents) ----

public sealed record BookCreatedEvent(int BookId, string Title, string Isbn, decimal Price) : IntegrationEvent;

public sealed record BookPriceChangedEvent(int BookId, string Title, decimal OldPrice, decimal NewPrice) : IntegrationEvent;

public sealed record BookRenamedEvent(int BookId, string OldTitle, string NewTitle) : IntegrationEvent;

public sealed record BookDeletedEvent(int BookId) : IntegrationEvent;
