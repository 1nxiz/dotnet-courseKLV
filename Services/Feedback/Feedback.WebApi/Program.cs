using System.Reflection;
using Feedback.Application;
using Feedback.Infrastructure;
using Feedback.WebApi.ErrorHandling;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ServiceName", "feedback-api")
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}"));

// кожен шар надає власний метод розширення
builder.Services
    .AddApplication(builder.Configuration)          // MediatR + behaviors, валідатори, AutoMapper
    .AddInfrastructure(builder.Configuration);      // MongoDB, BSON-мапінг, репозиторії, індекси, сідінг

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

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.Run();

/// <summary>Потрібен для WebApplicationFactory в API-тестах.</summary>
public partial class Program { }
