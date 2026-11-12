var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// JWT від Auth Service / IdentityServer / Keycloak; audience — Resource Server "payments-api"
builder.AddPlatformAuthentication(apiAudience: "payments-api");
builder.Services.AddPlatformAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseCorrelationId();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapDefaultEndpoints();
app.Run();

public partial class Program;
