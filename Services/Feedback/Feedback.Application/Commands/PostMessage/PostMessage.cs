using Feedback.Application.Common.Exceptions;
using Feedback.Application.Common.Messaging;
using Feedback.Domain.Entities;
using Feedback.Domain.Interfaces;
using Feedback.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using MongoDB.Bson;

namespace Feedback.Application.Commands.PostMessage;

public record PostMessageRequest(string AuthorId, string AuthorName, string Text, string? ReplyToId = null);

/// <summary>Нове повідомлення: документ у messages + оновлення вбудованого «хвоста» lastMessages в обговоренні.</summary>
public record PostMessageCommand(string DiscussionId, string AuthorId, string AuthorName, string Text, string? ReplyToId)
    : ICommand<string>;

public sealed class PostMessageCommandValidator : AbstractValidator<PostMessageCommand>
{
    public PostMessageCommandValidator()
    {
        RuleFor(x => x.DiscussionId).Must(id => ObjectId.TryParse(id, out _)).WithMessage("DiscussionId must be a valid ObjectId");
        RuleFor(x => x.ReplyToId).Must(id => ObjectId.TryParse(id, out _)).When(x => x.ReplyToId is not null)
            .WithMessage("ReplyToId must be a valid ObjectId");
        RuleFor(x => x.AuthorId).NotEmpty();
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(4000);
    }
}

public sealed class PostMessageCommandHandler : IRequestHandler<PostMessageCommand, string>
{
    private readonly IDiscussionRepository _discussions;

    public PostMessageCommandHandler(IDiscussionRepository discussions) => _discussions = discussions;

    public async Task<string> Handle(PostMessageCommand cmd, CancellationToken ct)
    {
        var discussion = await _discussions.GetByIdAsync(ObjectId.Parse(cmd.DiscussionId), ct)
                         ?? throw new NotFoundException("Discussion", cmd.DiscussionId);

        ObjectId? replyTo = null;
        if (cmd.ReplyToId is not null)
        {
            var parent = await _discussions.GetMessageAsync(ObjectId.Parse(cmd.ReplyToId), ct);
            if (parent is null || parent.DiscussionId != discussion.Id)
                throw new NotFoundException("Message", cmd.ReplyToId);
            replyTo = parent.Id;
        }

        var message = new Message(discussion.Id, new Author(cmd.AuthorId, cmd.AuthorName), cmd.Text, replyTo);
        await _discussions.AddMessageAsync(message, ct);
        discussion.RegisterMessage(message);
        await _discussions.UpdateAsync(discussion, ct);     // optimistic concurrency за Version
        return message.Id.ToString();
    }
}
