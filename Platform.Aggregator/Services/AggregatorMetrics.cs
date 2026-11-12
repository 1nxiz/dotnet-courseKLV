using System.Diagnostics.Metrics;

namespace Platform.Aggregator.Services;

/// <summary>
/// Метрики агрегатора: тривалість агрегації за транспортом (порівняння gRPC vs HTTP у Dashboard),
/// попадання/промахи кешу складених відповідей у Redis, часткові відповіді.
/// </summary>
public sealed class AggregatorMetrics
{
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _partial;
    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;

    public AggregatorMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("Platform.Aggregator");
        _duration = meter.CreateHistogram<double>("aggregator.request.duration", "ms", "Loan card aggregation time by transport");
        _partial = meter.CreateCounter<long>("aggregator.partial_responses", "{response}", "Responses with warnings");

        var cacheMeter = meterFactory.Create("Platform.Aggregator.Cache");
        _hits = cacheMeter.CreateCounter<long>("cache.hits", "{hit}");
        _misses = cacheMeter.CreateCounter<long>("cache.misses", "{miss}");
    }

    public void Aggregated(string transport, double ms, bool partial)
    {
        _duration.Record(ms, new KeyValuePair<string, object?>("transport", transport));
        if (partial) _partial.Add(1, new KeyValuePair<string, object?>("transport", transport));
    }

    public void CacheHit() => _hits.Add(1, new KeyValuePair<string, object?>("cache.level", "redis"));
    public void CacheMiss() => _misses.Add(1, new KeyValuePair<string, object?>("cache.level", "redis"));
}
