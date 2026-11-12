using Auth.Api.Data;
using Auth.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// ---------- ASP.NET Identity на окремій базі IdentityDb (рядок ConnectionStrings:IdentityDb передає AppHost) ----------
builder.Services.AddDbContext<AuthDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("IdentityDb")));
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;                        // 5 невдалих спроб -> блокування
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders();                                            // токени підтвердження email / скидання пароля
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));

// ---------- JWT: видача (JwtTokenService) і валідація (спільна схема платформи з ServiceDefaults) ----------
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddSingleton<IEmailSender, DevEmailSender>();
builder.AddPlatformAuthentication(apiAudience: "auth-api");
builder.Services.AddPlatformAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

var app = builder.Build();

// міграції IdentityDb (AspNetUsers, AspNetRoles, AspNetUserRoles, ..., RefreshTokens) + ролі й адміністратор
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            break;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (attempt < 10)
        {
            app.Logger.LogWarning(ex, "IdentityDb is not ready (attempt {Attempt})", attempt);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
    await IdentitySeeder.SeedAsync(scope.ServiceProvider, app.Configuration, app.Logger);
}

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
