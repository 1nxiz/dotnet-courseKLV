using Loans.Api.IntegrationEvents.Events;
using Loans.Bll.IntegrationEvents;
using Loans.Domain.Models;
using Platform.EventBus;

namespace Loans.Api.IntegrationEvents;

/// <summary>
/// Адаптер порту ILoanEventPublisher: LoanStatusChangedEvent у exchange loans-events (routing key loan.status_changed).
/// Викликається з LoanService після CommitAsync; збій брокера логується з бізнес-контекстом і не скасовує операцію.
/// </summary>
public sealed class LoanEventPublisher : ILoanEventPublisher
{
    private readonly IEventPublisher _publisher;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<LoanEventPublisher> _logger;

    public LoanEventPublisher(IEventPublisher publisher, IHttpContextAccessor http, ILogger<LoanEventPublisher> logger)
    {
        _publisher = publisher;
        _http = http;
        _logger = logger;
    }

    public async Task StatusChangedAsync(Loan loan, string oldStatus, CancellationToken ct)
    {
        var @event = new LoanStatusChangedEvent(loan.Id, loan.Number, oldStatus, loan.Status,
            loan.Items.Select(i => new LoanBookLine(i.BookId, i.Quantity)).ToList())
        {
            CorrelationId = _http.HttpContext?.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString()
        };
        try
        {
            await _publisher.PublishAsync(@event, Topology.LoansExchange, Topology.RoutingKeys.LoanStatusChanged, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish {EventType} {EventId} for loan {LoanId} ({OldStatus} -> {NewStatus}): change is saved, event is lost",
                nameof(LoanStatusChangedEvent), @event.EventId, loan.Id, oldStatus, loan.Status);
        }
    }
}
