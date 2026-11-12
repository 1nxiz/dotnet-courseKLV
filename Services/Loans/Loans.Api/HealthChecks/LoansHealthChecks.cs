using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Loans.Api.HealthChecks;

/// <summary>
/// Health checks сервісу видач (Лаб. 07). Теги: "ready" — входить у readiness (/health/ready), "live" — у liveness.
/// БД — обов'язкова залежність (Unhealthy); Redis — кеш, необов'язковий (Degraded: сервіс читає з БД).
/// </summary>
public static class LoansHealthChecks
{
    public static IServiceCollection AddLoansHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var checks = services.AddHealthChecks()
            .AddSqlServer(                                         // SELECT 1 — легкий запит
                connectionString: configuration.GetConnectionString("LoansDb")!,
                name: "loans-db",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["database", "sql", "ready"],
                timeout: TimeSpan.FromSeconds(5));

        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("redis")))
            checks.AddRedis(                                       // PING
                sp => sp.GetRequiredService<IConnectionMultiplexer>(),
                name: "redis-cache",
                failureStatus: HealthStatus.Degraded,              // кеш необов'язковий
                tags: ["cache", "redis", "ready"],
                timeout: TimeSpan.FromSeconds(3));

        // gRPC Health Checking Protocol (grpc.health.v1.Health/Check) за тими самими readiness-перевірками
        services.AddGrpcHealthChecks(o => o.Services.Map("", r => r.Tags.Contains("ready")));
        return services;
    }
}
