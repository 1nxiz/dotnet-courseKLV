using StackExchange.Redis;
using System.Reflection;
using Loans.Api.ErrorHandling;
using Loans.Api.Grpc;
using Loans.Api.HealthChecks;
using Loans.Api.IntegrationEvents;
using Loans.Api.IntegrationEvents.Events;
using Loans.Api.IntegrationEvents.Handlers;
using Loans.Bll.IntegrationEvents;
using Platform.EventBus;
using Loans.Api.Metrics;
using Loans.Bll;
using Loans.Dal;
using Loans.Dal.Initialization;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------- ServiceDefaults (Лаб. 05): Serilog, OpenTelemetry, health checks, service discovery, resilience ----------
builder.AddServiceDefaults();

// ---------- Рядок підключення: appsettings.json, перевизначається змінною ConnectionStrings__LoansDb ----------
var connectionString = builder.Configuration.GetConnectionString("LoansDb")
    ?? throw new InvalidOperationException("Connection string 'LoansDb' is missing");

// ---------- DI: DAL (Unit of Work — Scoped), BLL (сервіси, AutoMapper) ----------
builder.Services.AddLoansDal(connectionString);
builder.Services.AddLoansBll(builder.Configuration["AutoMapper:LicenseKey"]);

// ---------- Кешування (Лаб. 06): L1 — пам'ять з обмеженням розміру, L2 — Redis через Aspire-інтеграцію ----------
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
    builder.Services.AddDistributedMemoryCache();        // локальний запуск без Redis

// ---------- gRPC (Лаб. 06): endpoint "grpc" лише HTTP/2 (Kestrel:Endpoints), REST — на "http" ----------
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcLoggingInterceptor>();
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});
builder.Services.AddGrpcReflection();

// ---------- Лаб. 07: бізнес-метрики (Meter "Loans.Api") і health checks залежностей ----------
builder.Services.AddSingleton<LoansStatsSnapshot>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LoansStatsSnapshot>());
builder.Services.AddSingleton<LoansMetrics>();
builder.Services.AddLoansHealthChecks(builder.Configuration);

// ---------- Лаб. 08: подієва взаємодія через RabbitMQ (IConnection — Aspire-інтеграція, RabbitMQ.Client 7.x) ----------
EventBusBuilder eventBus;
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("messaging")))
{
    builder.AddRabbitMQClient("messaging", settings => settings.DisableHealthChecks = true);   // свій check — Degraded
    eventBus = builder.Services.AddEventBus();
}
else
    eventBus = builder.Services.AddLoggingOnlyEventBus();       // локальний запуск без брокера
builder.Services.AddScoped<ILoanEventPublisher, LoanEventPublisher>();   // loan.status_changed після Commit (LoanService)
eventBus.AddHandler<BookCatalogEventsHandler>()
        .AddConsumer(Topology.Queues.LoansBooksSync, c => c
            .On<BookCreatedEvent, BookCatalogEventsHandler>()
            .On<BookPriceChangedEvent, BookCatalogEventsHandler>()
            .On<BookRenamedEvent, BookCatalogEventsHandler>()
            .On<BookDeletedEvent, BookCatalogEventsHandler>());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Library — Loans API", Version = "v1",
        Description = "Видачі книг: ADO.NET + Dapper, Unit of Work, збережувані процедури LoansDb" });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml);
});

// ---------- Помилки: ProblemDetails (RFC 9457) + централізований обробник доменних винятків ----------
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

var app = builder.Build();

app.UseCorrelationId();               // X-Correlation-Id -> LogContext, заголовок відповіді
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

// Development / Aspire: розгорнути LoansDb скриптами Лаб. 01, якщо бази ще немає (скрипти ідемпотентні)
if (app.Configuration.GetValue<bool>("Database:InitializeOnStartup"))
{
    await DatabaseInitializer.InitializeAsync(connectionString,
        Path.Combine(AppContext.BaseDirectory, "Database"), app.Logger);
}

// Міграції Loans.Dal/Migrations (Лаб. 08: dbo.ProcessedEvents) — у всіх середовищах, до старту споживачів
await SqlMigrator.MigrateAsync(connectionString, Path.Combine(AppContext.BaseDirectory, "Migrations"), app.Logger);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGrpcService<LoansGrpcService>();
app.MapGrpcHealthChecksService();                        // grpc.health.v1 — для Aggregator
if (app.Environment.IsDevelopment()) app.MapGrpcReflectionService();
app.MapDefaultEndpoints();
app.Run();

/// <summary>Потрібен для WebApplicationFactory в API-тестах.</summary>
public partial class Program { }
