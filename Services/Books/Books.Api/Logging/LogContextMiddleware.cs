using Serilog.Context;

namespace Books.Api.Logging;

/// <summary>
/// Додає в усі логи запиту структуровані властивості CorrelationId (заголовок X-Correlation-Id або TraceIdentifier)
/// та UserId (якщо користувач автентифікований). RequestPath додає Serilog/ASP.NET Core автоматично.
/// </summary>
public sealed class LogContextMiddleware
{
    public const string CorrelationHeader = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public LogContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(CorrelationHeader, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : context.TraceIdentifier;
        context.Response.Headers[CorrelationHeader] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("UserId", context.User.Identity?.Name ?? "anonymous"))
        {
            await _next(context);
        }
    }
}
