using LibraryKeycloak;
using Pulumi;
using Pulumi.Keycloak;
using Pulumi.Keycloak.Inputs;
using Pulumi.Keycloak.OpenId;
using Pulumi.Keycloak.OpenId.Inputs;

// Infrastructure as Code для Keycloak (Лаб. 09, ч. 3). Provider налаштовується з конфігурації стеку (keycloak:url,
// keycloak:username, keycloak:password — Pulumi.dev.yaml). pulumi preview показує realm, resource servers, scopes,
// ролі, політики, permissions, клієнтів і користувачів.
return await Deployment.RunAsync(() =>
{
    var config = new Config("library");
    var realmName = config.Get("realm") ?? "library";
    var userPassword = config.Get("userPassword") ?? "Pass_123456";
    var temporaryPasswords = config.GetBoolean("temporaryPasswords") ?? true;
    var keycloakUrl = new Config("keycloak").Get("url") ?? "http://localhost:8080";

    // ---------------- Realm ----------------
    var realm = new Realm("library-realm", new RealmArgs
    {
        RealmName = realmName,
        DisplayName = "Онлайн-бібліотека",
        Enabled = true,
        RegistrationAllowed = true,
        ResetPasswordAllowed = true,
        RememberMe = true,
        LoginWithEmailAllowed = true,
        VerifyEmail = false,
        LoginTheme = "keycloak",
        SslRequired = "none",                                  // навчальне середовище — HTTP
        AccessTokenLifespan = "15m",
        SsoSessionIdleTimeout = "30m"
    });
    var realmId = realm.Id;

    // ---------------- Resource Servers: клієнти з увімкненою Authorization ----------------
    var clientRoleIds = new Dictionary<string, Output<string>>();          // "loans:read" -> id client role
    var scopeIds = new Dictionary<(string Rs, string Scope), Output<string>>();   // id authorization scope
    var resourceServers = new Dictionary<string, Client>();
    foreach (var (rsName, scopes) in Matrix.ResourceServers)
    {
        var rs = new Client($"rs-{rsName}", new ClientArgs
        {
            RealmId = realmId,
            ClientId = rsName,
            Name = $"Resource Server {rsName}",
            AccessType = "CONFIDENTIAL",
            StandardFlowEnabled = false,
            ServiceAccountsEnabled = true,                     // потрібно для Authorization Services
            Authorization = new ClientAuthorizationArgs
            {
                PolicyEnforcementMode = "ENFORCING",
                DecisionStrategy = "AFFIRMATIVE",
                KeepDefaults = false
            }
        });
        resourceServers[rsName] = rs;

        // scopes (permissions) ресурсного сервера + client roles з тими самими іменами (для звичайного access token)
        var scopeResources = new List<ClientAuthorizationScope>();
        foreach (var scope in scopes)
        {
            var authScope = new ClientAuthorizationScope($"{rsName}-scope-{scope.Replace(':', '-')}", new ClientAuthorizationScopeArgs
            {
                RealmId = realmId,
                ResourceServerId = rs.ResourceServerId,
                Name = scope,
                DisplayName = scope
            });
            scopeResources.Add(authScope);
            scopeIds[(rsName, scope)] = authScope.Id;
            var role = new Role($"{rsName}-role-{scope.Replace(':', '-')}", new RoleArgs
            {
                RealmId = realmId,
                ClientId = rs.Id,
                Name = scope,
                Description = $"Permission {scope} on {rsName}"
            });
            clientRoleIds[scope] = role.Id;
        }

        _ = new ClientAuthorizationResource($"{rsName}-resource", new ClientAuthorizationResourceArgs
        {
            RealmId = realmId,
            ResourceServerId = rs.ResourceServerId,
            Name = $"{rsName}-resource",
            DisplayName = rsName,
            Scopes = scopes,
            Uris = new[] { $"/api/{rsName.Replace("-api", "")}/*" }
        }, new CustomResourceOptions { DependsOn = scopeResources.Cast<Pulumi.Resource>().ToList() });   // scopes створюються першими
    }

    // ---------------- Realm roles: composite з client roles за матрицею ----------------
    var realmRoles = new Dictionary<string, Role>();
    foreach (var roleName in Matrix.Roles)
    {
        realmRoles[roleName] = new Role($"realm-role-{roleName}", new RoleArgs
        {
            RealmId = realmId,
            Name = roleName,
            Description = $"Platform role {roleName}",
            CompositeRoles = ToInputList(Matrix.RolePermissions[roleName].Select(p => clientRoleIds[p]))
        });
    }

    // ---------------- Permission Policies: роль -> scopes на кожному Resource Server ----------------
    foreach (var (rsName, scopes) in Matrix.ResourceServers)
    {
        var rs = resourceServers[rsName];
        foreach (var roleName in Matrix.Roles)
        {
            var granted = Matrix.RolePermissions[roleName].Intersect(scopes).ToArray();
            if (granted.Length == 0) continue;                // напр. payments-api для manager/user/guest — нічого

            var policy = new ClientRolePolicy($"{rsName}-policy-{roleName}", new ClientRolePolicyArgs
            {
                RealmId = realmId,
                ResourceServerId = rs.ResourceServerId,
                Name = $"role-{roleName}",
                Type = "role",
                Logic = "POSITIVE",
                DecisionStrategy = "UNANIMOUS",
                Roles = new InputList<ClientRolePolicyRoleArgs> { new ClientRolePolicyRoleArgs { Id = realmRoles[roleName].Id, Required = true } }
            });

            _ = new ClientAuthorizationPermission($"{rsName}-permission-{roleName}", new ClientAuthorizationPermissionArgs
            {
                RealmId = realmId,
                ResourceServerId = rs.ResourceServerId,
                Name = $"{roleName}-permissions",
                Description = $"{roleName}: {string.Join(", ", granted)}",
                Type = "scope",
                DecisionStrategy = "AFFIRMATIVE",
                Scopes = ToInputList(granted.Select(g => scopeIds[(rsName, g)])),   // scope permission: id-шники scopes
                Policies = ToInputList([policy.Id])
            });
        }
    }

    static InputList<string> ToInputList(IEnumerable<Output<string>> items)
    {
        var list = new InputList<string>();
        foreach (var item in items) list.Add(item);
        return list;
    }

    // ---------------- Clients (factory) ----------------
    var audiences = Matrix.ResourceServers.Keys.ToArray();
    var postman = ClientFactory.AuthorizationCode("client-postman", realmId, "library-postman", "postman_secret",
            "https://oauth.pstmn.io/v1/callback", "http://localhost:5555/callback")
        .WithAudiences(realmId, audiences);
    var swagger = ClientFactory.PublicPkce("client-swagger", realmId, "library-swagger",
            ["http://localhost:5000/swagger/oauth2-redirect.html", "http://localhost:5101/swagger/oauth2-redirect.html",
             "http://localhost:5102/swagger/oauth2-redirect.html"],
            ["http://localhost:5000", "http://localhost:5101", "http://localhost:5102"])
        .WithAudiences(realmId, audiences);
    var aggregator = ClientFactory.ClientCredentials("client-aggregator", realmId, "library-aggregator", "aggregator_secret")
        .WithAudiences(realmId, ["loans-api", "books-api", "feedback-api"]);
    var cli = ClientFactory.DirectGrantForTests("client-cli", realmId, "library-cli")
        .WithAudiences(realmId, audiences);

    // client-level ролі для service account агрегатора
    foreach (var permission in Matrix.AggregatorPermissions)
    {
        var rsName = Matrix.ResourceServers.First(kv => kv.Value.Contains(permission)).Key;
        _ = new ClientServiceAccountRole($"aggregator-sa-{permission.Replace(':', '-')}", new ClientServiceAccountRoleArgs
        {
            RealmId = realmId,
            ServiceAccountUserId = aggregator.ServiceAccountUserId,
            ClientId = resourceServers[rsName].Id,
            Role = permission
        }, new CustomResourceOptions { DependsOn = { resourceServers[rsName] } });
    }

    // ---------------- Тестові користувачі ----------------
    foreach (var (userName, firstName, roleName) in Matrix.Users)
    {
        var user = new User($"user-{userName}", new UserArgs
        {
            RealmId = realmId,
            Username = userName,
            Email = $"{userName}@library.local",
            EmailVerified = true,
            FirstName = firstName,
            LastName = roleName,
            Enabled = true,
            InitialPassword = new UserInitialPasswordArgs
            {
                Value = userPassword,
                Temporary = temporaryPasswords                // true — користувач змінить пароль при першому вході
            }
        });
        _ = new UserRoles($"user-{userName}-roles", new UserRolesArgs
        {
            RealmId = realmId,
            UserId = user.Id,
            Exhaustive = false,                                // default-roles realm лишаються
            RoleIds = ToInputList([realmRoles[roleName].Id])
        });
    }

    return new Dictionary<string, object?>
    {
        ["RealmUrl"] = Output.Format($"{keycloakUrl}/realms/{realm.RealmName}"),
        ["AdminConsoleUrl"] = Output.Format($"{keycloakUrl}/admin/master/console/#/{realm.RealmName}"),
        ["ClientIds"] = new[] { "library-postman", "library-swagger", "library-aggregator", "library-cli" },
        ["ResourceServers"] = audiences,
        ["ClientSecrets"] = Output.CreateSecret(new Dictionary<string, string>
        {
            ["library-postman"] = "postman_secret",
            ["library-aggregator"] = "aggregator_secret"
        })
    };
});
