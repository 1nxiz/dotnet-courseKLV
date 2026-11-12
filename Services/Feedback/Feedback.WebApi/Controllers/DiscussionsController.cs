using Feedback.Application.Commands.PostMessage;
using Feedback.Application.Commands.StartDiscussion;
using Feedback.Application.Common.Models;
using Feedback.Application.Queries.GetDiscussion;
using Microsoft.AspNetCore.Mvc;

namespace Feedback.WebApi.Controllers;

/// <summary>Обговорення книг (гібридна модель: subset останніх повідомлень + окрема колекція messages).</summary>
[Route("api/reviews/discussions")]
public class DiscussionsController : ApiControllerBase
{
    /// <summary>Обговорення + повідомлення з курсорною пагінацією (?after=&lt;id останнього отриманого&gt;).</summary>
    [RequirePermission(Permissions.Feedback.Read)]
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(DiscussionWithMessagesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiscussionWithMessagesDto>> Get(string id, [FromQuery] string? after,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await Mediator.Send(new GetDiscussionQuery(id, after, pageSize), ct));

    [RequirePermission(Permissions.Feedback.Write)]
    [HttpPost]
    [ProducesResponseType(typeof(CreatedIdDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreatedIdDto>> Start(StartDiscussionRequest request, CancellationToken ct)
    {
        var id = await Mediator.Send(new StartDiscussionCommand(request.BookId, request.BookTitle, request.Title,
            request.AuthorId, request.AuthorName, request.Tags, request.ReviewId), ct);
        return CreatedAtAction(nameof(Get), new { id }, new CreatedIdDto(id));
    }

    [RequirePermission(Permissions.Feedback.Write)]
    [HttpPost("{id}/messages")]
    [ProducesResponseType(typeof(CreatedIdDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedIdDto>> PostMessage(string id, PostMessageRequest request, CancellationToken ct)
    {
        var messageId = await Mediator.Send(new PostMessageCommand(id, request.AuthorId, request.AuthorName, request.Text,
            request.ReplyToId), ct);
        return CreatedAtAction(nameof(Get), new { id }, new CreatedIdDto(messageId));
    }
}
