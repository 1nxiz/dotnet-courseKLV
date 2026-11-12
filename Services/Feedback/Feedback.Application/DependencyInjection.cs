using Feedback.Application.Behaviors;
using Feedback.Application.Caching;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Feedback.Application;

public static class DependencyInjection
{
    /// <summary>MediatR (сканування обробників + behaviors у заданому порядку), валідатори, AutoMapper.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));            // 1 — зовнішній
            cfg.AddOpenBehavior(typeof(UnhandledExceptionBehavior<,>)); // 2
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));         // 3
            cfg.AddOpenBehavior(typeof(CachingBehavior<,>));            // 4 — кеш для ICachedQuery (Лаб. 06)
            cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));        // 5 — біля обробника
        });

        services.AddValidatorsFromAssembly(assembly);

        // Кешування (Лаб. 06): L1 IMemoryCache і L2 IDistributedCache (Redis) реєструє WebApi-хост
        services.AddSingleton<CacheMetrics>();
        services.AddScoped<IFeedbackCache, TwoLevelCache>();

        services.AddAutoMapper(cfg =>
        {
            cfg.LicenseKey = configuration["LuckyPenny:LicenseKey"];   // один ключ Lucky Penny для AutoMapper 15+ / MediatR 13+
            cfg.AddMaps(assembly);
        });
        return services;
    }
}
