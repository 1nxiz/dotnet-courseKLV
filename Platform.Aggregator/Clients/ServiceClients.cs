using System.Net;
using System.Net.Http.Json;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Clients;

// HTTP REST-клієнти з Лаб. 05 — лишено як альтернативний транспорт (?transport=http), щоб виміряти різницю з gRPC.

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

/// <summary>Typed HttpClient каталогу (https+http://books-api): одна книга — один запит.</summary>
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
}

/// <summary>Typed HttpClient відгуків (https+http://feedback-api).</summary>
public sealed class FeedbackClient(HttpClient http, ILogger<FeedbackClient> logger)
{
    public async Task<BookReviewsView> GetBookReviewsAsync(long bookId, CancellationToken ct)
    {
        logger.LogInformation("Calling {Service} for reviews of book {BookId}", "feedback-api", bookId);
        var page = await http.GetFromJsonAsync<ReviewsPage>($"/api/reviews?bookId={bookId}&pageSize=50", ct);
        var items = page?.Items ?? [];
        return new BookReviewsView(bookId, page?.TotalCount ?? 0,
            items.Count == 0 ? 0 : Math.Round(items.Average(r => r.Rating), 2), items.Take(3).ToList());
    }
}

/// <summary>Транспорт HTTP REST: N книг = N паралельних запитів (кожен — окреме HTTP/1.1-з'єднання).</summary>
public sealed class HttpUpstream(LoansClient loans, BooksClient books, FeedbackClient feedback) : IUpstream
{
    public UpstreamTransport Transport => UpstreamTransport.Http;

    public Task<LoanView?> GetLoanAsync(long id, CancellationToken ct) => loans.GetLoanAsync(id, ct);

    public async Task<IReadOnlyList<BookView>> GetBooksAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
        => (await Task.WhenAll(ids.Select(id => books.GetBookAsync(id, ct)))).Where(b => b is not null).Select(b => b!).ToList();

    public async Task<IReadOnlyList<BookReviewsView>> GetReviewsAsync(IReadOnlyCollection<long> bookIds, CancellationToken ct)
        => await Task.WhenAll(bookIds.Select(id => feedback.GetBookReviewsAsync(id, ct)));
}
