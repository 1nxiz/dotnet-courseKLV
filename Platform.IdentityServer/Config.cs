using Duende.IdentityServer;
using Duende.IdentityServer.Models;

namespace Platform.IdentityServer;

/// <summary>
/// Конфігурація IdentityServer (засівається в ConfigurationDbContext): identity resources, API scopes (гранульовані
/// permissions — окремий дозвіл на кожну дію), API resources (по одному на мікросервіс; audience токена) і три клієнти:
/// Postman (Authorization Code), Aggregator (Client Credentials), Swagger UI (public client з PKCE).
/// </summary>
public static class Config
{
    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
        new IdentityResources.Email(),
        new IdentityResource("roles", "User roles", ["role"])
    ];

    /// <summary>Scope = permission платформи (loans:read, books:publish, ...).</summary>
    public static IEnumerable<ApiScope> ApiScopes =>
        Permissions.All.Select(p => new ApiScope(p, p) { UserClaims = { "role" } });

    /// <summary>ApiResource = мікросервіс; ім'я ресурсу — audience, який сервіс перевіряє (AddJwtBearer Audience).</summary>
    public static IEnumerable<ApiResource> ApiResources =>
    [
        Resource("loans-api", "Loans (видачі)", Permissions.Loans.All),
        Resource("books-api", "Books (каталог)", Permissions.Books.All),
        Resource("feedback-api", "Feedback (відгуки)", Permissions.Feedback.All),
        Resource("payments-api", "Payments (платежі)", Permissions.Payments.All)
    ];

    private static ApiResource Resource(string name, string display, string[] scopes) => new(name, display)
    {
        Scopes = scopes.ToList(),
        UserClaims = { "role", "name", "email" }                      // ролі користувача потрапляють у access token
    };

    public static IEnumerable<Client> Clients(IConfiguration configuration) =>
    [
        // Postman — Authorization Code (confidential client), refresh tokens
        new Client
        {
            ClientId = "postman",
            ClientName = "Postman",
            AllowedGrantTypes = GrantTypes.Code,
            ClientSecrets = { new Secret("postman_secret".Sha256()) },
            RequirePkce = true,                                        // Postman підтримує PKCE — вмикаємо і тут
            RedirectUris = { "https://oauth.pstmn.io/v1/callback", "http://localhost:5555/callback" },
            AllowedScopes = [IdentityServerConstants.StandardScopes.OpenId, IdentityServerConstants.StandardScopes.Profile, "roles",
                             .. Permissions.All],
            AllowOfflineAccess = true,
            AccessTokenLifetime = 900                                  // 15 хв
        },
        // Aggregator — Client Credentials: токен ідентифікує сервіс, user context (sub) відсутній
        new Client
        {
            ClientId = "aggregator_service",
            ClientName = "Platform Aggregator",
            AllowedGrantTypes = GrantTypes.ClientCredentials,
            ClientSecrets = { new Secret((configuration["Clients:AggregatorSecret"] ?? "aggregator_secret").Sha256()) },
            AllowedScopes = [Permissions.Loans.Read, Permissions.Books.Read, Permissions.Feedback.Read],
            AccessTokenLifetime = 900
        },
        // Swagger UI — public client: без секрету, PKCE обов'язковий
        new Client
        {
            ClientId = "swagger",
            ClientName = "Swagger UI",
            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,
            RequireClientSecret = false,
            RedirectUris = (configuration.GetSection("Clients:SwaggerRedirectUris").Get<string[]>()
                            ?? ["http://localhost:5000/swagger/oauth2-redirect.html", "http://localhost:5101/swagger/oauth2-redirect.html",
                                "http://localhost:5102/swagger/oauth2-redirect.html"]).ToList(),
            AllowedCorsOrigins = { "http://localhost:5000", "http://localhost:5101", "http://localhost:5102" },
            AllowedScopes = [IdentityServerConstants.StandardScopes.OpenId, IdentityServerConstants.StandardScopes.Profile, "roles",
                             .. Permissions.Loans.All, .. Permissions.Books.All]
        }
    ];
}
