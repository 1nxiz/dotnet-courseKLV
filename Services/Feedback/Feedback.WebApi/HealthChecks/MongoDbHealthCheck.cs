using Microsoft.Extensions.Diagnostics.HealthChecks;
using Feedback.Infrastructure.Persistence;
using MongoDB.Bson;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Feedback.WebApi.HealthChecks;

/// <summary>MongoDB: команда ping до бази FeedbackDb через зареєстрований MongoDbContext (IMongoClient) — легка й без побічних ефектів.</summary>
public sealed class MongoDbHealthCheck : IHealthCheck
{
    private readonly MongoDbContext _context;

    public MongoDbHealthCheck(MongoDbContext context) => _context = context;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await _context.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
            return HealthCheckResult.Healthy("MongoDB ping OK",
                new Dictionary<string, object> { ["database"] = _context.Database.DatabaseNamespace.DatabaseName });
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "MongoDB is unavailable", ex);
        }
    }
}

public static class FeedbackHealthChecks
{
    /// <summary>MongoDB — обов'язкова залежність (Unhealthy), Redis — кеш (Degraded); обидві з тегом "ready".</summary>
    public static IServiceCollection AddFeedbackHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var checks = services.AddHealthChecks()
            .AddCheck<MongoDbHealthCheck>("feedback-db", HealthStatus.Unhealthy, ["database", "mongodb", "ready"],
                TimeSpan.FromSeconds(5));

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
