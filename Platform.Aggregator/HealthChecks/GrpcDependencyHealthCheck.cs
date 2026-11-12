using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Platform.Aggregator.HealthChecks;

/// <summary>
/// Перевірка gRPC-залежності агрегатора стандартним gRPC Health Checking Protocol (grpc.health.v1.Health/Check):
/// сервіс відповідає за власними readiness-перевірками. Deadline 2 с; RpcException -> статус збою реєстрації + лог.
/// Ідемпотентна, без побічних ефектів у сервісі.
/// </summary>
public sealed class GrpcDependencyHealthCheck : IHealthCheck
{
    private readonly GrpcClientFactory _factory;
    private readonly string _clientName;
    private readonly ILogger<GrpcDependencyHealthCheck> _logger;

    public GrpcDependencyHealthCheck(GrpcClientFactory factory, string clientName, ILogger<GrpcDependencyHealthCheck> logger)
    {
        _factory = factory;
        _clientName = clientName;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var service = context.Registration.Name;
        try
        {
            var client = _factory.CreateClient<Health.HealthClient>(_clientName);
            var reply = await client.CheckAsync(new HealthCheckRequest(), deadline: DateTime.UtcNow.AddSeconds(2), cancellationToken: ct);
            return reply.Status == HealthCheckResponse.Types.ServingStatus.Serving
                ? HealthCheckResult.Healthy($"{service} is serving")
                : new HealthCheckResult(context.Registration.FailureStatus, $"{service} status: {reply.Status}");
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "{Service} gRPC health check failed: {GrpcStatus}", service, ex.StatusCode);
            return new HealthCheckResult(context.Registration.FailureStatus, $"{service} is unavailable ({ex.StatusCode})", ex);
        }
    }
}

public static class AggregatorHealthChecks
{
#pragma warning disable EXTEXP0001
    /// <summary>
    /// loans-api і books-api — критичні (Unhealthy: без них картку не зібрати), feedback-api — некритичний (Degraded:
    /// агрегатор поверне часткову відповідь). Redis — кеш складених відповідей (Degraded).
    /// </summary>
    public static IServiceCollection AddAggregatorHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var checks = services.AddHealthChecks();
        foreach (var (service, failure) in new[] { ("loans-api", HealthStatus.Unhealthy), ("books-api", HealthStatus.Unhealthy),
                                                   ("feedback-api", HealthStatus.Degraded) })
        {
            var clientName = $"{service}-health";
            services.AddGrpcClient<Health.HealthClient>(clientName, o => o.Address = new Uri($"http://_grpc.{service}"))
                    .RemoveAllResilienceHandlers();                       // перевірка має бути швидкою, без повторів
            checks.Add(new HealthCheckRegistration(service,
                sp => new GrpcDependencyHealthCheck(sp.GetRequiredService<GrpcClientFactory>(), clientName,
                    sp.GetRequiredService<ILogger<GrpcDependencyHealthCheck>>()),
                failure, ["grpc", "downstream", "ready"], TimeSpan.FromSeconds(3)));
        }

        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("redis")))
            checks.AddRedis(sp => sp.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>(),
                name: "redis-cache", failureStatus: HealthStatus.Degraded, tags: ["cache", "redis", "ready"],
                timeout: TimeSpan.FromSeconds(3));
        return services;
    }
#pragma warning restore EXTEXP0001
}
