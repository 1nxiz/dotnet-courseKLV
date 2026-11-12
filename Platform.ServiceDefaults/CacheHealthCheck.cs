using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Статистика L1-кешу в health check: кількість записів, оцінений розмір, попадання/промахи й hit ratio
/// (MemoryCacheOptions.TrackStatistics = true). Заповнення понад 90 % ліміту — Degraded.
/// </summary>
public sealed class MemoryCacheHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _services;

    public MemoryCacheHealthCheck(IServiceProvider services) => _services = services;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_services.GetService<IMemoryCache>() is not MemoryCache cache || cache.GetCurrentStatistics() is not { } stats)
            return Task.FromResult(HealthCheckResult.Healthy("Memory cache statistics are not tracked"));

        var total = stats.TotalHits + stats.TotalMisses;
        var data = new Dictionary<string, object>
        {
            ["entries"] = stats.CurrentEntryCount,
            ["estimatedSize"] = stats.CurrentEstimatedSize ?? 0,
            ["hits"] = stats.TotalHits,
            ["misses"] = stats.TotalMisses,
            ["hitRatio"] = total == 0 ? 0 : Math.Round((double)stats.TotalHits / total, 3)
        };
        var limit = _services.GetService<IOptions<MemoryCacheOptions>>()?.Value.SizeLimit;
        if (limit is > 0 && stats.CurrentEstimatedSize > limit * 0.9)
            return Task.FromResult(HealthCheckResult.Degraded("Memory cache is over 90 % of its size limit", data: data));
        return Task.FromResult(HealthCheckResult.Healthy("Memory cache", data));
    }
}
