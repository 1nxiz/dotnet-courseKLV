using Grpc.Core;
using Library.Books.Grpc;
using Library.Feedback.Grpc;
using Library.Loans.Grpc;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Clients;

/// <summary>
/// gRPC-клієнти агрегатора (Лаб. 06). Типізовані клієнти згенеровано з .proto сервісів і зареєстровано через
/// AddGrpcClient з адресою http://_grpc.&lt;сервіс&gt; — service discovery знаходить endpoint "grpc" (HTTP/2).
/// Кожен виклик має deadline; X-Correlation-Id потрапляє в метадані автоматично (CorrelationIdHandler у HttpClient).
/// Книги й відгуки читаються ОДНИМ пакетним викликом на сервіс замість N HTTP-запитів.
/// </summary>
public sealed class GrpcUpstream : IUpstream
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    private readonly LoansService.LoansServiceClient _loans;
    private readonly BooksCatalog.BooksCatalogClient _books;
    private readonly FeedbackService.FeedbackServiceClient _feedback;

    public GrpcUpstream(LoansService.LoansServiceClient loans, BooksCatalog.BooksCatalogClient books,
        FeedbackService.FeedbackServiceClient feedback)
    {
        _loans = loans;
        _books = books;
        _feedback = feedback;
    }

    public UpstreamTransport Transport => UpstreamTransport.Grpc;

    public async Task<LoanView?> GetLoanAsync(long id, CancellationToken ct)
    {
        try
        {
            var l = await _loans.GetLoanAsync(new GetLoanRequest { Id = id }, deadline: DateTime.UtcNow.Add(Deadline), cancellationToken: ct);
            return new LoanView(l.Id, l.Number, l.ReaderId, l.Status, l.LoanDate.ToDateTime(), l.DueDate?.ToDateTime(), l.Total,
                l.Items.Select(i => new LoanItemView(i.BookId, i.BookTitle, i.UnitPrice, i.Discount, i.Quantity)).ToList());
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<BookView>> GetBooksAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        var request = new GetBooksByIdsRequest();
        request.Ids.AddRange(ids.Select(id => (int)id));
        var reply = await _books.GetBooksByIdsAsync(request, deadline: DateTime.UtcNow.Add(Deadline), cancellationToken: ct);
        return reply.Books.Select(b => new BookView(b.Id, b.Title, b.Isbn, b.Price, b.TotalCopies, b.IsPublished, b.AuthorName)).ToList();
    }

    public async Task<IReadOnlyList<BookReviewsView>> GetReviewsAsync(IReadOnlyCollection<long> bookIds, CancellationToken ct)
    {
        var request = new GetBooksReviewsRequest { LatestPerBook = 3 };
        request.BookIds.AddRange(bookIds);
        var reply = await _feedback.GetBooksReviewsAsync(request, deadline: DateTime.UtcNow.Add(Deadline), cancellationToken: ct);
        return reply.Books.Select(b => new BookReviewsView(b.BookId, b.ReviewsCount, b.AverageRating,
            b.Latest.Select(r => new ReviewView(r.Id, r.BookId, r.AuthorName, r.Rating, r.Text, r.CreatedAt.ToDateTime())).ToList()))
            .ToList();
    }
}
