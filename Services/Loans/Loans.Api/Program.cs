using System.Reflection;
using Loans.Api.ErrorHandling;
using Loans.Bll;
using Loans.Dal;
using Loans.Dal.Initialization;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------- Логування: Serilog (структуровані логи, налаштування в appsettings.json -> "Serilog") ----------
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ServiceName", "loans-api")
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}"));

// ---------- Рядок підключення: appsettings.json, перевизначається змінною ConnectionStrings__LoansDb ----------
var connectionString = builder.Configuration.GetConnectionString("LoansDb")
    ?? throw new InvalidOperationException("Connection string 'LoansDb' is missing");

// ---------- DI: DAL (Unit of Work — Scoped), BLL (сервіси, AutoMapper) ----------
builder.Services.AddLoansDal(connectionString);
builder.Services.AddLoansBll(builder.Configuration["AutoMapper:LicenseKey"]);

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
app.Run();

/// <summary>Потрібен для WebApplicationFactory в API-тестах.</summary>
public partial class Program { }
