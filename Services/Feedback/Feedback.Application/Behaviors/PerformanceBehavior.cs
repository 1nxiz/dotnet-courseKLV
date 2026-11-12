using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Feedback.Application.Behaviors;

/// <summary>3-й behavior (найближчий до обробника): вимірює час обробника разом із запитами до MongoDB
/// і попереджає, якщо він працює аномально довго.</summary>
public sealed class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private const int SlowThresholdMs = 500;
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;

    public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger) => _logger = logger;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        if (sw.ElapsedMilliseconds > SlowThresholdMs)
            _logger.LogWarning("Slow request {RequestName}: {ElapsedMs} ms (threshold {ThresholdMs} ms)",
                typeof(TRequest).Name, sw.ElapsedMilliseconds, SlowThresholdMs);
        return response;
    }
}
