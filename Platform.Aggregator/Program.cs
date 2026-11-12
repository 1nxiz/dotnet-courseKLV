using StackExchange.Redis;
using System.Text.Json.Serialization;
using Grpc.Core;
using Library.Books.Grpc;
using Library.Feedback.Grpc;
using Library.Loans.Grpc;
using Microsoft.AspNetCore.Diagnostics;
using Platform.Aggregator.Clients;
using Platform.Aggregator.HealthChecks;
using Platform.Aggregator.Resilience;
using Platform.Aggregator.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();          // service discovery і CorrelationIdHandler діють для всіх HttpClient — і для gRPC-каналів

// ---------- Клієнти мікросервісів із політиками стійкості за критичністю (Лаб. 07, таблиця в README) ----------
// gRPC (типовий транспорт, Лаб. 06): адреса http://_grpc.<ресурс>; retry — gRPC ServiceConfig, breaker/таймаути — HTTP-рівень.
builder.Services.AddSingleton<GrpcClientLoggingInterceptor>();
builder.Services.AddResilientGrpcClient<LoansService.LoansServiceClient>(ServiceResilienceProfile.Loans);
builder.Services.AddResilientGrpcClient<BooksCatalog.BooksCatalogClient>(ServiceResilienceProfile.Books);
builder.Services.AddResilientGrpcClient<FeedbackService.FeedbackServiceClient>(ServiceResilienceProfile.Feedback);
builder.Services.AddScoped<GrpcUpstream>();

// HTTP REST (Лаб. 05, transport=http): повний Standard Resilience Handler з параметрами профілю
builder.Services.AddResilientHttpClient<LoansClient>(ServiceResilienceProfile.Loans);
builder.Services.AddResilientHttpClient<BooksClient>(ServiceResilienceProfile.Books);
builder.Services.AddResilientHttpClient<FeedbackClient>(ServiceResilienceProfile.Feedback);
builder.Services.AddScoped<HttpUpstream>();

// Health checks залежностей (gRPC Health Checking Protocol + Redis)
builder.Services.AddAggregatorHealthChecks(builder.Configuration);

// ---------- Кеш складених відповідей: Redis через Aspire-інтеграцію (без Redis — у пам'яті) ----------
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

builder.Services.AddSingleton<AggregatorMetrics>();
builder.Services.AddScoped<LoanCardAggregator>();

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);   // camelCase за замовчуванням
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseCorrelationId();

// Недоступний loans-api (без видачі агрегувати нічого) -> 503 ProblemDetails, а не 500
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    // Unavailable / DeadlineExceeded, а також Internal із транспортною причиною (розімкнений breaker, таймаут спроби)
    var unavailable = error is HttpRequestException or TaskCanceledException
        || error is RpcException { StatusCode: StatusCode.Unavailable or StatusCode.DeadlineExceeded }
        || error is RpcException { StatusCode: StatusCode.Internal, Status.DebugException: not null };
    context.Response.StatusCode = unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError;
    await Results.Problem(statusCode: context.Response.StatusCode,
        title: unavailable ? "Upstream service unavailable" : "Internal server error",
        detail: unavailable ? "loans-api is unavailable" : null).ExecuteAsync(context);
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapControllers();
app.MapDefaultEndpoints();
app.Run();
