using AutoMapper;
using Feedback.Application.Commands.AddComment;
using Feedback.Application.Commands.CreateReview;
using Feedback.Application.Commands.DeleteReview;
using Feedback.Application.Commands.UpdateReview;
using Feedback.Application.Common.Models;
using Feedback.Application.Queries.GetReviewById;
using Feedback.Application.Queries.GetReviewsList;
using Microsoft.AspNetCore.Mvc;

namespace Feedback.WebApi.Controllers;

/// <summary>Відгуки на книги. Кожна операція — команда або запит MediatR; жодного звернення до IMongoCollection.</summary>
[Route("api/reviews")]
public class ReviewsController : ApiControllerBase
{
    private readonly IMapper _mapper;

    public ReviewsController(IMapper mapper) => _mapper = mapper;

    /// <summary>Список відгуків: фільтри bookId, authorId, minRating, search (text-індекс) і пагінація.</summary>
    [RequirePermission(Permissions.Feedback.Read)]
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ReviewDto>>> GetList([FromQuery] long? bookId, [FromQuery] string? authorId,
        [FromQuery] int? minRating, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await Mediator.Send(new GetReviewsListQuery(bookId, authorId, minRating, search, page, pageSize), ct));

    /// <summary>Відгук із коментарями. Версія документа повертається в заголовку ETag.</summary>
    [RequirePermission(Permissions.Feedback.Read)]
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ReviewDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewDetailsDto>> GetById(string id, CancellationToken ct)
    {
        var review = await Mediator.Send(new GetReviewByIdQuery(id), ct);
        Response.Headers.ETag = $"\"{review.Version}\"";
        return Ok(review);
    }

    /// <summary>Створити відгук -> 201 + Location із рядковим ObjectId.</summary>
    [RequirePermission(Permissions.Feedback.Write)]
    [HttpPost]
    [ProducesResponseType(typeof(CreatedIdDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedIdDto>> Create(CreateReviewRequest request, CancellationToken ct)
    {
        var id = await Mediator.Send(_mapper.Map<CreateReviewCommand>(request), ct);
        return CreatedAtAction(nameof(GetById), new { id }, new CreatedIdDto(id));
    }

    /// <summary>Редагувати відгук. Заголовок If-Match: "версія" -> 409, якщо документ уже змінено.</summary>
    [RequirePermission(Permissions.Feedback.Write)]
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ReviewDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewDetailsDto>> Update(string id, UpdateReviewRequest request, CancellationToken ct)
    {
        int? expectedVersion = int.TryParse(Request.Headers.IfMatch.ToString().Trim('"', ' ', 'W', '/'), out var v) ? v : null;
        var review = await Mediator.Send(new UpdateReviewCommand(id, request.Rating, request.Text, request.PagesRead,
            request.Finished, expectedVersion), ct);
        Response.Headers.ETag = $"\"{review.Version}\"";
        return Ok(review);
    }

    [RequirePermission(Permissions.Feedback.Write)]
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await Mediator.Send(new DeleteReviewCommand(id), ct);
        return NoContent();
    }

    /// <summary>Додати коментар (вбудований масив comments).</summary>
    [RequirePermission(Permissions.Feedback.Write)]
    [HttpPost("{id}/comments")]
    [ProducesResponseType(typeof(CommentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommentDto>> AddComment(string id, AddCommentRequest request, CancellationToken ct)
    {
        var comment = await Mediator.Send(new AddCommentCommand(id, request.AuthorId, request.AuthorName, request.Text), ct);
        return CreatedAtAction(nameof(GetById), new { id }, comment);
    }
}

public record CreatedIdDto(string Id);
