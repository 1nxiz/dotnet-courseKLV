using System.Diagnostics;
using System.Text.Json;
using Grpc.Core;
using Microsoft.Extensions.Caching.Distributed;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Services;

/// <summary>
/// Aggregator Pattern. Видача читається першою (без неї агрегувати нічого), потім каталог і відгуки опитуються
/// ПАРАЛЕЛЬНО (Task.WhenAll) — через gRPC (типово) або HTTP (для порівняння). Недоступність каталогу чи відгуків
/// не «валить» запит: відповідь містить доступні дані й перелік попереджень.
/// Повна (без попереджень) відповідь кешується в Redis на 30 с — коротше за TTL кешів самих сервісів;
/// часткова — ніколи (інакше збій закріпився б у кеші).
/// </summary>
public sealed class LoanCardAggregator
{
    public static readonly ActivitySource ActivitySource = new("Platform.Aggregator");
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly GrpcUpstream _grpc;
    private readonly HttpUpstream _http;
    private readonly IDistributedCache _cache;
    private readonly AggregatorMetrics _metrics;
    private readonly ILogger<LoanCardAggregator> _logger;

    public LoanCardAggregator(GrpcUpstream grpc, HttpUpstream http, IDistributedCache cache, AggregatorMetrics metrics,
        ILogger<LoanCardAggregator> logger)
    {
        _grpc = grpc;
        _http = http;
        _cache = cache;
        _metrics = metrics;
        _logger = logger;
    }

    public static string CacheKey(long loanId) => $"aggregator:loan:{loanId}:full";

    /// <param name="useCache">false — обійти кеш (вимірювання чистої затримки транспорту).</param>
    public async Task<LoanCardDto?> GetLoanCardAsync(long loanId, UpstreamTransport transport, bool useCache, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("AggregateLoanCard");
        activity?.SetTag("loan.id", loanId);
        activity?.SetTag("aggregator.transport", transport.ToString().ToLowerInvariant());

        // ---- кеш перевіряється ДО паралельних викликів (лише для основного шляху gRPC) ----
        var cacheable = useCache && transport == UpstreamTransport.Grpc;
        if (cacheable && await TryGetCachedAsync(loanId, ct) is { } cached)
        {
            activity?.SetTag("cache.hit", true);
            return cached;
        }

        var sw = Stopwatch.StartNew();
        IUpstream upstream = transport == UpstreamTransport.Grpc ? _grpc : _http;
        var loan = await upstream.GetLoanAsync(loanId, ct);
        if (loan is null) return null;

        var warnings = new List<string>();
        var bookIds = loan.Items.Select(i => i.BookId).Distinct().ToList();

        var booksTask = SafeAsync(() => upstream.GetBooksAsync(bookIds, ct), "books-api", warnings);
        var reviewsTask = SafeAsync(() => upstream.GetReviewsAsync(bookIds, ct), "feedback-api", warnings);
        await Task.WhenAll(booksTask, reviewsTask);                          // паралельно

        var result = Compose(loan, await booksTask, await reviewsTask, warnings) with
        {
            Transport = transport.ToString().ToLowerInvariant()
        };

        _metrics.Aggregated(result.Transport!, sw.Elapsed.TotalMilliseconds, warnings.Count > 0);
        _logger.LogInformation("Loan card {LoanId} aggregated via {Transport} in {ElapsedMs} ms with {WarningsCount} warnings",
            loanId, result.Transport, sw.ElapsedMilliseconds, warnings.Count);
        activity?.SetTag("aggregation.warnings", warnings.Count);

        if (cacheable && warnings.Count == 0)                                // часткові відповіді не кешуємо
            await TrySetCachedAsync(loanId, result, ct);
        return result;
    }

    internal static LoanCardDto Compose(LoanView loan, IReadOnlyList<BookView>? books, IReadOnlyList<BookReviewsView>? reviews,
        IReadOnlyList<string> warnings)
    {
        var bookIds = loan.Items.Select(i => i.BookId).ToHashSet();
        // узгодженість: беремо лише книги й відгуки, що належать САМЕ цій видачі
        var booksById = (books ?? []).Where(b => bookIds.Contains(b.Id)).ToDictionary(b => (long)b.Id);
        var reviewsByBook = (reviews ?? []).Where(r => bookIds.Contains(r.BookId)).ToDictionary(r => r.BookId);

        var items = loan.Items.Select(item =>
        {
            booksById.TryGetValue(item.BookId, out var book);
            reviewsByBook.TryGetValue(item.BookId, out var summary);
            return new LoanCardBookDto(
                item.BookId, item.BookTitle, item.UnitPrice, item.Quantity,
                book is null ? null : new CatalogInfoDto(book.Title, book.AuthorName, book.Price, book.TotalCopies, book.IsPublished),
                summary?.ReviewsCount,
                summary is { ReviewsCount: > 0 } ? summary.AverageRating : null,
                summary?.Latest);
        }).ToList();

        return new LoanCardDto(loan.Id, loan.Number, loan.Status, loan.ReaderId, loan.LoanDate, loan.DueDate, loan.Total,
            items, warnings);
    }

    private async Task<LoanCardDto?> TryGetCachedAsync(long loanId, CancellationToken ct)
    {
        try
        {
            var bytes = await _cache.GetAsync(CacheKey(loanId), ct);
            if (bytes is null)
            {
                _metrics.CacheMiss();
                _logger.LogDebug("Aggregator cache miss {CacheKey}", CacheKey(loanId));
                return null;
            }
            _metrics.CacheHit();
            _logger.LogInformation("Aggregator cache hit {CacheKey}", CacheKey(loanId));
            return JsonSerializer.Deserialize<LoanCardDto>(bytes, Json)! with { FromCache = true };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Aggregator cache read failed for {CacheKey}; continuing without cache", CacheKey(loanId));
            return null;
        }
    }

    private async Task TrySetCachedAsync(long loanId, LoanCardDto card, CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(CacheKey(loanId), JsonSerializer.SerializeToUtf8Bytes(card, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Aggregator cache write failed for {CacheKey}", CacheKey(loanId));
        }
    }

    /// <summary>Виклик із перехопленням часткової відмови: замість винятку — попередження.</summary>
    private async Task<T?> SafeAsync<T>(Func<Task<T>> call, string service, List<string> warnings) where T : class
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await call();
            _logger.LogInformation("{Service} responded in {ElapsedMs} ms", service, sw.ElapsedMilliseconds);
            return result;
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
                                          or StatusCode.Internal or StatusCode.Unknown or StatusCode.ResourceExhausted)
        {
            return Fail(ex, $"{ex.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException
                                     || ex.GetType().Name.Contains("BrokenCircuit", StringComparison.Ordinal))
        {
            return Fail(ex, ex.GetType().Name);
        }

        T? Fail(Exception ex, string reason)
        {
            _logger.LogWarning(ex, "{Service} is unavailable ({Reason}) after {ElapsedMs} ms", service, reason, sw.ElapsedMilliseconds);
            lock (warnings) warnings.Add($"{service} is unavailable");
            return null;
        }
    }
}
