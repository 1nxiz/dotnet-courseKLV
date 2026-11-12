using System.Diagnostics;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Services;

/// <summary>
/// Aggregator Pattern: видача читається першою (без неї агрегувати нічого), потім каталог і відгуки
/// опитуються ПАРАЛЕЛЬНО (Task.WhenAll). Недоступність каталогу чи відгуків не «валить» запит —
/// відповідь містить доступні дані й перелік попереджень.
/// </summary>
public sealed class LoanCardAggregator
{
    public static readonly ActivitySource ActivitySource = new("Platform.Aggregator");

    private readonly LoansClient _loans;
    private readonly BooksClient _books;
    private readonly FeedbackClient _feedback;
    private readonly ILogger<LoanCardAggregator> _logger;

    public LoanCardAggregator(LoansClient loans, BooksClient books, FeedbackClient feedback, ILogger<LoanCardAggregator> logger)
    {
        _loans = loans;
        _books = books;
        _feedback = feedback;
        _logger = logger;
    }

    public async Task<LoanCardDto?> GetLoanCardAsync(long loanId, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("AggregateLoanCard");
        activity?.SetTag("loan.id", loanId);

        var loan = await TimedAsync(() => _loans.GetLoanAsync(loanId, ct), "loans-api");
        if (loan is null) return null;

        var warnings = new List<string>();
        var bookIds = loan.Items.Select(i => i.BookId).Distinct().ToList();

        var booksTask = SafeAsync(() => _books.GetBooksAsync(bookIds, ct), "books-api", warnings);
        var reviewsTask = SafeAsync(() => _feedback.GetReviewsForBooksAsync(bookIds, ct), "feedback-api", warnings);
        await Task.WhenAll(booksTask, reviewsTask);                     // паралельно

        var result = Compose(loan, await booksTask, await reviewsTask, warnings);
        activity?.SetTag("aggregation.warnings", warnings.Count);
        return result;
    }

    internal static LoanCardDto Compose(LoanView loan, IReadOnlyList<BookView>? books, IReadOnlyList<ReviewView>? reviews,
        IReadOnlyList<string> warnings)
    {
        var bookIds = loan.Items.Select(i => i.BookId).ToHashSet();
        // узгодженість: беремо лише книги й відгуки, що належать САМЕ цій видачі
        var booksById = (books ?? []).Where(b => bookIds.Contains(b.Id)).ToDictionary(b => (long)b.Id);
        var reviewsByBook = (reviews ?? []).Where(r => bookIds.Contains(r.BookId)).ToLookup(r => r.BookId);

        var items = loan.Items.Select(item =>
        {
            booksById.TryGetValue(item.BookId, out var book);
            var bookReviews = reviews is null ? null : reviewsByBook[item.BookId].OrderByDescending(r => r.CreatedAt).Take(3).ToList();
            return new LoanCardBookDto(
                item.BookId, item.BookTitle, item.UnitPrice, item.Quantity,
                book is null ? null : new CatalogInfoDto(book.Title, book.AuthorName, book.Price, book.TotalCopies, book.IsPublished),
                bookReviews is { Count: > 0 } ? Math.Round(bookReviews.Average(r => r.Rating), 2) : null,
                bookReviews);
        }).ToList();

        return new LoanCardDto(loan.Id, loan.Number, loan.Status, loan.ReaderId, loan.LoanDate, loan.DueDate, loan.Total,
            items, warnings);
    }

    private async Task<T> TimedAsync<T>(Func<Task<T>> call, string service)
    {
        var sw = Stopwatch.StartNew();
        var result = await call();
        _logger.LogInformation("{Service} responded in {ElapsedMs} ms", service, sw.ElapsedMilliseconds);
        return result;
    }

    /// <summary>Виклик із перехопленням часткової відмови: замість винятку — попередження.</summary>
    private async Task<T?> SafeAsync<T>(Func<Task<T>> call, string service, List<string> warnings) where T : class
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await call();
            _logger.LogInformation("{Service} responded in {ElapsedMs} ms", service, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException
                                     || ex.GetType().Name.Contains("BrokenCircuit", StringComparison.Ordinal))
        {
            _logger.LogWarning(ex, "{Service} is unavailable after {ElapsedMs} ms", service, sw.ElapsedMilliseconds);
            lock (warnings) warnings.Add($"{service} is unavailable");
            return null;
        }
    }
}
