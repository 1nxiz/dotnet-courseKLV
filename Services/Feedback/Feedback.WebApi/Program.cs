using StackExchange.Redis;
using System.Reflection;
using Feedback.Application;
using Feedback.Infrastructure;
using Feedback.WebApi.ErrorHandling;
using Feedback.WebApi.Grpc;
using Feedback.WebApi.HealthChecks;
using Feedback.WebApi.Metrics;
using MediatR;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ServiceDefaults (Лаб. 05): Serilog, OpenTelemetry (+ MongoDB spans), health checks, service discovery, resilience
builder.AddServiceDefaults();

// кожен шар надає власний метод розширення
builder.Services
    .AddApplication(builder.Configuration)          // MediatR + behaviors, валідатори, AutoMapper
    .AddInfrastructure(builder.Configuration);      // MongoDB, BSON-мапінг, репозиторії, індекси, сідінг

// Кешування (Лаб. 06): L1 — пам'ять з обмеженням розміру, L2 — Redis через Aspire-інтеграцію (CachingBehavior)
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = builder.Configuration.GetValue("Caching:MemorySizeLimit", 5_000);
    options.CompactionPercentage = 0.25;
    options.TrackStatistics = true;
});
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("redis")))
    builder.AddRedisDistributedCache("redis",
        settings => settings.DisableHealthChecks = true,     // власна перевірка Redis зі статусом Degraded (Лаб. 07)
        options =>
        {
            // кеш — необов'язкова залежність: при недоступному Redis команда має падати за ~1 с, а не чекати 5 с у черзі,
            // щоб TwoLevelCache одразу перейшов на читання з БД (graceful degradation)
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 2000;
            options.AsyncTimeout = 1000;
            options.SyncTimeout = 1000;
            options.BacklogPolicy = BacklogPolicy.FailFast;
        });
else
    builder.Services.AddDistributedMemoryCache();

// gRPC (Лаб. 06): endpoint "grpc" лише HTTP/2 (Kestrel:Endpoints у appsettings.json)
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcLoggingInterceptor>();
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});
builder.Services.AddGrpcReflection();

// Лаб. 07: бізнес-метрики (Meter "Feedback.WebApi") через MediatR behavior і health checks MongoDB / Redis
builder.Services.AddSingleton<FeedbackMetrics>();
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MetricsBehavior<,>));
builder.Services.AddFeedbackHealthChecks(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Library — Feedback API", Version = "v1",
        Description = "Відгуки й обговорення: Clean Architecture, CQRS (MediatR), MongoDB" });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml);
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<FeedbackExceptionHandler>();

var app = builder.Build();

app.UseCorrelationId();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGrpcService<FeedbackGrpcService>();
app.MapGrpcHealthChecksService();
if (app.Environment.IsDevelopment()) app.MapGrpcReflectionService();
app.MapDefaultEndpoints();
app.Run();

/// <summary>Потрібен для WebApplicationFactory в API-тестах.</summary>
public partial class Program { }
