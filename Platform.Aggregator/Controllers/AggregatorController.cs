using Microsoft.AspNetCore.Mvc;
using Platform.Aggregator.Models;
using Platform.Aggregator.Services;

namespace Platform.Aggregator.Controllers;

[ApiController]
[Route("api/aggregator")]
[Produces("application/json")]
public class AggregatorController(LoanCardAggregator aggregator) : ControllerBase
{
    /// <summary>Повна картка видачі: видача + книги з каталогу + відгуки (часткова відповідь із Warnings, якщо сервіс недоступний).</summary>
    [HttpGet("loans/{id:long}/full")]
    [ProducesResponseType(typeof(LoanCardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<LoanCardDto>> GetLoanCard(long id, CancellationToken ct)
    {
        var card = await aggregator.GetLoanCardAsync(id, ct);
        return card is null ? Problem(statusCode: 404, title: "Loan not found", detail: $"Loan {id} not found") : Ok(card);
    }
}
