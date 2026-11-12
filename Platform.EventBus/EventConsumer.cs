using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Platform.EventBus;

/// <summary>Підписка: черга і відповідність «тип події (BasicProperties.Type) → обробник».</summary>
public sealed class ConsumerRegistration
{
    internal ConsumerRegistration(string queue) => Queue = queue;

    public string Queue { get; }
    public ushort PrefetchCount { get; set; } = 10;
    public int MaxRetries { get; set; } = 5;

    internal Dictionary<string, (Type EventType, Func<IServiceProvider, object, CancellationToken, Task> Invoke)> Handlers { get; } = new();

    /// <summary>Зіставити тип події з обробником (обробник реєструється в DI як scoped).</summary>
    public ConsumerRegistration On<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        Handlers[typeof(TEvent).Name] = (typeof(TEvent),
            (sp, e, ct) => sp.GetRequiredService<THandler>().HandleAsync((TEvent)e, ct));
        return this;
    }
}

/// <summary>
/// Споживач черги як BackgroundService (Лаб. 08): оголошує топологію, BasicQos (prefetch), BasicConsumeAsync з autoAck: false.
/// Рішення щодо повідомлення:
/// <list type="bullet">
/// <item>успіх — BasicAckAsync (повідомлення видаляється з черги);</item>
/// <item>постійний збій (некоректний JSON, невідомий тип, PermanentEventException) — BasicNackAsync(requeue: false) → DLX → DLQ;</item>
/// <item>тимчасовий збій (БД недоступна, таймаут) — повторна публікація в ту саму чергу з x-retry-count + 1 і ack оригіналу
/// (заголовки повідомлення, поверненого через requeue, змінити не можна); після MaxRetries — у DLQ.</item>
/// </list>
/// Кожне повідомлення обробляється в окремому DI-scope, з відновленим контекстом трасування і CorrelationId у лог-контексті.
/// </summary>
public sealed class EventConsumer : BackgroundService
{
    private readonly ConsumerRegistration _registration;
    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopes;
    private readonly EventBusMetrics _metrics;
    private readonly ILogger<EventConsumer> _logger;
    private IChannel? _channel;

    public EventConsumer(ConsumerRegistration registration, IConnection connection, IServiceScopeFactory scopes,
        EventBusMetrics metrics, ILogger<EventConsumer> logger)
    {
        _registration = registration;
        _connection = connection;
        _scopes = scopes;
        _metrics = metrics;
        _logger = logger;
    }

    private string Queue => _registration.Queue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // брокер може стартувати пізніше за сервіс — не падаємо, а повторюємо підключення
        while (!stoppingToken.IsCancellationRequested && _channel is null)
        {
            try
            {
                var channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await Topology.DeclareAsync(channel, stoppingToken);
                await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: _registration.PrefetchCount, global: false, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += OnMessageAsync;
                await channel.BasicConsumeAsync(Queue, autoAck: false, consumer, stoppingToken);
                _channel = channel;
                _logger.LogInformation("Consuming {Queue}: {EventTypes}, prefetch {Prefetch}, max retries {MaxRetries}",
                    Queue, _registration.Handlers.Keys, _registration.PrefetchCount, _registration.MaxRetries);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Cannot start consumer for {Queue}, retrying in 5 s", Queue);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        // моніторинг глибини DLQ для gauge eventbus.dlq.depth
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _metrics.DeadLetterDepth(Topology.DeadLetterQueue(Queue),
                    await _channel!.MessageCountAsync(Topology.DeadLetterQueue(Queue), stoppingToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Cannot read DLQ depth of {Queue}", Queue);
            }
        }
    }

    private async Task OnMessageAsync(object sender, BasicDeliverEventArgs ea)
    {
        var channel = _channel ?? ((AsyncEventingBasicConsumer)sender).Channel;
        using var activity = TracePropagation.StartConsumerActivity(ea, Queue);
        var eventType = ea.BasicProperties.Type ?? "unknown";
        var correlationId = ea.BasicProperties.Headers is { } h && h.TryGetValue("x-correlation-id", out var c)
            ? TracePropagation.HeaderToString(c) : null;
        var attempt = RetryCount(ea);
        var sw = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["EventType"] = eventType,
            ["EventId"] = ea.BasicProperties.MessageId,
            ["Queue"] = Queue,
            ["Attempt"] = attempt
        });

