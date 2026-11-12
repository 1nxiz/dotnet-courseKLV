using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();          // resilience, service discovery і CorrelationIdHandler діють для всіх HttpClient

// Typed HttpClients: адреса за логічним іменем ресурсу ("https+http" — HTTPS, якщо доступний, інакше HTTP)
builder.Services.AddHttpClient<LoansClient>(c => c.BaseAddress = new("https+http://loans-api"));
builder.Services.AddHttpClient<BooksClient>(c => c.BaseAddress = new("https+http://books-api"));
builder.Services.AddHttpClient<FeedbackClient>(c => c.BaseAddress = new("https+http://feedback-api"));
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
    var unavailable = error is HttpRequestException or TaskCanceledException;
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
