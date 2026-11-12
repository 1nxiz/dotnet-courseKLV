using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Feedback.Application.Behaviors;

/// <summary>
/// 1-й (зовнішній) behavior: структуроване логування кожної команди/запиту й результату.
/// Якщо ValidationBehavior відхилив запит, у лозі буде «Handling ...» і «... failed», але НЕ буде «Handled» —
/// видно, що обробник не виконувався.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger) => _logger = logger;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        _logger.LogInformation("Handling {RequestName} {@Request}", name, request);
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next();
            _logger.LogInformation("Handled {RequestName} in {ElapsedMs} ms", name, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("{RequestName} failed after {ElapsedMs} ms: {ExceptionType} {Error}",
                name, sw.ElapsedMilliseconds, ex.GetType().Name, ex.Message);
            throw;
        }
    }
}
