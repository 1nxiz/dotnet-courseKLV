using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Books.Bll.Caching;

public interface IBooksCache
{
    /// <summary>Cache-aside: L1 (IMemoryCache) -> L2 (Redis) -> фабрика (БД); рівні заповнюються на зворотному шляху.</summary>
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CachePolicy policy, CancellationToken ct) where T : class;

    /// <summary>Синхронна з записом інвалідація обох рівнів.</summary>
    Task InvalidateAsync(string reason, CancellationToken ct, params string[] keys);
}

/// <summary>
/// Дворівневий кеш. L1 — пам'ять процесу (найшвидше, лише цей екземпляр), L2 — Redis через IDistributedCache
/// (спільний для всіх реплік). Відмова Redis не ламає запит: помилка логуються, і на 30 с L2 пропускається
/// (сервіс працює повільніше — з БД, але працює).
/// </summary>
public sealed class TwoLevelCache : IBooksCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan L2Backoff = TimeSpan.FromSeconds(30);

    private readonly IMemoryCache _memory;
    private readonly IDistributedCache _distributed;
    private readonly CacheMetrics _metrics;
    private readonly ILogger<TwoLevelCache> _logger;
    private static long _l2DisabledUntilTicks;                       // спільно для всіх scope-ів процесу

    public TwoLevelCache(IMemoryCache memory, IDistributedCache distributed, CacheMetrics metrics, ILogger<TwoLevelCache> logger)
    {
        _memory = memory;
        _distributed = distributed;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CachePolicy policy, CancellationToken ct)
        where T : class
    {
        var entity = EntityOf(key);
        var sw = Stopwatch.StartNew();

        // ---- L1 ----
        if (_memory.TryGetValue(key, out T? value) && value is not null)
        {
            _metrics.Hit("memory", entity);
            _metrics.Duration("memory", sw.Elapsed.TotalMilliseconds);
            _logger.LogDebug("Cache hit {CacheLevel} {CacheKey}", "memory", key);
            return value;
        }
        _metrics.Miss("memory", entity);

        // ---- L2 ----
        var bytes = await TryGetL2Async(key, ct);
        if (bytes is not null)
        {
            value = JsonSerializer.Deserialize<T>(bytes, Json)!;
            SetL1(key, value, policy);
            _metrics.Hit("redis", entity);
            _metrics.Duration("redis", sw.Elapsed.TotalMilliseconds);
            _logger.LogDebug("Cache hit {CacheLevel} {CacheKey}, {SizeBytes} bytes", "redis", key, bytes.Length);
            return value;
        }
        _metrics.Miss("redis", entity);

        // ---- БД ----
        _logger.LogDebug("Cache miss {CacheKey}, loading from database", key);
        value = await factory(ct);
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        await TrySetL2Async(key, payload, policy, ct);
        SetL1(key, value, policy);
        _logger.LogDebug("Cached {CacheKey}: {SizeBytes} bytes, L1 {L1Ttl}, L2 {L2Ttl}", key, payload.Length, policy.L1Absolute, policy.L2Absolute);
        return value;
    }

    public async Task InvalidateAsync(string reason, CancellationToken ct, params string[] keys)
    {
        foreach (var key in keys)
        {
            _memory.Remove(key);
            if (L2Available())
            {
                try { await _distributed.RemoveAsync(key, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { L2Failed("remove", key, ex); }
            }
            _metrics.Invalidated(reason);
        }
        _logger.LogInformation("Cache invalidated {CacheKeys}, reason {Reason}", keys, reason);
    }

    private void SetL1<T>(string key, T value, CachePolicy policy) where T : class
    {
        _memory.Set(key, value, new MemoryCacheEntryOptions
        {
            Size = 1,                                                   // SizeLimit рахується в записах
            SlidingExpiration = policy.L1Sliding,
            AbsoluteExpirationRelativeToNow = policy.L1Absolute,        // L1 коротший за L2
            Priority = (CacheItemPriority)(int)policy.Priority
        }.RegisterPostEvictionCallback(static (k, _, reason, state) =>
        {
            if (reason is EvictionReason.Capacity && state is ILogger logger)
                logger.LogDebug("Cache entry {CacheKey} evicted from memory: {EvictionReason}", k, reason);
        }, _logger));
    }

    private async Task<byte[]?> TryGetL2Async(string key, CancellationToken ct)
    {
        if (!L2Available()) return null;
        try { return await _distributed.GetAsync(key, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { L2Failed("get", key, ex); return null; }
    }

    private async Task TrySetL2Async(string key, byte[] payload, CachePolicy policy, CancellationToken ct)
    {
        if (!L2Available()) return;
        try
        {
            await _distributed.SetAsync(key, payload,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = policy.L2Absolute }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { L2Failed("set", key, ex); }
    }

    private static bool L2Available() => DateTime.UtcNow.Ticks >= Interlocked.Read(ref _l2DisabledUntilTicks);

    private void L2Failed(string operation, string key, Exception ex)
    {
        _metrics.Error(operation);
        Interlocked.Exchange(ref _l2DisabledUntilTicks, DateTime.UtcNow.Add(L2Backoff).Ticks);
        _logger.LogWarning(ex, "Distributed cache {Operation} failed for {CacheKey}; L2 is bypassed for {Backoff}", operation, key, L2Backoff);
    }

    private static string EntityOf(string key) => key.Split(':') is { Length: > 1 } parts ? parts[1] : key;
}
