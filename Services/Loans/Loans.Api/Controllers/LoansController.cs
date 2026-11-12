using Loans.Api.Metrics;
using Loans.Bll.Dtos;
using Loans.Bll.Services;
using Loans.Domain.Common;
using Loans.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace Loans.Api.Controllers;

/// <summary>
/// Видачі книг. Тонкий адаптер HTTP -> BLL: без SQL і без бізнес-правил; помилки перетворює
/// на статуси глобальний обробник (404 / 409 / 400 у форматі ProblemDetails).
/// </summary>
[ApiController]
[Route("api/loans")]
[Produces("application/json")]
public class LoansController : ControllerBase
{
    private readonly ILoanService _loans;
    private readonly LoansMetrics _metrics;

    public LoansController(ILoanService loans, LoansMetrics metrics)
    {
        _loans = loans;
        _metrics = metrics;
    }

    private string CurrentUser => User.Identity?.Name ?? "anonymous";

    /// <summary>Список видач з фільтрами та пагінацією.</summary>
    [RequirePermission(Permissions.Loans.Read)]
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<LoanSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<LoanSummaryDto>>> GetList([FromQuery] LoanQuery query, CancellationToken ct)
        => Ok(await _loans.GetPageAsync(query, ct));

    /// <summary>Картка видачі з позиціями та деталями.</summary>
    [RequirePermission(Permissions.Loans.Read)]
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(LoanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoanDto>> GetById(long id, CancellationToken ct)
        => Ok(await _loans.GetByIdAsync(id, ct));

    /// <summary>Оформити видачу. Назва й ціна книг фіксуються в позиціях як знімок.</summary>
    [RequirePermission(Permissions.Loans.Create)]
    [HttpPost]
    [ProducesResponseType(typeof(LoanDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LoanDto>> Create(CreateLoanDto input, CancellationToken ct)
    {
        var created = await _metrics.MeasureAsync("create", () => _loans.CreateAsync(input, CurrentUser, ct));
        _metrics.LoanCreated(input.DeliveryType);                // лічильник — одразу після успішної операції
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Змінити деталі доставки (оптимістична конкурентність через RowVersion).</summary>
    [RequirePermission(Permissions.Loans.Update)]
    [HttpPut("{id:long}/details")]
    [ProducesResponseType(typeof(RowVersionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RowVersionDto>> UpdateDetails(long id, UpdateLoanDetailsDto input, CancellationToken ct)
        => Ok(await _loans.UpdateDetailsAsync(id, input, CurrentUser, ct));

    /// <summary>Підтвердити видачу: Reserved + резервування примірників в одній транзакції.</summary>
    [RequirePermission(Permissions.Loans.Approve)]
    [HttpPost("{id:long}/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(long id, CancellationToken ct)
    {
        await _metrics.MeasureAsync("confirm", () => _loans.ConfirmAsync(id, CurrentUser, ct));
        _metrics.StatusChanged(LoanStatus.Reserved);
        return NoContent();
    }

    /// <summary>Оплатити видачу. Заголовок Idempotency-Key захищає від повторного списання.</summary>
    [RequirePermission(Permissions.Loans.Update)]
    [HttpPost("{id:long}/pay")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> Pay(long id, PayLoanDto input,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        var payment = await _metrics.MeasureAsync("pay", () => _loans.PayAsync(id, input, idempotencyKey, CurrentUser, ct));
        _metrics.PaymentRegistered(payment.Method, payment.Amount);
        _metrics.StatusChanged(LoanStatus.Paid);
        return Ok(payment);
    }

    /// <summary>Платежі видачі.</summary>
    [RequirePermission(Permissions.Loans.Read)]
    [HttpGet("{id:long}/payments")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PaymentDto>>> GetPayments(long id, CancellationToken ct)
        => Ok(await _loans.GetPaymentsAsync(id, ct));

    /// <summary>Видати книги читачеві (лише оплачену видачу).</summary>
    [RequirePermission(Permissions.Loans.Update)]
    [HttpPost("{id:long}/issue")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Issue(long id, CancellationToken ct)
    {
        await _metrics.MeasureAsync("issue", () => _loans.IssueAsync(id, CurrentUser, ct));
        _metrics.StatusChanged(LoanStatus.Issued);
        return NoContent();
    }

    /// <summary>Повернення книг: Returned + примірники на полицю.</summary>
    [RequirePermission(Permissions.Loans.Update)]
    [HttpPost("{id:long}/return")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Return(long id, CancellationToken ct)
    {
        await _metrics.MeasureAsync("return", () => _loans.ReturnAsync(id, CurrentUser, ct));
        _metrics.StatusChanged(LoanStatus.Returned);
        return NoContent();
    }

    /// <summary>Скасувати видачу (лише до оплати).</summary>
    [RequirePermission(Permissions.Loans.Update)]
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
    {
        await _metrics.MeasureAsync("cancel", () => _loans.CancelAsync(id, CurrentUser, ct));
        _metrics.StatusChanged(LoanStatus.Cancelled);
        return NoContent();
    }

    /// <summary>М'яко видалити скасовану або повернену видачу.</summary>
    [RequirePermission(Permissions.Loans.Delete)]
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _loans.DeleteAsync(id, ct);
        return NoContent();
    }
}
