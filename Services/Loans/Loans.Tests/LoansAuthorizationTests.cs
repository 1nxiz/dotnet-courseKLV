using System.Net;
using System.Net.Http.Json;

namespace Loans.Tests;

/// <summary>
/// Лаб. 09: permission-based авторизація на рівні endpoint (Таблиця 3.3.1 для сервісу видач).
/// Немає токена -> 401; немає permission -> 403 (а не 500); одна роль — різні правила на різних endpoint-ах.
/// </summary>
public class LoansAuthorizationTests : IClassFixture<AuthenticatedApiFactory>
{
    private readonly AuthenticatedApiFactory _factory;

    public LoansAuthorizationTests(AuthenticatedApiFactory factory) => _factory = factory;

    [SkippableFact]
    public async Task Request_without_token_returns_401()
    {
        TestDatabase.SkipIfUnavailable();
        var response = await _factory.CreateClient().GetAsync("/api/loans");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableTheory]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData("manager", HttpStatusCode.OK)]
    [InlineData("user", HttpStatusCode.OK)]
    [InlineData("guest", HttpStatusCode.Forbidden)]                 // guest не має loans:read
    public async Task Get_loans_requires_loans_read(string role, HttpStatusCode expected)
    {
        TestDatabase.SkipIfUnavailable();
        var response = await _factory.CreateClient(role).GetAsync("/api/loans?page=1&pageSize=1");
        Assert.Equal(expected, response.StatusCode);
    }

    [SkippableTheory]
    [InlineData("manager")]                                        // manager: update/approve так, delete — ні
    [InlineData("user")]
    [InlineData("guest")]
    public async Task Delete_loan_requires_loans_delete(string role)
    {
        TestDatabase.SkipIfUnavailable();
        var response = await _factory.CreateClient(role).DeleteAsync("/api/loans/999999999");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task Admin_passes_authorization_for_delete()
    {
        TestDatabase.SkipIfUnavailable();
        var response = await _factory.CreateClient("admin").DeleteAsync("/api/loans/999999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);   // дозволено -> далі бізнес-логіка: 404
    }

    [SkippableFact]
    public async Task Manager_cannot_create_loan_but_user_can_reach_validation()
    {
        TestDatabase.SkipIfUnavailable();
        var body = new { readerId = 0, items = Array.Empty<object>() };
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.CreateClient("manager").PostAsJsonAsync("/api/loans", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _factory.CreateClient("user").PostAsJsonAsync("/api/loans", body)).StatusCode);
    }
}
