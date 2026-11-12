using Feedback.Application.Common.Interfaces;
using Feedback.Application.Common.Messaging;
using Feedback.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace Feedback.Application.Queries.GetReviewsList;

/// <summary>Список відгуків з фільтрами й пагінацією — фільтрація ($match) і skip/limit виконуються в MongoDB.</summary>
public record GetReviewsListQuery(long? BookId, string? AuthorId, int? MinRating, string? Search, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<ReviewDto>>;

public sealed class GetReviewsListQueryValidator : AbstractValidator<GetReviewsListQuery>
{
    public GetReviewsListQueryValidator()
    {
        RuleFor(x => x.BookId).GreaterThan(0).When(x => x.BookId is not null);
        RuleFor(x => x.MinRating).InclusiveBetween(1, 5).When(x => x.MinRating is not null);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public sealed class GetReviewsListQueryHandler : IRequestHandler<GetReviewsListQuery, PagedResult<ReviewDto>>
{
    private readonly IReviewQueries _queries;

    public GetReviewsListQueryHandler(IReviewQueries queries) => _queries = queries;

    public Task<PagedResult<ReviewDto>> Handle(GetReviewsListQuery q, CancellationToken ct)
        => _queries.GetListAsync(q.BookId, q.AuthorId, q.MinRating, q.Search, q.Page, Math.Clamp(q.PageSize, 1, 100), ct);
}
