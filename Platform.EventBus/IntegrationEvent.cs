using System.Text.Json;
using System.Text.Json.Serialization;

namespace Platform.EventBus;

/// <summary>
/// Базовий тип integration event: незмінний record (порівняння за значенням), самодостатній — споживач не звертається
/// до сервісу-джерела по додаткові дані. EventId — ключ ідемпотентності, CorrelationId — бізнес-трасування (Лаб. 05),
/// OccurredAt — захист копій від застосування застарілої події (out-of-order), SchemaVersion — еволюція контракту
/// (нові поля лише додаються, наявні не перейменовуються й не видаляються).
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Єдині правила серіалізації для publisher і consumer: camelCase, enum рядками, null не пишеться.</summary>
public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

/// <summary>Обробник подій одного типу. Створюється в окремому DI-scope на кожне повідомлення.</summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}

/// <summary>Постійний збій (некоректні дані, порушене бізнес-правило): повтор не допоможе — повідомлення одразу йде в DLQ.</summary>
public sealed class PermanentEventException(string message, Exception? inner = null) : Exception(message, inner);
