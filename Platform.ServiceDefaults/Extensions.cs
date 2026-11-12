using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// ServiceDefaults: однакові для всіх сервісів логування (Serilog), трасування й метрики (OpenTelemetry),
/// health checks, service discovery, resilience і наскрізний CorrelationId.
/// Підключення в кожному сервісі: builder.AddServiceDefaults(); ... app.MapDefaultEndpoints();
/// </summary>
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.AddSerilogDefaults();
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<CorrelationIdHandler>();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();                     // retry, circuit breaker, таймаути (Лаб. 07)
            http.AddServiceDiscovery();                               // http://loans-api -> реальна адреса
            http.AddHttpMessageHandler<CorrelationIdHandler>();       // X-Correlation-Id у вихідні запити
        });
        return builder;
    }

    /// <summary>
    /// Serilog: структуровані логи (CompactJson у консоль) зі збагаченням ServiceName, Environment, MachineName, ThreadId
    /// і LogContext (CorrelationId). writeToProviders: true передає записи провайдеру OpenTelemetry — так вони
    /// з'являються у вкладці Structured logs Aspire Dashboard.
    /// </summary>
    public static TBuilder AddSerilogDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var serviceName = builder.Environment.ApplicationName;
        var isDevelopment = builder.Environment.IsDevelopment();

        // прибрати стандартні Console/Debug-провайдери: інакше writeToProviders дублював би кожен запис у консолі.
        // Провайдер OpenTelemetry додається пізніше (ConfigureOpenTelemetry) і отримує записи Serilog.
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog((services, cfg) =>
        {
            // спершу типові рівні, потім секція "Serilog" appsettings (вона має пріоритет)
            cfg.MinimumLevel.Is(isDevelopment ? LogEventLevel.Debug : LogEventLevel.Information)
               .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
               .MinimumLevel.Override("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogEventLevel.Fatal)
               .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
               .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
               .MinimumLevel.Override("Polly", LogEventLevel.Information)
               .ReadFrom.Configuration(builder.Configuration)
               .Enrich.FromLogContext()
               .Enrich.WithProperty("ServiceName", serviceName)
               .Enrich.WithEnvironmentName()
               .Enrich.WithMachineName()
               .Enrich.WithThreadId()
               .WriteTo.Console(new CompactJsonFormatter());                     // структурований JSON у stdout
        },
            writeToProviders: true);
        return builder;
    }

    /// <summary>
    /// OpenTelemetry: трасування вхідних запитів (ASP.NET Core), вихідних HTTP/gRPC (HttpClient), SQL Server
    /// (SqlClient — покриває і Dapper, і EF Core), MongoDB (DiagnosticSources) і власних ActivitySource;
    /// метрики; експорт OTLP в Aspire Dashboard (адресу задає Aspire через OTEL_EXPORTER_OTLP_ENDPOINT).
    /// </summary>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var samplingRatio = builder.Configuration.GetValue("OpenTelemetry:SamplingRatio", builder.Environment.IsDevelopment() ? 1.0 : 0.2);

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(PlatformTelemetry.Meters))
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)))   // 100 % у Development
                .AddSource(builder.Environment.ApplicationName)                    // власні spans сервісу (3.3)
                .AddSource(PlatformTelemetry.ActivitySources)
                .AddSource("MongoDB.Driver.Core.Extensions.DiagnosticSources")     // MongoDB
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
                    !ctx.Request.Path.StartsWithSegments(HealthEndpointPath) && !ctx.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation());                                   // Dapper та EF Core

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();                 // батчевий експорт в Dashboard
        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);   // liveness без зовнішніх залежностей
        return builder;
    }

    /// <summary>CorrelationId-middleware + /health і /alive (у Development).</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthEndpointPath);
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        }
        return app;
    }

    /// <summary>Наскрізний CorrelationId: реєструється одразу після builder.Build() у кожному сервісі й у Gateway.</summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) => app.UseMiddleware<CorrelationIdMiddleware>();
}

/// <summary>Імена Meter-ів і ActivitySource-ів платформи, які збирає OpenTelemetry.</summary>
public static class PlatformTelemetry
{
    public static readonly string[] Meters = ["Loans.Api", "Books.Api", "Feedback.WebApi", "Platform.Aggregator", "Platform.ApiGateway"];
    public static readonly string[] ActivitySources = ["Loans.Api", "Books.Api", "Feedback.WebApi", "Platform.Aggregator"];
}
