using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Platform.IdentityServer;
using Platform.IdentityServer.Data;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// окрема база IdentityServerDb (окремий SQL Server у AppHost) — ізоляція безпеки від бізнес-баз
var connectionString = builder.Configuration.GetConnectionString("IdentityServerDb");
var migrationsAssembly = typeof(Program).Assembly.GetName().Name;

builder.Services.AddRazorPages();
builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connectionString));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.ClaimsIdentity.RoleClaimType = "role";                        // claim "role" у токенах
        o.ClaimsIdentity.UserNameClaimType = "name";
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddIdentityServer(options =>
    {
        // фіксований issuer: однаковий незалежно від адреси, через яку прийшов клієнт (Aspire-проксі, Gateway, localhost)
        options.IssuerUri = builder.Configuration["IdentityServer:IssuerUri"];
        options.Events.RaiseErrorEvents = true;
        options.Events.RaiseFailureEvents = true;
        options.Events.RaiseSuccessEvents = true;
        options.EmitStaticAudienceClaim = false;
        options.UserInteraction.LoginUrl = "/Account/Login";
        options.UserInteraction.LogoutUrl = "/Account/Logout";
    })
    .AddAspNetIdentity<ApplicationUser>()                               // користувачі й ролі — ASP.NET Identity
    .AddConfigurationStore(options =>                                   // Clients, ApiResources, ApiScopes, IdentityResources
        options.ConfigureDbContext = b => b.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly)))
    .AddOperationalStore(options =>                                     // PersistedGrants, DeviceCodes, Keys
    {
        options.ConfigureDbContext = b => b.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly));
        options.EnableTokenCleanup = true;
    });

var app = builder.Build();

if (!EF.IsDesignTime)
    await SeedData.InitializeAsync(app.Services, app.Configuration, app.Logger);

app.UseCorrelationId();
app.UseStaticFiles();
app.UseRouting();
app.UseIdentityServer();                                                // /connect/authorize, /connect/token, discovery
app.UseAuthorization();
app.MapRazorPages();
app.MapDefaultEndpoints();
app.Run();
