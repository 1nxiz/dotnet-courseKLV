using Duende.AccessTokenManagement;

namespace Platform.Aggregator.Security;

/// <summary>
/// Автентифікація вихідних викликів Aggregator -> мікросервіси (Лаб. 09).
/// <list type="bullet">
/// <item>Основний режим — OAuth 2.0 <b>Client Credentials</b> (Duende IdentityServer, client "aggregator_service"):
/// токен ідентифікує СЕРВІС (без sub користувача), містить scopes loans:read, books:read, feedback:read;
/// отримується й кешується (IDistributedCache / Redis) Duende.AccessTokenManagement і додається до HTTP- та gRPC-запитів.</item>
/// <item>Без IdentityServer (локальний запуск) — пересилання токена користувача, що викликав агрегатор.</item>
/// </list>
/// </summary>
public static class UpstreamAuthentication
{
    public const string ClientName = "identityserver";

    public static bool UseClientCredentials(IConfiguration configuration)
        => !string.IsNullOrEmpty(configuration["IdentityServer:Authority"]);

    public static IServiceCollection AddUpstreamAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddTransient<ForwardAuthorizationHandler>();
        if (UseClientCredentials(configuration))
        {
            services.AddClientCredentialsTokenManagement()
                .AddClient(ClientName, client =>
                {
                    client.TokenEndpoint = configuration["IdentityServer:Authority"]!.TrimEnd('/') + "/connect/token";
                    client.ClientId = configuration["IdentityServer:ClientId"] ?? "aggregator_service";
                    client.ClientSecret = configuration["IdentityServer:ClientSecret"] ?? "aggregator_secret";
                    client.Scope = configuration["IdentityServer:Scopes"] ?? "loans:read books:read feedback:read";
                });
        }
        return services;
    }

    /// <summary>Додає до клієнта токен: Client Credentials або пересланий токен користувача.</summary>
    public static IHttpClientBuilder AddUpstreamToken(this IHttpClientBuilder builder, IConfiguration configuration)
        => UseClientCredentials(configuration)
            ? builder.AddClientCredentialsTokenHandler(ClientName)
            : builder.AddHttpMessageHandler<ForwardAuthorizationHandler>();
}

/// <summary>Пересилає заголовок Authorization вхідного запиту у вихідний (режим без IdentityServer).</summary>
public sealed class ForwardAuthorizationHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var header = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(header) && request.Headers.Authorization is null)
            request.Headers.TryAddWithoutValidation("Authorization", header);
        return base.SendAsync(request, ct);
    }
}
