using System.Reflection;
using Loans.Api.ErrorHandling;
using Loans.Api.Grpc;
using Loans.Api.HealthChecks;
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
    builder.AddRedisDistributedCache("redis",            // ConnectionStrings:redis передає AppHost
        settings => settings.DisableHealthChecks = true);  // власна перевірка зі статусом Degraded — нижче
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
