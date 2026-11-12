using Pulumi;
using Pulumi.Keycloak.OpenId;

namespace LibraryKeycloak;

/// <summary>
/// Factory pattern для клієнтів realm: однакові налаштування для кожного типу flow в одному місці.
/// Default scopes і audience mappers — extension methods (WithAudiences / WithDefaultScopes).
/// </summary>
public static class ClientFactory
{
    /// <summary>Authorization Code (confidential) + PKCE — Postman.</summary>
    public static Client AuthorizationCode(string name, Output<string> realmId, string clientId, string secret, params string[] redirectUris)
        => new(name, new ClientArgs
        {
            RealmId = realmId,
            ClientId = clientId,
            Name = clientId,
            AccessType = "CONFIDENTIAL",
            ClientSecret = secret,
            StandardFlowEnabled = true,
            DirectAccessGrantsEnabled = false,
            PkceCodeChallengeMethod = "S256",
            ValidRedirectUris = redirectUris,
            UseRefreshTokens = true
        });

    /// <summary>Public client з PKCE — Swagger UI (секрету немає, PKCE обов'язковий).</summary>
    public static Client PublicPkce(string name, Output<string> realmId, string clientId, string[] redirectUris, string[] webOrigins)
        => new(name, new ClientArgs
        {
            RealmId = realmId,
            ClientId = clientId,
            Name = clientId,
            AccessType = "PUBLIC",
            StandardFlowEnabled = true,
            PkceCodeChallengeMethod = "S256",
            ValidRedirectUris = redirectUris,
            WebOrigins = webOrigins
        });

    /// <summary>Client Credentials (service account) — Aggregator, без user context.</summary>
    public static Client ClientCredentials(string name, Output<string> realmId, string clientId, string secret)
        => new(name, new ClientArgs
        {
            RealmId = realmId,
            ClientId = clientId,
            Name = clientId,
            AccessType = "CONFIDENTIAL",
            ClientSecret = secret,
            StandardFlowEnabled = false,
            ServiceAccountsEnabled = true
        });

    /// <summary>
    /// Тестовий клієнт для автоматизованих перевірок (Direct Access Grants / password grant) — лише для розробки й CI;
    /// інтерактивні клієнти користуються Authorization Code.
    /// </summary>
    public static Client DirectGrantForTests(string name, Output<string> realmId, string clientId)
        => new(name, new ClientArgs
        {
            RealmId = realmId,
            ClientId = clientId,
            Name = clientId,
            AccessType = "PUBLIC",
            StandardFlowEnabled = false,
            DirectAccessGrantsEnabled = true
        });

    /// <summary>Audience mapper на кожен Resource Server: токен клієнта придатний для loans-api, books-api, ...</summary>
    public static Client WithAudiences(this Client client, Output<string> realmId, IEnumerable<string> audiences)
    {
        foreach (var audience in audiences)
            _ = new AudienceProtocolMapper($"{client.GetResourceName()}-aud-{audience}", new AudienceProtocolMapperArgs
            {
                RealmId = realmId,
                ClientId = client.Id,
                Name = $"audience-{audience}",
                IncludedClientAudience = audience,
                AddToAccessToken = true,
                AddToIdToken = false
            });
        return client;
    }

    /// <summary>Default client scopes (profile, email, roles, web-origins) — однакові для інтерактивних клієнтів.</summary>
    public static Client WithDefaultScopes(this Client client, Output<string> realmId, params string[] scopes)
    {
        _ = new ClientDefaultScopes($"{client.GetResourceName()}-default-scopes", new ClientDefaultScopesArgs
        {
            RealmId = realmId,
            ClientId = client.Id,
            DefaultScopes = scopes.Length > 0 ? scopes : ["profile", "email", "roles", "web-origins", "basic", "acr"]
        });
        return client;
    }
}
