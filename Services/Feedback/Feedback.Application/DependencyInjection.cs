using Feedback.Application.Behaviors;
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
            cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));        // 4 — біля обробника
        });

        services.AddValidatorsFromAssembly(assembly);

        services.AddAutoMapper(cfg =>
        {
            cfg.LicenseKey = configuration["LuckyPenny:LicenseKey"];   // один ключ Lucky Penny для AutoMapper 15+ / MediatR 13+
            cfg.AddMaps(assembly);
        });
        return services;
    }
}
