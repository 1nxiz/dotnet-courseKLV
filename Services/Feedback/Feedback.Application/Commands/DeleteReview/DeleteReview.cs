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

    public DeleteReviewCommandHandler(IReviewRepository reviews) => _reviews = reviews;

    public async Task Handle(DeleteReviewCommand cmd, CancellationToken ct)
    {
        if (!await _reviews.DeleteAsync(ObjectId.Parse(cmd.Id), ct))
            throw new NotFoundException("Review", cmd.Id);
    }
}
