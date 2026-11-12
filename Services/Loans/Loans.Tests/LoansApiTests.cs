using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Loans.Bll.Dtos;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Loans.Tests;

/// <summary>API-тести через WebApplicationFactory: статуси та формат помилок (ProblemDetails).</summary>
public class LoansApiTests : IClassFixture<AuthenticatedApiFactory>
{
    private readonly AuthenticatedApiFactory _factory;

    public LoansApiTests(AuthenticatedApiFactory factory) => _factory = factory;

    [SkippableFact]
    public async Task Missing_loan_returns_404_problem_details()
    {
        TestDatabase.SkipIfUnavailable();
        var client = _factory.CreateClient("admin");

        var response = await client.GetAsync("/api/loans/999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [SkippableFact]
    public async Task Confirming_already_confirmed_loan_returns_409_and_issuing_unpaid_returns_409()
    {
        TestDatabase.SkipIfUnavailable();
        var client = _factory.CreateClient("admin");

        var create = await client.PostAsJsonAsync("/api/loans", new
        {
            readerId = 1,
            items = new[] { new { bookId = 7, quantity = 1 } },
            deliveryType = "Pickup"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(create.Headers.Location);
        var loan = await create.Content.ReadFromJsonAsync<LoanDto>();

        var first = await client.PostAsync($"/api/loans/{loan!.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await client.PostAsync($"/api/loans/{loan.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);

        var issueUnpaid = await client.PostAsync($"/api/loans/{loan.Id}/issue", null);
        Assert.Equal(HttpStatusCode.Conflict, issueUnpaid.StatusCode);

        // оплата ідемпотентна: той самий ключ — той самий платіж
        var pay1 = await client.SendAsync(PayRequest(loan.Id, loan.Total, "it-" + loan.Id));
        var pay2 = await client.SendAsync(PayRequest(loan.Id, loan.Total, "it-" + loan.Id));
        Assert.Equal(HttpStatusCode.OK, pay1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pay2.StatusCode);
        var p1 = await pay1.Content.ReadFromJsonAsync<PaymentDto>();
        var p2 = await pay2.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.Equal(p1!.Id, p2!.Id);

        var issue = await client.PostAsync($"/api/loans/{loan.Id}/issue", null);
        Assert.Equal(HttpStatusCode.NoContent, issue.StatusCode);
    }

    [SkippableFact]
    public async Task Invalid_input_returns_400_validation_problem()
    {
        TestDatabase.SkipIfUnavailable();
        var client = _factory.CreateClient("admin");

        var response = await client.PostAsJsonAsync("/api/loans", new { readerId = 0, items = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.TryGetProperty("errors", out _));
    }

    private static HttpRequestMessage PayRequest(long loanId, decimal amount, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/loans/{loanId}/pay")
        {
            Content = JsonContent.Create(new { amount, method = "Card" })
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }
}
