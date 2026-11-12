using System.Diagnostics;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();                                   // Serilog, OpenTelemetry, service discovery, CorrelationIdHandler

// Маршрути й кластери — у секції ReverseProxy (appsettings.json); адреси http://loans-api тощо — логічні імена,
// їх перетворює на реальні адреси service discovery Aspire.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.UseCorrelationId();                                         // ДО MapReverseProxy: генерує X-Correlation-Id на вході

// Логування рішень маршрутизації й тривалості (структуровані властивості, без конкатенації рядків)
app.Use(async (context, next) =>
{
    var sw = Stopwatch.StartNew();
    try
    {
        await next();
    }
    finally
    {
        var proxy = context.Features.Get<IReverseProxyFeature>();
        var route = proxy?.Route.Config.RouteId;
        var destination = proxy?.ProxiedDestination?.Model.Config.Address;
        var level = context.Response.StatusCode >= 500 ? LogLevel.Warning : LogLevel.Information;
        app.Logger.Log(level,
            "{Method} {Path} from {ClientIp} -> route {Route} ({Destination}): {StatusCode} in {ElapsedMs} ms, {ResponseBytes} bytes",
            context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress, route ?? "none",
            destination ?? "-", context.Response.StatusCode, sw.ElapsedMilliseconds, context.Response.ContentLength);
    }
});

app.MapReverseProxy(pipeline =>
{
    // помилки недоступних сервісів (timeout, connection refused) — з деталями
    pipeline.Use(async (context, next) =>
    {
        await next();
        var error = context.GetForwarderErrorFeature();
        if (error is not null)
            app.Logger.LogWarning(error.Exception, "Proxy error {Error} for {Path}", error.Error, context.Request.Path);
    });
    pipeline.UseSessionAffinity();
    pipeline.UseLoadBalancing();                                  // RoundRobin для кластера books
    pipeline.UsePassiveHealthChecks();
});
app.MapDefaultEndpoints();
app.Run();
