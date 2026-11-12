using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Feedback.Tests;

public sealed class FeedbackApiFixture : AuthenticatedApiFactory { }

[CollectionDefinition("feedback-api")]
public sealed class FeedbackApiCollection : ICollectionFixture<FeedbackApiFixture> { }

[Collection("feedback-api")]
public class FeedbackApiTests
{
    private readonly FeedbackApiFixture _factory;

    public FeedbackApiTests(FeedbackApiFixture factory) => _factory = factory;

    private static void SkipIfNoDatabase() =>
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__FeedbackDb")),
            "Set ConnectionStrings__FeedbackDb to run API tests against MongoDB");

    [SkippableFact]
    public async Task Create_get_update_with_etag_and_duplicate_is_rejected()
    {
        SkipIfNoDatabase();
        var client = _factory.CreateClient("admin");
        var author = "u-test-" + Guid.NewGuid().ToString("N")[..8];
        var body = new { bookId = 3, bookTitle = "Місто", authorId = author, authorName = "Тест", rating = 4, text = "Атмосферно" };

        var created = await client.PostAsJsonAsync("/api/reviews", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        Assert.Equal(24, id.Length);                                       // рядковий ObjectId, а не сирий BSON
        Assert.EndsWith($"/api/reviews/{id}", created.Headers.Location!.ToString());

        var get = await client.GetAsync($"/api/reviews/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("\"0\"", get.Headers.ETag?.Tag);

        var stale = new HttpRequestMessage(HttpMethod.Put, $"/api/reviews/{id}") { Content = JsonContent.Create(new { rating = 5, text = "x" }) };
        stale.Headers.TryAddWithoutValidation("If-Match", "\"7\"");
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(stale)).StatusCode);

        var duplicate = await client.PostAsJsonAsync("/api/reviews", body);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/reviews/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/reviews/{id}")).StatusCode);
    }

    [SkippableFact]
    public async Task Invalid_object_id_and_rating_return_400()
    {
        SkipIfNoDatabase();
        var client = _factory.CreateClient("admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reviews/not-an-id")).StatusCode);
        var response = await client.PostAsJsonAsync("/api/reviews",
            new { bookId = 2, bookTitle = "Тигролови", authorId = "u-x", authorName = "X", rating = 10, text = "?" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("Rating", out _));
    }
}
