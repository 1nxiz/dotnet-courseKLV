using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// CorrelationId — GUID, створений один раз на вході в систему (у Gateway) і переданий через усі сервіси.
/// Middleware бере X-Correlation-Id із запиту або генерує новий, кладе в LogContext (потрапляє в усі логи запиту),
/// у Request.Headers (його побачать YARP і HttpClient) та в заголовок відповіді; логує межі запиту.
/// На відміну від TraceId (технічне трасування OpenTelemetry), це бізнес-ідентифікатор звернення.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var generated = false;
        if (!context.Request.Headers.TryGetValue(HeaderName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            value = Guid.NewGuid().ToString();
            context.Request.Headers[HeaderName] = value;               // далі його побачать YARP і HttpClient
            generated = true;
        }

        var correlationId = value.ToString();
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            if (generated)
                _logger.LogDebug("CorrelationId {CorrelationId} generated for {Method} {Path}", correlationId,
                    context.Request.Method, context.Request.Path);
            _logger.LogInformation("Request started {Method} {Path}", context.Request.Method, context.Request.Path);
            await _next(context);
            _logger.LogInformation("Request finished {Method} {Path} with {StatusCode}", context.Request.Method,
                context.Request.Path, context.Response.StatusCode);
        }
    }
}

/// <summary>Додає той самий X-Correlation-Id у вихідні HTTP- і gRPC-запити (через фабрику HttpClient).</summary>
public sealed class CorrelationIdHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _accessor;

    public CorrelationIdHandler(IHttpContextAccessor accessor) => _accessor = accessor;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var id = _accessor.HttpContext?.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        if (!string.IsNullOrEmpty(id) && !request.Headers.Contains(CorrelationIdMiddleware.HeaderName))
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, id);
        return base.SendAsync(request, ct);
    }
}
