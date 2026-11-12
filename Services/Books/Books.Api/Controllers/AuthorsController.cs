using Books.Bll.Dtos;
using Books.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace Books.Api.Controllers;

[ApiController]
[Route("api/books/authors")]
[Produces("application/json")]
public class AuthorsController : ControllerBase
{
    private readonly IAuthorService _authors;
    private readonly IBookService _books;

    public AuthorsController(IAuthorService authors, IBookService books)
    {
        _authors = authors;
        _books = books;
    }

    [RequirePermission(Permissions.Books.Read)]
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AuthorDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AuthorDto>>> GetAll(CancellationToken ct)
        => Ok(await _authors.GetAllAsync(ct));

    [RequirePermission(Permissions.Books.Read)]
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(AuthorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AuthorDto>> GetById(int id, CancellationToken ct)
        => Ok(await _authors.GetByIdAsync(id, ct));

    /// <summary>Опубліковані книги автора (друга специфікація через той самий репозиторій).</summary>
    [RequirePermission(Permissions.Books.Read)]
    [HttpGet("{id:int}/books")]
    [ProducesResponseType(typeof(IReadOnlyList<BookDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<BookDto>>> GetBooks(int id, CancellationToken ct)
        => Ok(await _books.GetPublishedByAuthorAsync(id, ct));

    [RequirePermission(Permissions.Books.Create)]
    [HttpPost]
    [ProducesResponseType(typeof(AuthorDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthorDto>> Create(CreateAuthorDto input, CancellationToken ct)
    {
        var created = await _authors.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }
}
