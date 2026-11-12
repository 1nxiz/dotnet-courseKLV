using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;

namespace Microsoft.Extensions.Hosting;

/// <summary>Вимога: у користувача (або сервісу) є permission.</summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Позначає endpoint потрібним permission: [RequirePermission(Permissions.Loans.Delete)].
/// Політика з тим самим ім'ям реєструється для кожного permission в AddPlatformAuthorization.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string permission) : base(policy: permission) => Permission = permission;

    public string Permission { get; }
}

/// <summary>
/// Перевірка permission ЛОКАЛЬНО, без звернення до Identity Provider. Джерела permissions у токені (за пріоритетом):
/// <list type="number">
/// <item>Keycloak RPT: claim <c>authorization.permissions[].scopes</c> (Authorization Services, Permission Policies);</item>
/// <item>Keycloak access token: client roles ресурс-серверів <c>resource_access.&lt;client&gt;.roles</c> (composite-ролі realm);</item>
/// <item>OAuth 2.0 scopes (<c>scope</c>) — Duende IdentityServer, зокрема Client Credentials для Aggregator;</item>
/// <item>ролі (<c>role</c>, <c>realm_access.roles</c>) — через матрицю <see cref="Permissions.RoleMatrix"/> (Auth Service, IdentityServer).</item>
/// </list>
/// Немає permission -> вимога не виконана -> 403 (а не 500); немає токена -> 401.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && GetPermissions(context.User).Contains(requirement.Permission))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }

    public static HashSet<string> GetPermissions(ClaimsPrincipal user)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        // 1) Keycloak RPT: {"permissions":[{"rsname":"loans-api","scopes":["loans:read",...]}]}
        foreach (var claim in user.FindAll("authorization"))
            foreach (var scope in ReadJson(claim.Value, root =>
                         root.TryGetProperty("permissions", out var perms) && perms.ValueKind == JsonValueKind.Array
                             ? perms.EnumerateArray().SelectMany(p => Strings(p, "scopes"))
                             : []))
                result.Add(scope);

        // 2) Keycloak client roles ресурс-серверів: {"loans-api":{"roles":["loans:read"]}, ...}
        foreach (var claim in user.FindAll("resource_access"))
            foreach (var role in ReadJson(claim.Value, root =>
                         root.ValueKind == JsonValueKind.Object
                             ? root.EnumerateObject().SelectMany(c => Strings(c.Value, "roles"))
                             : []))
                if (role.Contains(':')) result.Add(role);

        // 3) OAuth 2.0 scopes (IdentityServer): окремі claims або один рядок через пробіл
        foreach (var claim in user.FindAll("scope"))
            foreach (var scope in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (scope.Contains(':')) result.Add(scope);

        // 4) ролі -> матриця
        var roles = user.FindAll("role").Concat(user.FindAll(ClaimTypes.Role)).Select(c => c.Value)
            .Concat(user.FindAll("realm_access").SelectMany(c => ReadJson(c.Value, root => Strings(root, "roles"))));
        foreach (var role in roles)
            if (Permissions.RoleMatrix.TryGetValue(role, out var granted))
                result.UnionWith(granted);

        return result;
    }

    private static IEnumerable<string> ReadJson(string json, Func<JsonElement, IEnumerable<string>> select)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return select(doc.RootElement).ToList();               // матеріалізувати до Dispose
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IEnumerable<string> Strings(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)
            : [];
}
