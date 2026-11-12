using Feedback.Application.Caching;
using Feedback.Application.Common.Exceptions;
using Feedback.Application.Common.Messaging;
using Feedback.Domain.Interfaces;
using FluentValidation;
using MediatR;
using MongoDB.Bson;

namespace Feedback.Application.Commands.DeleteReview;

public record DeleteReviewCommand(string Id) : ICommand;

public sealed class DeleteReviewCommandValidator : AbstractValidator<DeleteReviewCommand>
{
    public DeleteReviewCommandValidator()
        => RuleFor(x => x.Id).Must(id => ObjectId.TryParse(id, out _)).WithMessage("Id must be a valid ObjectId");
}

public sealed class DeleteReviewCommandHandler : IRequestHandler<DeleteReviewCommand>
{
    private readonly IReviewRepository _reviews;
    private readonly IFeedbackCache _cache;

    public DeleteReviewCommandHandler(IReviewRepository reviews, IFeedbackCache cache)
    {
        _reviews = reviews;
        _cache = cache;
    }

    public async Task Handle(DeleteReviewCommand cmd, CancellationToken ct)
    {
        var id = ObjectId.Parse(cmd.Id);
        var review = await _reviews.GetByIdAsync(id, ct) ?? throw new NotFoundException("Review", cmd.Id);
        if (!await _reviews.DeleteAsync(id, ct))
            throw new NotFoundException("Review", cmd.Id);
        await _cache.InvalidateAsync("ReviewDeleted", ct, CacheKeys.Review(cmd.Id), CacheKeys.BookSummary(review.BookId));
    }
}
