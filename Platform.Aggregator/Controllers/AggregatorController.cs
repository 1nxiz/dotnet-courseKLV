using Microsoft.AspNetCore.Mvc;
using Platform.Aggregator.Models;
using Platform.Aggregator.Services;

namespace Platform.Aggregator.Controllers;

[ApiController]
[Route("api/aggregator")]
[Produces("application/json")]
public class AggregatorController(LoanCardAggregator aggregator) : ControllerBase
{
    /// <summary>
    /// Повна картка видачі: видача + книги з каталогу + підсумки відгуків. Внутрішні виклики — gRPC
    /// (transport=http — HTTP REST з Лаб. 05 для порівняння затримок); cache=false — обійти кеш Redis.
    /// </summary>
    [HttpGet("loans/{id:long}/full")]
    [ProducesResponseType(typeof(LoanCardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<LoanCardDto>> GetLoanCard(long id, [FromQuery] UpstreamTransport transport = UpstreamTransport.Grpc,
        [FromQuery] bool cache = true, CancellationToken ct = default)
    {
        var card = await aggregator.GetLoanCardAsync(id, transport, cache, ct);
        return card is null ? Problem(statusCode: 404, title: "Loan not found", detail: $"Loan {id} not found") : Ok(card);
    }
}
