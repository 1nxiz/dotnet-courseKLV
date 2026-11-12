using Platform.Aggregator.Models;

namespace Platform.Aggregator.Clients;

/// <summary>Три мікросервіси з погляду агрегатора; дві реалізації — gRPC і HTTP REST.</summary>
public interface IUpstream
{
    UpstreamTransport Transport { get; }

    /// <summary>Видача або null, якщо її немає (gRPC NOT_FOUND / HTTP 404).</summary>
    Task<LoanView?> GetLoanAsync(long id, CancellationToken ct);

    Task<IReadOnlyList<BookView>> GetBooksAsync(IReadOnlyCollection<long> ids, CancellationToken ct);

    Task<IReadOnlyList<BookReviewsView>> GetReviewsAsync(IReadOnlyCollection<long> bookIds, CancellationToken ct);
}
