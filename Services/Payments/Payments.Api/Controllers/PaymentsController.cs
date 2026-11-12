using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Payments.Api.Controllers;

public record PaymentRecord(Guid Id, long LoanId, decimal Amount, string Method, string Status, DateTime CreatedAt, string? ProcessedBy);
public record ProcessPaymentRequest([Range(1, long.MaxValue)] long LoanId, [Range(0.01, 1_000_000)] decimal Amount, [Required] string Method);
public record RefundRequest([Required] Guid PaymentId, [Required, StringLength(200)] string Reason);

/// <summary>
/// Таблиця 3.3.3: GET /payments — payments:read, POST /payments/process — payments:process, POST /payments/refund —
/// payments:refund. Матриця ролей видає ці permissions лише admin: user / manager / guest отримують 403.
/// </summary>
[ApiController]
[Route("api/payments")]
[Produces("application/json")]
public class PaymentsController(ILogger<PaymentsController> logger) : ControllerBase
{
    private static readonly ConcurrentDictionary<Guid, PaymentRecord> Store = new();

    private string? Actor => User.FindFirstValue("preferred_username") ?? User.FindFirstValue("unique_name")
                             ?? User.FindFirstValue("name") ?? User.FindFirstValue("client_id") ?? User.FindFirstValue("sub");

    [HttpGet]
    [RequirePermission(Permissions.Payments.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentRecord>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<PaymentRecord>> GetAll()
        => Ok(Store.Values.OrderByDescending(p => p.CreatedAt).ToList());

    [HttpPost("process")]
    [RequirePermission(Permissions.Payments.Process)]
    [ProducesResponseType(typeof(PaymentRecord), StatusCodes.Status201Created)]
    public ActionResult<PaymentRecord> Process(ProcessPaymentRequest request)
    {
        var payment = new PaymentRecord(Guid.NewGuid(), request.LoanId, request.Amount, request.Method, "Processed", DateTime.UtcNow, Actor);
        Store[payment.Id] = payment;
        logger.LogInformation("Payment {PaymentId} for loan {LoanId}: {Amount} by {Actor}", payment.Id, request.LoanId, request.Amount, Actor);
        return StatusCode(StatusCodes.Status201Created, payment);
    }

    [HttpPost("refund")]
    [RequirePermission(Permissions.Payments.Refund)]
    [ProducesResponseType(typeof(PaymentRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<PaymentRecord> Refund(RefundRequest request)
    {
        if (!Store.TryGetValue(request.PaymentId, out var payment)) return Problem(statusCode: 404, title: "Payment not found");
        if (payment.Status == "Refunded") return Problem(statusCode: 409, title: "Payment is already refunded");
        var refunded = payment with { Status = "Refunded" };
        Store[payment.Id] = refunded;
        logger.LogInformation("Payment {PaymentId} refunded by {Actor}: {Reason}", payment.Id, Actor, request.Reason);
        return Ok(refunded);
    }
}
