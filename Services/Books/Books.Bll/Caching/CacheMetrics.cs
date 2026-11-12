using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Books.Bll.Caching;

/// <summary>
/// Метрики кешу (Meter "Books.Cache", підписаний у ServiceDefaults): попадання й промахи окремо для L1 (memory)
/// і L2 (redis) — з них Dashboard рахує hit ratio; інвалідації, помилки Redis і тривалість операцій.
/// </summary>
public sealed class CacheMetrics
{
    public const string MeterName = "Books.Cache";

    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _invalidations;
    private readonly Counter<long> _errors;
    private readonly Histogram<double> _duration;

    public CacheMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _hits = meter.CreateCounter<long>("cache.hits", "{hit}", "Cache hits by level (memory / redis)");
        _misses = meter.CreateCounter<long>("cache.misses", "{miss}", "Cache misses by level (memory / redis)");
        _invalidations = meter.CreateCounter<long>("cache.invalidations", "{key}", "Keys removed after writes");
        _errors = meter.CreateCounter<long>("cache.errors", "{error}", "Failed distributed cache operations");
        _duration = meter.CreateHistogram<double>("cache.operation.duration", "ms", "Duration of cache lookups");
    }

    public void Hit(string level, string entity) => _hits.Add(1, Tags(level, entity));
    public void Miss(string level, string entity) => _misses.Add(1, Tags(level, entity));
    public void Invalidated(string reason) => _invalidations.Add(1, new KeyValuePair<string, object?>("cache.reason", reason));
    public void Error(string operation) => _errors.Add(1, new KeyValuePair<string, object?>("cache.operation", operation));
    public void Duration(string level, double ms) => _duration.Record(ms, new KeyValuePair<string, object?>("cache.level", level));

    private static TagList Tags(string level, string entity) => new() { { "cache.level", level }, { "cache.entity", entity } };
}
