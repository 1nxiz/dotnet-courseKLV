namespace Books.Dal.Entities;

/// <summary>
/// Оброблена integration event (Лаб. 08): EventId — первинний ключ. Запис додається в ТІЙ САМІЙ транзакції
/// (одному SaveChanges), що й бізнес-зміна, тож повторна доставка події не змінює дані вдруге.
/// </summary>
public class ProcessedEvent
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = "";
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
