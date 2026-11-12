using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Feedback.Tests;

/// <summary>
/// Тестова схема автентифікації (Лаб. 09): роль береться із заголовка X-Test-Role; без заголовка — анонімний запит (401).
/// Авторизація при цьому справжня — ті самі [RequirePermission] і матриця ролей, що й у production.
/// </summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Test";
    public const string RoleHeader = "X-Test-Role";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RoleHeader, out var role) || string.IsNullOrEmpty(role))
            return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(
            [new Claim("sub", "test-" + role), new Claim("unique_name", "test-" + role), new Claim("role", role.ToString())],
            Scheme, "unique_name", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}

/// <summary>WebApplicationFactory з тестовою схемою за замовчуванням; CreateClient(role) — клієнт із роллю.</summary>
public class AuthenticatedApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestAuthHandler.Scheme;
                o.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                o.DefaultChallengeScheme = TestAuthHandler.Scheme;
                o.DefaultForbidScheme = TestAuthHandler.Scheme;
            });
        });

    public HttpClient CreateClient(string role)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        return client;
    }
}
