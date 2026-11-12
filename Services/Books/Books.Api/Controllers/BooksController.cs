using Books.Bll.Dtos;
using Books.Bll.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Books.Api.Controllers;

/// <summary>
/// Каталог книг. Контролер не звертається до DbContext — лише до сервісу BLL. Прості правила DTO перевіряє
/// [ApiController] (DataAnnotations), складні — FluentValidation, викликана явно (ValidateAsync).
/// </summary>
[ApiController]
[Route("api/books")]
[Produces("application/json")]
public class BooksController : ControllerBase
{
    private readonly IBookService _books;

    public BooksController(IBookService books) => _books = books;

    /// <summary>Список з пагінацією, фільтрацією та сортуванням (виконуються в SQL).</summary>
    /// <remarks>Метадані пагінації — у тілі відповіді й у заголовках X-Pagination-TotalCount / -Page / -PageSize / -TotalPages.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<BookDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<BookDto>>> GetList([FromQuery] BookQuery query,
        [FromServices] IValidator<BookQuery> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(query, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        var result = await _books.GetListAsync(query, ct);
        Response.Headers["X-Pagination-TotalCount"] = result.TotalCount.ToString();
        Response.Headers["X-Pagination-Page"] = result.Page.ToString();
        Response.Headers["X-Pagination-PageSize"] = result.PageSize.ToString();
        Response.Headers["X-Pagination-TotalPages"] = result.TotalPages.ToString();
        return Ok(result);
    }

    /// <summary>Картка книги (eager loading: деталі, автор, зображення, жанри — одним запитом).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(BookCardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookCardDto>> GetById(int id, CancellationToken ct)
        => Ok(await _books.GetByIdAsync(id, ct));

    /// <summary>Створити книгу. Повторний ISBN -> 409.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(BookCardDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookCardDto>> Create(CreateBookDto input,
        [FromServices] IValidator<CreateBookDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        var created = await _books.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Оновити назву, автора, рік.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(BookDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookDto>> Update(int id, UpdateBookDto input,
        [FromServices] IValidator<UpdateBookDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return Ok(await _books.UpdateAsync(id, input, ct));
    }

    /// <summary>Змінити ціну (плату за видачу).</summary>
    [HttpPut("{id:int}/price")]
    [ProducesResponseType(typeof(BookDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookDto>> ChangePrice(int id, ChangePriceDto input,
        [FromServices] IValidator<ChangePriceDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return Ok(await _books.ChangePriceAsync(id, input.Price, ct));
    }

    /// <summary>Надходження примірників у фонд.</summary>
    [HttpPost("{id:int}/copies")]
    [ProducesResponseType(typeof(BookDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookDto>> AddCopies(int id, CopiesDto input,
        [FromServices] IValidator<CopiesDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return Ok(await _books.AddCopiesAsync(id, input.Quantity, ct));
    }

    /// <summary>Списання примірників. Більше, ніж є, -> 409 (правило сутності).</summary>
    [HttpPost("{id:int}/write-off")]
    [ProducesResponseType(typeof(BookDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookDto>> WriteOff(int id, CopiesDto input,
        [FromServices] IValidator<CopiesDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return Ok(await _books.WriteOffCopiesAsync(id, input.Quantity, ct));
    }

    /// <summary>Опублікувати книгу (стає видимою читачам).</summary>
    [HttpPost("{id:int}/publish")]
    [ProducesResponseType(typeof(BookDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookDto>> Publish(int id, CancellationToken ct)
        => Ok(await _books.PublishAsync(id, ct));

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _books.DeleteAsync(id, ct);
        return NoContent();
    }
}
