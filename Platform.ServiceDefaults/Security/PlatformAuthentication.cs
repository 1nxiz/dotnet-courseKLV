using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Автентифікація мікросервісів (Лаб. 09): сервіс приймає JWT від трьох Identity Provider-ів і валідує їх ЛОКАЛЬНО
/// (підпис, issuer, audience, термін дії) — без звернення до провайдера на кожен запит:
/// <list type="bullet">
/// <item><b>auth-service</b> — Auth.Api (ASP.NET Identity), HS256, спільні Jwt:Issuer / Jwt:Audience / Jwt:SecretKey;</item>
/// <item><b>identityserver</b> — Duende IdentityServer: Authority + discovery document (ключі RS256 завантажуються автоматично),
/// audience = ApiResource сервісу;</item>
/// <item><b>keycloak</b> — Aspire-інтеграція AddKeycloakJwtBearer (service discovery ресурсу "keycloak", realm з Keycloak:Realm),
/// audience = Resource Server сервісу.</item>
/// </list>
/// Policy-схема "platform" обирає схему за issuer (iss) токена. Налаштовані лише ті провайдери, для яких є конфігурація.
/// </summary>
public static class PlatformAuthentication
{
    public const string Scheme = "platform";
    public const string AuthServiceScheme = "auth-service";
    public const string IdentityServerScheme = "identityserver";
    public const string KeycloakScheme = "keycloak";

    public static IHostApplicationBuilder AddPlatformAuthentication(this IHostApplicationBuilder builder, string apiAudience,
        bool validateAudience = true)
    {
        var config = builder.Configuration;
        var issuers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // iss -> схема
        // схема для запитів БЕЗ токена (health probes, swagger): має бути локальною — без мережевого запиту metadata/JWKS
        // до Identity Provider-а, інакше сервіс під час старту закешував би ще порожній набір ключів IdentityServer
        string? fallback = null;

        var auth = builder.Services.AddAuthentication(o =>
        {
            o.DefaultScheme = Scheme;
            o.DefaultChallengeScheme = Scheme;
            o.DefaultForbidScheme = Scheme;
        });

        // 1) Auth Service (ASP.NET Identity + JWT, HS256)
        var jwt = config.GetSection("Jwt");
        if (jwt["SecretKey"] is { Length: >= 32 } secret && jwt["Issuer"] is { } issuer)
        {
            auth.AddJwtBearer(AuthServiceScheme, o =>
            {
                o.Events = LoggingEvents(AuthServiceScheme);
                o.MapInboundClaims = false;                   // claims як у токені: sub, unique_name, role
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = issuer,
                    ValidAudience = jwt["Audience"],
                    ValidateAudience = validateAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "unique_name",
                    RoleClaimType = "role"
                };
            });
            issuers[issuer] = AuthServiceScheme;
            fallback = AuthServiceScheme;
        }

        // 2) Duende IdentityServer (OAuth 2.0 / OIDC)
        if (config["IdentityServer:Authority"] is { Length: > 0 } authority)
        {
            var issuerUri = config["IdentityServer:IssuerUri"];
            auth.AddJwtBearer(IdentityServerScheme, o =>
            {
                o.Events = LoggingEvents(IdentityServerScheme);
                o.Authority = authority;                      // /.well-known/openid-configuration -> ключі підпису
                o.Audience = apiAudience;                     // ApiResource сервісу
                o.RequireHttpsMetadata = false;               // навчальне середовище — HTTP
                o.MapInboundClaims = false;
                o.TokenValidationParameters.ValidateAudience = validateAudience;
                o.TokenValidationParameters.NameClaimType = "name";
                o.TokenValidationParameters.RoleClaimType = "role";
                if (!string.IsNullOrEmpty(issuerUri)) o.TokenValidationParameters.ValidIssuer = issuerUri;
                o.RefreshOnIssuerKeyNotFound = true;          // ротація ключів IdentityServer -> перечитати JWKS
                o.RefreshInterval = TimeSpan.FromSeconds(30);
            });
            issuers[issuerUri ?? authority.TrimEnd('/')] = IdentityServerScheme;
            fallback ??= IdentityServerScheme;
        }

        // 3) Keycloak (Aspire.Keycloak.Authentication): адреса — service discovery ресурсу "keycloak"
        var keycloak = config["Keycloak:Realm"] is { Length: > 0 } realm
                       && (config["services:keycloak:http:0"] is not null || config["services:keycloak:https:0"] is not null);
        if (keycloak)
        {
            auth.AddKeycloakJwtBearer("keycloak", config["Keycloak:Realm"]!, KeycloakScheme, o =>
            {
                o.Events = LoggingEvents(KeycloakScheme);
                o.Audience = apiAudience;                     // Resource Server сервісу (audience mapper у Pulumi)
                o.RequireHttpsMetadata = false;               // Keycloak у розробці — HTTP
                o.MapInboundClaims = false;
                o.TokenValidationParameters.ValidateAudience = validateAudience;
                o.TokenValidationParameters.NameClaimType = "preferred_username";
                o.RefreshOnIssuerKeyNotFound = true;
                o.RefreshInterval = TimeSpan.FromSeconds(30);
            });
        }

        auth.AddPolicyScheme(Scheme, "JWT: Auth Service / IdentityServer / Keycloak", o =>
            o.ForwardDefaultSelector = context => SelectScheme(context, issuers, keycloak, fallback ?? (keycloak ? KeycloakScheme : AuthServiceScheme)));
        return builder;
    }

    /// <summary>Політики для кожного permission (ім'я політики = permission) і обробник PermissionRequirement.</summary>
    public static IServiceCollection AddPlatformAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.All)
                options.AddPolicy(permission, p => p.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
            options.AddPolicy("authenticated", p => p.RequireAuthenticatedUser());
        });
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }

    /// <summary>Причина відхилення токена — у структурований лог (Warning), а не лише 401 у відповіді.</summary>
    private static JwtBearerEvents LoggingEvents(string scheme) => new()
    {
        OnAuthenticationFailed = context =>
        {
            context.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
                .CreateLogger("Platform.Authentication")
                .LogWarning("JWT rejected by {Scheme} for {Path}: {Reason}", scheme, context.Request.Path, context.Exception.Message);
            return Task.CompletedTask;
        }
    };

    private static string SelectScheme(HttpContext context, Dictionary<string, string> issuers, bool keycloak, string fallback)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return fallback;
        try
        {
            var iss = new JsonWebToken(header["Bearer ".Length..].Trim()).Issuer;   // лише читання, підпис перевірить обрана схема
            if (issuers.TryGetValue(iss.TrimEnd('/'), out var scheme)) return scheme;
            if (keycloak && iss.Contains("/realms/", StringComparison.Ordinal)) return KeycloakScheme;
        }
        catch (ArgumentException)
        {
            // не JWT — обрана схема поверне 401
        }
        return fallback;
    }
}
