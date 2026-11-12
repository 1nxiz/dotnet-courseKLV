using System.Text.Json.Serialization;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Library.Books.Grpc;
using Library.Feedback.Grpc;
using Library.Loans.Grpc;
using Microsoft.AspNetCore.Diagnostics;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();          // service discovery і CorrelationIdHandler діють для всіх HttpClient — і для gRPC-каналів

// ---------- gRPC-клієнти (Лаб. 06): адреса http://_grpc.<ресурс> — іменований endpoint "grpc" сервісу (HTTP/2) ----------
// Стійкість gRPC — рідним механізмом (ServiceConfig: повтор лише UNAVAILABLE), а не HTTP-резільєнсом:
// HTTP-retry не бачить gRPC-статусів (вони в trailers) і не може безпечно повторити gRPC-запит.
var grpcRetry = new ServiceConfig
{
    MethodConfigs =
    {
        new MethodConfig
        {
            Names = { MethodName.Default },
            RetryPolicy = new RetryPolicy
            {
                MaxAttempts = 3,
                InitialBackoff = TimeSpan.FromMilliseconds(200),
                MaxBackoff = TimeSpan.FromSeconds(1),
                BackoffMultiplier = 2,
                RetryableStatusCodes = { StatusCode.Unavailable }
            }
        }
    }
};
builder.Services.AddSingleton<GrpcClientLoggingInterceptor>();
void RegisterGrpcClient<TClient>(string service) where TClient : ClientBase
{
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers — експериментальний API Microsoft.Extensions.Http.Resilience
    builder.Services.AddGrpcClient<TClient>(o => o.Address = new Uri($"http://_grpc.{service}"))
        .ConfigureChannel(c => c.ServiceConfig = grpcRetry)
        .AddInterceptor<GrpcClientLoggingInterceptor>()
        .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
}
RegisterGrpcClient<LoansService.LoansServiceClient>("loans-api");
RegisterGrpcClient<BooksCatalog.BooksCatalogClient>("books-api");
RegisterGrpcClient<FeedbackService.FeedbackServiceClient>("feedback-api");
builder.Services.AddScoped<GrpcUpstream>();

// ---------- HTTP REST-клієнти Лаб. 05 (transport=http — для порівняння затримок) ----------
builder.Services.AddHttpClient<LoansClient>(c => c.BaseAddress = new("https+http://loans-api"));
builder.Services.AddHttpClient<BooksClient>(c => c.BaseAddress = new("https+http://books-api"));
builder.Services.AddHttpClient<FeedbackClient>(c => c.BaseAddress = new("https+http://feedback-api"));
builder.Services.AddScoped<HttpUpstream>();

// ---------- Кеш складених відповідей: Redis через Aspire-інтеграцію (без Redis — у пам'яті) ----------
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("redis")))
    builder.AddRedisDistributedCache("redis");
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
    var unavailable = error is HttpRequestException or TaskCanceledException
        || error is RpcException { StatusCode: StatusCode.Unavailable or StatusCode.DeadlineExceeded };
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
