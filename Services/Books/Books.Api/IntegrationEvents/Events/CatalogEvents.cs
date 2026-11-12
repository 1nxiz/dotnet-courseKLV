using Platform.EventBus;

namespace Books.Api.IntegrationEvents.Events;

// Integration events Каталогу (Лаб. 08) — публічний контракт сервісу. Незмінні record-и з EventId / CorrelationId
// (базовий IntegrationEvent), самодостатні: споживачам не потрібно звертатися до Каталогу по додаткові дані.
// Ім'я типу передається в BasicProperties.Type — споживачі мають record-и з тими самими іменами й полями.

/// <summary>Нова книга в каталозі (routing key book.created) — Loans додає її в локальну копію довідника.</summary>
public sealed record BookCreatedEvent(int BookId, string Title, string Isbn, decimal Price) : IntegrationEvent;

/// <summary>Змінилася ціна (book.price_changed) — Loans оновлює Books.Price; ціни в оформлених видачах не змінюються.</summary>
public sealed record BookPriceChangedEvent(int BookId, string Title, decimal OldPrice, decimal NewPrice) : IntegrationEvent;

/// <summary>Змінилася назва (book.renamed) — Loans оновлює Books.Title, Feedback — bookTitle у відгуках і обговореннях.</summary>
public sealed record BookRenamedEvent(int BookId, string OldTitle, string NewTitle) : IntegrationEvent;

/// <summary>Книгу видалено з каталогу (book.deleted) — Loans м'яко видаляє локальну копію.</summary>
public sealed record BookDeletedEvent(int BookId) : IntegrationEvent;

// ---- події, які Каталог СПОЖИВАЄ (контракт сервісу видач) ----

/// <summary>Позиція видачі в події сервісу видач.</summary>
public sealed record LoanBookLine(long BookId, int Quantity);

/// <summary>Статус видачі змінився (loan.status_changed від loans-api).</summary>
public sealed record LoanStatusChangedEvent(long LoanId, string Number, string OldStatus, string NewStatus,
    IReadOnlyList<LoanBookLine> Items) : IntegrationEvent;
