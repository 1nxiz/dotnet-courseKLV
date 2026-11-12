using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Books.Tests;

public sealed class BooksApiFixture : WebApplicationFactory<Program> { }

[CollectionDefinition("books-api")]
public sealed class BooksApiCollection : ICollectionFixture<BooksApiFixture> { }

/// <summary>
/// API-тести проти реальної BooksDb (міграції + сідінг виконуються під час старту застосунку).
/// Запускаються, якщо задано змінну ConnectionStrings__BooksDb.
/// </summary>
[Collection("books-api")]
public class BooksApiTests
{
    private readonly BooksApiFixture _factory;

    public BooksApiTests(BooksApiFixture factory) => _factory = factory;

    private static void SkipIfNoDatabase() =>
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__BooksDb")),
            "Set ConnectionStrings__BooksDb to run API tests");

    [SkippableFact]
    public async Task Paging_returns_total_count_of_all_rows_not_page_size()
    {
        SkipIfNoDatabase();
        var client = _factory.CreateClient();

        var all = await client.GetFromJsonAsync<JsonElement>("/api/books?page=1&pageSize=100");
        var page2 = await client.GetAsync("/api/books?page=2&pageSize=5");

        Assert.Equal(HttpStatusCode.OK, page2.StatusCode);
        var body = await page2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(all.GetProperty("totalCount").GetInt32(), body.GetProperty("totalCount").GetInt32());
        Assert.True(body.GetProperty("items").GetArrayLength() <= 5);
        Assert.Equal(2, body.GetProperty("page").GetInt32());
        Assert.True(page2.Headers.Contains("X-Pagination-TotalCount"));
    }

    [SkippableFact]
    public async Task Empty_title_returns_400_with_errors_per_field()
    {
        SkipIfNoDatabase();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/books", new { title = "", isbn = "978-1-11-111111-1", price = 10, authorId = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("Title", out _));
    }

    [SkippableFact]
    public async Task Duplicate_isbn_returns_409_and_missing_id_returns_404()
    {
        SkipIfNoDatabase();
        var client = _factory.CreateClient();
        var isbn = $"978-0-{Random.Shared.Next(10, 99)}-{Random.Shared.Next(100000, 999999)}-1";
        var book = new { title = "Refactoring", isbn, price = 55, authorId = 4, initialCopies = 1, genreIds = new[] { 4 } };

        var first = await client.PostAsJsonAsync("/api/books", book);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.NotNull(first.Headers.Location);

        var second = await client.PostAsJsonAsync("/api/books", book);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);

        var missing = await client.GetAsync("/api/books/999999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
    }

    [SkippableFact]
    public async Task Seeded_reference_data_is_not_duplicated()
    {
        SkipIfNoDatabase();
        var client = _factory.CreateClient();
        var genres = await client.GetFromJsonAsync<JsonElement>("/api/books/genres");
        Assert.Equal(6, genres.GetArrayLength());
    }
}
