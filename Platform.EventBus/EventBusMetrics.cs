using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Platform.EventBus;

/// <summary>Метрики шини (Meter "Platform.EventBus"): публікації, результати обробки, тривалість, глибина DLQ (gauge).</summary>
public sealed class EventBusMetrics
{
    public const string MeterName = "Platform.EventBus";

    private readonly Counter<long> _published;
    private readonly Counter<long> _consumed;
    private readonly Histogram<double> _duration;
    private readonly ConcurrentDictionary<string, long> _dlqDepth = new();

    public EventBusMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _published = meter.CreateCounter<long>("eventbus.messages.published", "{message}", "Published integration events");
        _consumed = meter.CreateCounter<long>("eventbus.messages.consumed", "{message}",
            "Processed messages by outcome (ack / retry / dead_letter / duplicate)");
        _duration = meter.CreateHistogram<double>("eventbus.processing.duration", "ms", "Message processing time");
        meter.CreateObservableGauge("eventbus.dlq.depth",
            () => _dlqDepth.Select(kv => new Measurement<long>(kv.Value, new KeyValuePair<string, object?>("queue", kv.Key))),
            "{message}", "Messages waiting in dead letter queues");
    }

    public void Published(string eventType) => _published.Add(1, new KeyValuePair<string, object?>("event.type", eventType));

    public void Consumed(string eventType, string outcome, double ms)
    {
        _consumed.Add(1, new("event.type", eventType), new("outcome", outcome));
        _duration.Record(ms, new KeyValuePair<string, object?>("event.type", eventType), new("outcome", outcome));
    }

    public void DeadLetterDepth(string queue, long depth) => _dlqDepth[queue] = depth;
}
