using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Loans.Api.Metrics;

/// <summary>
/// Бізнес-метрики сервісу видач (Meter "Loans.Api", підписаний у ServiceDefaults). Singleton.
/// Теги лише з малою кількістю значень (тип доставки, статус, спосіб оплати, операція, результат) —
/// жодних id читачів чи видач, щоб не плодити часові ряди.
/// </summary>
public sealed class LoansMetrics
{
    public const string MeterName = "Loans.Api";

    private readonly Counter<long> _created;
    private readonly Counter<long> _statusChanged;
    private readonly Counter<double> _paymentsAmount;
    private readonly Histogram<double> _duration;

    public LoansMetrics(IMeterFactory meterFactory, LoansStatsSnapshot snapshot)
    {
        var meter = meterFactory.Create(MeterName);
        _created = meter.CreateCounter<long>("loans.created", "{loan}", "Number of created loans");
        _statusChanged = meter.CreateCounter<long>("loans.status_changed", "{loan}", "Loan status transitions by target status");
        _paymentsAmount = meter.CreateCounter<double>("loans.payments.amount", "UAH", "Sum of registered payments");
        _duration = meter.CreateHistogram<double>("loans.operation.duration", "ms", "Duration of loan operations (success and failure)");

        // gauge: знімок поточного стану, значення читається в момент збору метрик
        meter.CreateObservableGauge("loans.active", () => snapshot.Observe(), "{loan}",
            "Loans currently in an active status (awaiting / reserved / paid / issued)");
    }

    public void LoanCreated(string deliveryType) => _created.Add(1, new KeyValuePair<string, object?>("delivery.type", deliveryType));

    public void StatusChanged(string status) => _statusChanged.Add(1, new KeyValuePair<string, object?>("loan.status", status));

    public void PaymentRegistered(string method, decimal amount)
        => _paymentsAmount.Add((double)amount, new KeyValuePair<string, object?>("payment.method", method));

    /// <summary>Вимірює тривалість УСІХ виконань — і успішних, і невдалих (тег outcome).</summary>
    public async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();
        var outcome = "failure";
        try
        {
            var result = await action();
            outcome = "success";
            return result;
        }
        finally
        {
            _duration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("operation", operation),
                new KeyValuePair<string, object?>("outcome", outcome));
        }
    }

    public Task MeasureAsync(string operation, Func<Task> action)
        => MeasureAsync(operation, async () => { await action(); return true; });
}
