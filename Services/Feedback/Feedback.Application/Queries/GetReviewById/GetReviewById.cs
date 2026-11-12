using Feedback.Application.Common.Exceptions;
using Feedback.Application.Common.Interfaces;
using Feedback.Application.Common.Messaging;
using Feedback.Application.Common.Models;
using FluentValidation;
using MediatR;
using MongoDB.Bson;

namespace Feedback.Application.Queries.GetReviewById;

public record GetReviewByIdQuery(string Id) : IQuery<ReviewDetailsDto>;

public sealed class GetReviewByIdQueryValidator : AbstractValidator<GetReviewByIdQuery>
{
    public GetReviewByIdQueryValidator()
        => RuleFor(x => x.Id).Must(id => ObjectId.TryParse(id, out _)).WithMessage("Id must be a valid ObjectId");
}

/// <summary>Обробник запиту не звертається до MongoDB напряму — лише до IReviewQueries.</summary>
public sealed class GetReviewByIdQueryHandler : IRequestHandler<GetReviewByIdQuery, ReviewDetailsDto>
{
    private readonly IReviewQueries _queries;

    public GetReviewByIdQueryHandler(IReviewQueries queries) => _queries = queries;

    public async Task<ReviewDetailsDto> Handle(GetReviewByIdQuery query, CancellationToken ct)
        => await _queries.GetByIdAsync(query.Id, ct) ?? throw new NotFoundException("Review", query.Id);
}
