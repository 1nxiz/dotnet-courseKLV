using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;

namespace Books.Api.Metrics;

/// <summary>
/// Бізнес-метрики каталогу (Meter "Books.Api"). Singleton. Теги з малою кардинальністю:
/// operation, outcome, price.direction (up / down), copies.operation (add / write_off).
/// </summary>
public sealed class BooksMetrics
{
    public const string MeterName = "Books.Api";

    private readonly Counter<long> _created;
    private readonly Counter<long> _priceChanged;
    private readonly Counter<long> _copiesChanged;
    private readonly Counter<long> _published;
    private readonly Histogram<double> _duration;

    public BooksMetrics(IMeterFactory meterFactory, IMemoryCache cache)
    {
        var meter = meterFactory.Create(MeterName);
        _created = meter.CreateCounter<long>("books.created", "{book}", "Number of books added to the catalog");
        _priceChanged = meter.CreateCounter<long>("books.price_changed", "{change}", "Price changes by direction");
        _copiesChanged = meter.CreateCounter<long>("books.copies.changed", "{copy}", "Copies added or written off");
        _published = meter.CreateCounter<long>("books.published", "{book}", "Number of published books");
        _duration = meter.CreateHistogram<double>("books.operation.duration", "ms", "Duration of catalog write operations");

        // gauge: поточна кількість записів L1-кешу (знімок у момент збору)
        meter.CreateObservableGauge("books.cache.entries",
            () => (cache as MemoryCache)?.GetCurrentStatistics()?.CurrentEntryCount ?? 0,
            "{entry}", "Entries currently held in the in-memory cache");
    }

    public void BookCreated() => _created.Add(1);
    public void PriceChanged(decimal oldPrice, decimal newPrice)
        => _priceChanged.Add(1, new KeyValuePair<string, object?>("price.direction", newPrice >= oldPrice ? "up" : "down"));
    public void CopiesChanged(string operation, int quantity)
        => _copiesChanged.Add(quantity, new KeyValuePair<string, object?>("copies.operation", operation));
    public void Published() => _published.Add(1);

    /// <summary>Тривалість УСІХ виконань — і успішних, і невдалих (тег outcome).</summary>
    public async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();
        var outcome = "failure";
        try
        {
            var result = await action();
            outcome = "success";
            return result;
        }
        finally
        {
            _duration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("operation", operation),
                new KeyValuePair<string, object?>("outcome", outcome));
        }
    }
}
