using Feedback.Application.Common.Exceptions;
using Feedback.Application.Common.Messaging;
using Feedback.Domain.Entities;
using Feedback.Domain.Interfaces;
using Feedback.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using MongoDB.Bson;

namespace Feedback.Application.Commands.StartDiscussion;

public record StartDiscussionRequest(long BookId, string BookTitle, string Title, string AuthorId, string AuthorName,
    string[]? Tags = null, string? ReviewId = null);

/// <summary>Почати обговорення книги; за потреби пов'язати його з відгуком (reference discussionId).</summary>
public record StartDiscussionCommand(long BookId, string BookTitle, string Title, string AuthorId, string AuthorName,
    string[]? Tags, string? ReviewId) : ICommand<string>;

public sealed class StartDiscussionCommandValidator : AbstractValidator<StartDiscussionCommand>
{
    public StartDiscussionCommandValidator()
    {
        RuleFor(x => x.BookId).GreaterThan(0);
        RuleFor(x => x.BookTitle).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().Length(3, 200);
        RuleFor(x => x.AuthorId).NotEmpty();
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Tags).Must(t => t is null || t.Length <= 10).WithMessage("At most 10 tags");
        RuleFor(x => x.ReviewId).Must(id => ObjectId.TryParse(id, out _)).When(x => x.ReviewId is not null)
            .WithMessage("ReviewId must be a valid ObjectId");
    }
}

public sealed class StartDiscussionCommandHandler : IRequestHandler<StartDiscussionCommand, string>
{
    private readonly IDiscussionRepository _discussions;
    private readonly IReviewRepository _reviews;

    public StartDiscussionCommandHandler(IDiscussionRepository discussions, IReviewRepository reviews)
    {
        _discussions = discussions;
        _reviews = reviews;
    }

    public async Task<string> Handle(StartDiscussionCommand cmd, CancellationToken ct)
    {
        var review = cmd.ReviewId is null ? null
            : await _reviews.GetByIdAsync(ObjectId.Parse(cmd.ReviewId), ct) ?? throw new NotFoundException("Review", cmd.ReviewId);

        var discussion = new Discussion(cmd.BookId, cmd.BookTitle, cmd.Title, new Author(cmd.AuthorId, cmd.AuthorName), cmd.Tags);
        await _discussions.AddAsync(discussion, ct);

        if (review is not null)
        {
            review.LinkDiscussion(discussion.Id);
            await _reviews.UpdateAsync(review, ct);
        }
        return discussion.Id.ToString();
    }
}
