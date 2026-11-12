using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace Platform.EventBus;

public static class EventBusExtensions
{
    /// <summary>
    /// Реєструє publisher, метрики й health check шини. Підключення (IConnection) має бути вже зареєстроване через
    /// builder.AddRabbitMQClient("messaging") (Aspire.RabbitMQ.Client: retry підключення, телеметрія).
    /// </summary>
    public static EventBusBuilder AddEventBus(this IServiceCollection services)
    {
        services.AddSingleton<EventBusMetrics>();
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        services.AddHealthChecks().AddCheck<RabbitMqHealthCheck>("messaging", HealthStatus.Degraded,
            ["messaging", "rabbitmq", "ready"], TimeSpan.FromSeconds(3));
        return new EventBusBuilder(services, enabled: true);
    }

    /// <summary>Без брокера: події лише логуються, споживачі не запускаються.</summary>
    public static EventBusBuilder AddLoggingOnlyEventBus(this IServiceCollection services)
    {
        services.AddSingleton<EventBusMetrics>();
        services.AddSingleton<IEventPublisher, LoggingOnlyEventPublisher>();
        return new EventBusBuilder(services, enabled: false);
    }
}

public sealed class EventBusBuilder(IServiceCollection services, bool enabled)
{
    public IServiceCollection Services { get; } = services;

    /// <summary>Споживач черги (окремий BackgroundService) з картою «подія → обробник».</summary>
    public EventBusBuilder AddConsumer(string queue, Action<ConsumerRegistration> configure)
    {
        var registration = new ConsumerRegistration(queue);
        configure(registration);
        if (!enabled) return this;
        Services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp =>
            ActivatorUtilities.CreateInstance<EventConsumer>(sp, registration));
        return this;
    }

    /// <summary>Обробник події (scoped — свій DI-scope на кожне повідомлення).</summary>
    public EventBusBuilder AddHandler<THandler>() where THandler : class
    {
        Services.AddScoped<THandler>();
        return this;
    }
}

/// <summary>Стан з'єднання з брокером. Degraded: без брокера сервіс обслуговує запити, але не обмінюється подіями.</summary>
public sealed class RabbitMqHealthCheck(IConnection connection) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        => Task.FromResult(connection.IsOpen
            ? HealthCheckResult.Healthy("RabbitMQ connection is open",
                new Dictionary<string, object> { ["endpoint"] = connection.Endpoint.ToString() })
            : new HealthCheckResult(context.Registration.FailureStatus, "RabbitMQ connection is closed"));
}
