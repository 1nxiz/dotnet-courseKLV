using System.Diagnostics.Metrics;
using Loans.Dal.Abstractions;
using Loans.Domain.Constants;

namespace Loans.Api.Metrics;

/// <summary>
/// Джерело gauge loans.active: фоновий сервіс раз на 30 с рахує активні видачі за статусами в БД
/// (callback gauge має бути миттєвим, тому БД у ньому не опитується).
/// </summary>
public sealed class LoansStatsSnapshot : BackgroundService
{
    private static readonly string[] ActiveStatuses =
        [LoanStatus.AwaitingValidation, LoanStatus.Reserved, LoanStatus.Paid, LoanStatus.Issued];

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LoansStatsSnapshot> _logger;
    private volatile IReadOnlyDictionary<string, long> _counts = new Dictionary<string, long>();

    public LoansStatsSnapshot(IServiceScopeFactory scopes, ILogger<LoansStatsSnapshot> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public IEnumerable<Measurement<long>> Observe()
        => _counts.Select(kv => new Measurement<long>(kv.Value, new KeyValuePair<string, object?>("loan.status", kv.Key)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var counts = new Dictionary<string, long>();
                foreach (var status in ActiveStatuses)
                    counts[status] = (await uow.Loans.GetPageAsync(null, status, 1, 1, stoppingToken)).TotalCount;
                _counts = counts;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Loans statistics snapshot failed");   // gauge лишає попередні значення
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