        try
        {
            if (!_registration.Handlers.TryGetValue(eventType, out var handler))
                throw new PermanentEventException($"No handler for event type '{eventType}' in {Queue}");

            var @event = JsonSerializer.Deserialize(ea.Body.Span, handler.EventType, EventJson.Options)
                         ?? throw new JsonException("Empty message body");
            _logger.LogInformation("Received {EventType} {EventId} from {Queue} (attempt {Attempt})",
                eventType, ea.BasicProperties.MessageId, Queue, attempt);

            await using (var di = _scopes.CreateAsyncScope())
                await handler.Invoke(di.ServiceProvider, @event, CancellationToken.None);   // обробка не переривається на половині

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            _metrics.Consumed(eventType, "ack", sw.Elapsed.TotalMilliseconds);
            _logger.LogInformation("Acked {EventType} {EventId} in {ElapsedMs} ms", eventType, ea.BasicProperties.MessageId,
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is JsonException or PermanentEventException or NotSupportedException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Permanent failure of {EventType} {EventId} in {Queue}, sending to DLQ",
                eventType, ea.BasicProperties.MessageId, Queue);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);   // -> platform.dlx -> DLQ
            _metrics.Consumed(eventType, "dead_letter", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await RetryOrDeadLetterAsync(channel, ea, attempt, eventType, ex, sw);
        }
    }

    private async Task RetryOrDeadLetterAsync(IChannel channel, BasicDeliverEventArgs ea, int attempt, string eventType,
        Exception ex, Stopwatch sw)
    {
        if (attempt >= _registration.MaxRetries)
        {
            _logger.LogError(ex, "{EventType} {EventId} failed after {Attempts} retries, sending to DLQ",
                eventType, ea.BasicProperties.MessageId, attempt);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            _metrics.Consumed(eventType, "dead_letter", sw.Elapsed.TotalMilliseconds);
            return;
        }

        // експоненційна затримка перед повтором (1, 2, 4, 8, 10 с) — тимчасовому збою (перезапуск БД) треба час минути
        await Task.Delay(TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), 10)));
        var props = new BasicProperties(ea.BasicProperties)
        {
            Headers = new Dictionary<string, object?>(ea.BasicProperties.Headers ?? new Dictionary<string, object?>())
            {
                ["x-retry-count"] = attempt + 1
            }
        };
        // через default exchange прямо в СВОЮ чергу: інші сервіси, підписані на той самий exchange, дубля не отримують
        await channel.BasicPublishAsync(string.Empty, Queue, mandatory: false, props, ea.Body);
        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        _metrics.Consumed(eventType, "retry", sw.Elapsed.TotalMilliseconds);
        _logger.LogWarning(ex, "{EventType} {EventId} failed: {Reason}; retry {Attempt}/{MaxRetries}",
            eventType, ea.BasicProperties.MessageId, ex.Message, attempt + 1, _registration.MaxRetries);
    }

    private static int RetryCount(BasicDeliverEventArgs ea)
        => ea.BasicProperties.Headers is { } headers && headers.TryGetValue("x-retry-count", out var v) && v is not null
            ? Convert.ToInt32(v)
            : 0;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is null) return;
        try
        {
            if (_channel.IsOpen)
                await _channel.CloseAsync(cancellationToken);          // непідтверджені повідомлення повернуться в чергу
            await _channel.DisposeAsync();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or RabbitMQ.Client.Exceptions.AlreadyClosedException
                                       or OperationCanceledException)
        {
            // з'єднання вже закрите під час зупинки хоста — каналу нічого закривати
            _logger.LogDebug(ex, "Channel of {Queue} was already closed", Queue);
        }
    }
}
