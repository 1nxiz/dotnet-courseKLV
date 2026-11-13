using Loans.Bll.Dtos;
using Loans.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace Loans.Api.Controllers;

/// <summary>Читачі бібліотеки (CRUD; репозиторій на чистому ADO.NET).</summary>
[ApiController]
[Route("api/readers")]
[Produces("application/json")]
public class ReadersController : ControllerBase
{
    private readonly IReaderService _readers;

    public ReadersController(IReaderService readers) => _readers = readers;

    private string CurrentUser => User.Identity?.Name ?? "anonymous";

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReaderDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ReaderDto>>> GetAll(CancellationToken ct)
        => Ok(await _readers.GetAllAsync(ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ReaderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReaderDto>> GetById(long id, CancellationToken ct)
        => Ok(await _readers.GetByIdAsync(id, ct));

    [HttpPost]
    [ProducesResponseType(typeof(ReaderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReaderDto>> Create(CreateReaderDto input, CancellationToken ct)
    {
        var created = await _readers.CreateAsync(input, CurrentUser, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(ReaderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReaderDto>> Update(long id, UpdateReaderDto input, CancellationToken ct)
        => Ok(await _readers.UpdateAsync(id, input, CurrentUser, ct));

    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _readers.DeleteAsync(id, ct);
        return NoContent();
    }
}
