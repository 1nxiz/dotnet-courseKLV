using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Platform.EventBus;

public interface IEventPublisher
{
    /// <summary>Публікує подію в exchange з routing key. Викликати ЛИШЕ після успішного збереження змін у БД.</summary>
    Task PublishAsync<TEvent>(TEvent @event, string exchange, string routingKey, CancellationToken ct = default)
        where TEvent : IntegrationEvent;
}

/// <summary>
/// Публікація в RabbitMQ: JSON (System.Text.Json, camelCase), persistent-доставка, MessageId = EventId, Type = ім'я типу події,
/// заголовки x-correlation-id, x-schema-version і traceparent. Один канал з publisher confirms (брокер підтверджує,
/// що прийняв повідомлення) на весь застосунок; доступ серіалізовано семафором (IChannel не потокобезпечний для publish).
/// </summary>
public sealed class RabbitMqEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly EventBusMetrics _metrics;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public RabbitMqEventPublisher(IConnection connection, EventBusMetrics metrics, ILogger<RabbitMqEventPublisher> logger)
    {
        _connection = connection;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, string exchange, string routingKey, CancellationToken ct = default)
        where TEvent : IntegrationEvent
    {
        var eventType = typeof(TEvent).Name;
        using var activity = TracePropagation.StartPublishActivity(exchange, routingKey, @event);
        var props = new BasicProperties
        {
            Persistent = true,                                   // переживе перезапуск брокера (черги durable)
            ContentType = "application/json",
            MessageId = @event.EventId.ToString(),
            Type = eventType,
            Timestamp = new AmqpTimestamp(new DateTimeOffset(@event.OccurredAt).ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                ["x-correlation-id"] = @event.CorrelationId,
                ["x-schema-version"] = @event.SchemaVersion
            }
        };
        TracePropagation.Inject(activity, props);
        var body = JsonSerializer.SerializeToUtf8Bytes(@event, EventJson.Options);

        await _lock.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);
            await channel.BasicPublishAsync(exchange, routingKey, mandatory: false, props, body, ct);   // чекає confirm
        }
        finally
        {
            _lock.Release();
        }

        _metrics.Published(eventType);
        _logger.LogInformation("Published {EventType} {EventId} to {Exchange}/{RoutingKey}, CorrelationId {CorrelationId}",
            eventType, @event.EventId, exchange, routingKey, @event.CorrelationId);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;
        if (_channel is not null) await _channel.DisposeAsync();
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        await Topology.DeclareAsync(_channel, ct);               // exchange існує ще до першої публікації
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        _lock.Dispose();
    }
}

/// <summary>Без брокера (локальний запуск без RabbitMQ, тести): подія лише логуються.</summary>
public sealed class LoggingOnlyEventPublisher(ILogger<LoggingOnlyEventPublisher> logger) : IEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent @event, string exchange, string routingKey, CancellationToken ct = default)
        where TEvent : IntegrationEvent
    {
        logger.LogWarning("Event bus is not configured: {EventType} {EventId} for {Exchange}/{RoutingKey} was not published",
            typeof(TEvent).Name, @event.EventId, exchange, routingKey);
        return Task.CompletedTask;
    }
}
