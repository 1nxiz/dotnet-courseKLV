using Feedback.Application.Caching;
using Feedback.Application.Common.Interfaces;
using Feedback.Application.Common.Messaging;
using Feedback.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace Feedback.Application.Queries.GetBookReviewsSummary;

/// <summary>
/// Підсумок відгуків книги: кількість, середня оцінка і кілька останніх відгуків. Read-heavy (Aggregator, gRPC),
/// тому кешується (ICachedQuery). Кешується завжди MaxLatest відгуків, а обрізання до потрібної кількості —
/// у gRPC-адаптері: так один ключ на книгу, і інвалідувати його просто.
/// </summary>
public record GetBookReviewsSummaryQuery(long BookId) : IQuery<BookReviewsSummaryDto>, ICachedQuery
{
    public const int MaxLatest = 5;
    public string CacheKey => CacheKeys.BookSummary(BookId);
    public CachePolicy Policy => CachePolicy.BookSummary;
}

public sealed class GetBookReviewsSummaryQueryValidator : AbstractValidator<GetBookReviewsSummaryQuery>
{
    public GetBookReviewsSummaryQueryValidator() => RuleFor(x => x.BookId).GreaterThan(0);
}

public sealed class GetBookReviewsSummaryQueryHandler : IRequestHandler<GetBookReviewsSummaryQuery, BookReviewsSummaryDto>
{
    private readonly IReviewQueries _queries;

    public GetBookReviewsSummaryQueryHandler(IReviewQueries queries) => _queries = queries;

    public Task<BookReviewsSummaryDto> Handle(GetBookReviewsSummaryQuery query, CancellationToken ct)
        => _queries.GetBookSummaryAsync(query.BookId, GetBookReviewsSummaryQuery.MaxLatest, ct);
}
