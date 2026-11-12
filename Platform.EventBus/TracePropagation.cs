using System.Diagnostics;
using System.Text;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Platform.EventBus;

/// <summary>
/// Передача контексту трасування через брокер (W3C traceparent / tracestate у заголовках повідомлення).
/// ActivitySource "Platform.Messaging" підписаний у ServiceDefaults — у Dashboard один trace:
/// HTTP-запит → publish → process в іншому сервісі → запити до БД.
/// </summary>
public static class TracePropagation
{
    public const string SourceName = "Platform.Messaging";
    public static readonly ActivitySource Source = new(SourceName);
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public static Activity? StartPublishActivity(string exchange, string routingKey, IntegrationEvent @event)
    {
        var activity = Source.StartActivity($"{exchange} publish", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", exchange);
        activity?.SetTag("messaging.rabbitmq.destination.routing_key", routingKey);
        activity?.SetTag("messaging.message.id", @event.EventId.ToString());
        activity?.SetTag("event.type", @event.GetType().Name);
        return activity;
    }

    public static void Inject(Activity? activity, BasicProperties props)
    {
        var context = activity?.Context ?? Activity.Current?.Context;
        if (context is null) return;
        props.Headers ??= new Dictionary<string, object?>();
        Propagator.Inject(new PropagationContext(context.Value, Baggage.Current), props.Headers,
            static (headers, key, value) => headers[key] = value);
    }

    public static Activity? StartConsumerActivity(BasicDeliverEventArgs ea, string queue)
    {
        var parent = Propagator.Extract(default, ea.BasicProperties.Headers, static (headers, key) =>
            headers is not null && headers.TryGetValue(key, out var v) ? [HeaderToString(v)] : []);
        Baggage.Current = parent.Baggage;

        var activity = Source.StartActivity($"{queue} process", ActivityKind.Consumer, parent.ActivityContext);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", queue);
        activity?.SetTag("messaging.message.id", ea.BasicProperties.MessageId);
        activity?.SetTag("event.type", ea.BasicProperties.Type);
        return activity;
    }

    /// <summary>Рядкові заголовки AMQP приходять як byte[].</summary>
    public static string HeaderToString(object? value) => value switch
    {
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        null => string.Empty,
        _ => value.ToString() ?? string.Empty
    };
}
