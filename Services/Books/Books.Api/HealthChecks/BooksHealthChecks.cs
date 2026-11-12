using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Books.Api.HealthChecks;

/// <summary>
/// Health checks каталогу (Лаб. 07): SQL Server (BooksDb) — обов'язкова залежність (Unhealthy),
/// Redis — кеш (Degraded). Обидві з тегом "ready" — потрапляють у /health/ready і в gRPC Health.
/// </summary>
public static class BooksHealthChecks
{
    public static IServiceCollection AddBooksHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var checks = services.AddHealthChecks()
            .AddSqlServer(
                connectionString: configuration.GetConnectionString("BooksDb")!,
                name: "books-db",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["database", "sql", "ready"],
                timeout: TimeSpan.FromSeconds(5));

        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("redis")))
            checks.AddRedis(
                sp => sp.GetRequiredService<IConnectionMultiplexer>(),
                name: "redis-cache",
                failureStatus: HealthStatus.Degraded,
                tags: ["cache", "redis", "ready"],
                timeout: TimeSpan.FromSeconds(3));

        services.AddGrpcHealthChecks(o => o.Services.Map("", r => r.Tags.Contains("ready")));
        return services;
    }
}
