using Books.Bll.Dtos;
using Books.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace Books.Api.Controllers;

[ApiController]
[Route("api/books/genres")]
[Produces("application/json")]
public class GenresController : ControllerBase
{
    private readonly IGenreService _genres;

    public GenresController(IGenreService genres) => _genres = genres;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<GenreDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GenreDto>>> GetAll(CancellationToken ct)
        => Ok(await _genres.GetAllAsync(ct));

    /// <summary>Жанр із книгами (explicit loading — 2 SQL-запити).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(GenreWithBooksDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GenreWithBooksDto>> GetWithBooks(int id, CancellationToken ct)
        => Ok(await _genres.GetWithBooksAsync(id, ct));

    /// <summary>Нещодавно додані в жанр (LINQ через проміжну сутність M:N і її поле AddedAt).</summary>
    [HttpGet("{slug}/recent")]
    [ProducesResponseType(typeof(IReadOnlyList<BookDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BookDto>>> GetRecentlyAdded(string slug, [FromQuery] DateTime? since,
        CancellationToken ct)
        => Ok(await _genres.GetRecentlyAddedAsync(slug, since ?? DateTime.UtcNow.AddMonths(-3), ct));

    /// <summary>Кількість книг у кожному жанрі.</summary>
    [HttpGet("statistics")]
    [ProducesResponseType(typeof(IReadOnlyList<GenreStatisticsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GenreStatisticsDto>>> GetStatistics(CancellationToken ct)
        => Ok(await _genres.GetStatisticsAsync(ct));
}
