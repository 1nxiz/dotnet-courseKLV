using System.Reflection;
using Books.Api.ErrorHandling;
using Books.Bll;
using Books.Dal;
using Books.Dal.Seeding;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------- ServiceDefaults (Лаб. 05): Serilog (console JSON + file sink з appsettings), OpenTelemetry, health, discovery ----------
builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("BooksDb")
    ?? throw new InvalidOperationException("Connection string 'BooksDb' is missing");

builder.Services.AddBooksDal(connectionString);
builder.Services.AddBooksBll(builder.Configuration["AutoMapper:LicenseKey"]);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Library — Books API", Version = "v1",
        Description = "Каталог книг: EF Core Code First, Repository / Unit of Work, специфікації, пагінація" });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml);
});

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ExceptionToProblemDetailsHandler>();

var app = builder.Build();

app.UseCorrelationId();               // спільний CorrelationId-middleware замість власного LogContextMiddleware
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

// ---------- Міграції + сідінг (сідінг вимикається в Production) ----------
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbInitializer.MigrateAsync(db, app.Configuration.GetValue<bool>("Database:RecreateIfNotMigrated"), app.Logger);
    if (!app.Environment.IsProduction())
        await DbInitializer.SeedAsync(db, app.Logger);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapDefaultEndpoints();
app.Run();

/// <summary>Потрібен для WebApplicationFactory в API-тестах.</summary>
public partial class Program { }
