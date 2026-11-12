using System.Net;
using System.Net.Http.Json;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Clients;

/// <summary>Typed HttpClient сервісу видач (BaseAddress https+http://loans-api — service discovery).</summary>
public sealed class LoansClient(HttpClient http, ILogger<LoansClient> logger)
{
    public async Task<LoanView?> GetLoanAsync(long id, CancellationToken ct)
    {
        logger.LogInformation("Calling {Service} for loan {LoanId}", "loans-api", id);
        using var response = await http.GetAsync($"/api/loans/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoanView>(ct);
    }
}

/// <summary>Typed HttpClient каталогу (https+http://books-api).</summary>
public sealed class BooksClient(HttpClient http, ILogger<BooksClient> logger)
{
    public async Task<BookView?> GetBookAsync(long id, CancellationToken ct)
    {
        logger.LogInformation("Calling {Service} for book {BookId}", "books-api", id);
        using var response = await http.GetAsync($"/api/books/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BookView>(ct);
    }

    /// <summary>Книги видачі — паралельно, одна на запит.</summary>
    public async Task<IReadOnlyList<BookView>> GetBooksAsync(IEnumerable<long> ids, CancellationToken ct)
    {
        var books = await Task.WhenAll(ids.Select(id => GetBookAsync(id, ct)));
        return books.Where(b => b is not null).Select(b => b!).ToList();
    }
}

/// <summary>Typed HttpClient відгуків (https+http://feedback-api).</summary>
public sealed class FeedbackClient(HttpClient http, ILogger<FeedbackClient> logger)
{
    public async Task<IReadOnlyList<ReviewView>> GetReviewsForBooksAsync(IEnumerable<long> bookIds, CancellationToken ct)
    {
        var pages = await Task.WhenAll(bookIds.Select(async id =>
        {
            logger.LogInformation("Calling {Service} for reviews of book {BookId}", "feedback-api", id);
            var page = await http.GetFromJsonAsync<ReviewsPage>($"/api/reviews?bookId={id}&pageSize=5", ct);
            return page?.Items ?? [];
        }));
        return pages.SelectMany(p => p).ToList();
    }
}
