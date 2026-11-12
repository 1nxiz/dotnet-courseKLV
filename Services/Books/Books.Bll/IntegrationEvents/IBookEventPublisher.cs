namespace Books.Bll.IntegrationEvents;

/// <summary>
/// Порт публікації подій Каталогу (Лаб. 08). BLL не знає ні про RabbitMQ, ні про формат повідомлень:
/// реалізація (адаптер) — у Books.Api/IntegrationEvents. Викликається ЛИШЕ після успішного SaveChangesAsync —
/// якщо транзакція БД не вдалася, подія не публікується.
/// </summary>
public interface IBookEventPublisher
{
    Task BookCreatedAsync(int bookId, string title, string isbn, decimal price, CancellationToken ct);
    Task PriceChangedAsync(int bookId, string title, decimal oldPrice, decimal newPrice, CancellationToken ct);
    Task RenamedAsync(int bookId, string oldTitle, string newTitle, CancellationToken ct);
    Task DeletedAsync(int bookId, CancellationToken ct);
}
