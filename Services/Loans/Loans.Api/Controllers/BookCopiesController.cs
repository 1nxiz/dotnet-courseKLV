using Loans.Bll.Dtos;
using Loans.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace Loans.Api.Controllers;

/// <summary>
/// Локальна копія довідника книг у контексті «Видачі» (копія даних Каталогу + власна кількість на полиці).
/// Маршрут у просторі /api/loans, бо /api/books належить сервісу Каталогу.
/// </summary>
[ApiController]
[Route("api/loans/books")]
[Produces("application/json")]
public class BookCopiesController : ControllerBase
{
    private readonly IBookCopyService _books;

    public BookCopiesController(IBookCopyService books) => _books = books;

    [RequirePermission(Permissions.Loans.Read)]
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BookCopyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BookCopyDto>>> GetAll(CancellationToken ct)
        => Ok(await _books.GetAllAsync(ct));

    [RequirePermission(Permissions.Loans.Read)]
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(BookCopyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookCopyDto>> GetById(long id, CancellationToken ct)
        => Ok(await _books.GetByIdAsync(id, ct));

    /// <summary>Створити (201) або оновити (200) копію книги.</summary>
    [RequirePermission(Permissions.Loans.Update)]
    [HttpPut]
    [ProducesResponseType(typeof(BookCopyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BookCopyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BookCopyDto>> Upsert(UpsertBookCopyDto input, CancellationToken ct)
    {
        var (book, created) = await _books.UpsertAsync(input, User.Identity?.Name ?? "anonymous", ct);
        return created ? CreatedAtAction(nameof(GetById), new { id = book.Id }, book) : Ok(book);
    }

    [RequirePermission(Permissions.Loans.Delete)]
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _books.DeleteAsync(id, ct);
        return NoContent();
    }
}
