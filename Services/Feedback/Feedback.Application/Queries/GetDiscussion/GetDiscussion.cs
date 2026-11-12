using Feedback.Application.Common.Exceptions;
using Feedback.Application.Common.Interfaces;
using Feedback.Application.Common.Messaging;
using Feedback.Application.Common.Models;
using FluentValidation;
using MediatR;
using MongoDB.Bson;

namespace Feedback.Application.Queries.GetDiscussion;

/// <summary>Обговорення + сторінка повідомлень з КУРСОРНОЮ пагінацією (після AfterMessageId).</summary>
public record GetDiscussionQuery(string Id, string? AfterMessageId, int PageSize = 20) : IQuery<DiscussionWithMessagesDto>;

public sealed class GetDiscussionQueryValidator : AbstractValidator<GetDiscussionQuery>
{
    public GetDiscussionQueryValidator()
    {
        RuleFor(x => x.Id).Must(id => ObjectId.TryParse(id, out _)).WithMessage("Id must be a valid ObjectId");
        RuleFor(x => x.AfterMessageId).Must(id => ObjectId.TryParse(id, out _)).When(x => x.AfterMessageId is not null)
            .WithMessage("AfterMessageId must be a valid ObjectId");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetDiscussionQueryHandler : IRequestHandler<GetDiscussionQuery, DiscussionWithMessagesDto>
{
    private readonly IDiscussionQueries _queries;

    public GetDiscussionQueryHandler(IDiscussionQueries queries) => _queries = queries;

    public async Task<DiscussionWithMessagesDto> Handle(GetDiscussionQuery q, CancellationToken ct)
    {
        var discussion = await _queries.GetByIdAsync(q.Id, ct) ?? throw new NotFoundException("Discussion", q.Id);
        var messages = await _queries.GetMessagesAsync(q.Id, q.AfterMessageId, q.PageSize, ct);
        return new DiscussionWithMessagesDto(discussion, messages);
    }
}
