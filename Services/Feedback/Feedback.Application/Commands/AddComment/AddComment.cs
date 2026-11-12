using AutoMapper;
using Feedback.Application.Common.Exceptions;
using Feedback.Application.Common.Messaging;
using Feedback.Application.Common.Models;
using Feedback.Domain.Interfaces;
using Feedback.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using MongoDB.Bson;

namespace Feedback.Application.Commands.AddComment;

public record AddCommentRequest(string AuthorId, string AuthorName, string Text);

/// <summary>Додати коментар — новий елемент вбудованого масиву comments.</summary>
public record AddCommentCommand(string ReviewId, string AuthorId, string AuthorName, string Text) : ICommand<CommentDto>;

public sealed class AddCommentCommandValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentCommandValidator()
    {
        RuleFor(x => x.ReviewId).Must(id => ObjectId.TryParse(id, out _)).WithMessage("ReviewId must be a valid ObjectId");
        RuleFor(x => x.AuthorId).NotEmpty();
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(1000);
    }
}

public sealed class AddCommentCommandHandler : IRequestHandler<AddCommentCommand, CommentDto>
{
    private readonly IReviewRepository _reviews;
    private readonly IMapper _mapper;

    public AddCommentCommandHandler(IReviewRepository reviews, IMapper mapper)
    {
        _reviews = reviews;
        _mapper = mapper;
    }

    public async Task<CommentDto> Handle(AddCommentCommand cmd, CancellationToken ct)
    {
        var review = await _reviews.GetByIdAsync(ObjectId.Parse(cmd.ReviewId), ct) ?? throw new NotFoundException("Review", cmd.ReviewId);
        var comment = review.AddComment(new Author(cmd.AuthorId, cmd.AuthorName), cmd.Text);
        await _reviews.UpdateAsync(review, ct);
        return _mapper.Map<CommentDto>(comment);
    }
}
