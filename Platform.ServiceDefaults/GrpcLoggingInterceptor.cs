using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Серверний gRPC-interceptor (Лаб. 06), спільний для всіх сервісів: структуровано логує метод, адресу клієнта (peer),
/// статус і тривалість кожного виклику; RpcException — Warning, неочікуваний виняток — Error зі stack trace
/// і перетворення на Internal без деталей реалізації. CorrelationId потрапляє в записи з LogContext
/// (CorrelationIdMiddleware читає його з метаданих gRPC — це звичайний HTTP/2-заголовок x-correlation-id).
/// Підключення: builder.Services.AddGrpc(o => o.Interceptors.Add&lt;GrpcLoggingInterceptor&gt;()).
/// </summary>
public sealed class GrpcLoggingInterceptor : Interceptor
{
    private readonly ILogger<GrpcLoggingInterceptor> _logger;

    public GrpcLoggingInterceptor(ILogger<GrpcLoggingInterceptor> logger) => _logger = logger;

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await continuation(request, context);
            _logger.LogInformation("gRPC {GrpcMethod} from {Peer}: {GrpcStatus} in {ElapsedMs} ms",
                context.Method, context.Peer, StatusCode.OK, sw.ElapsedMilliseconds);
            return response;
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "gRPC {GrpcMethod} from {Peer}: {GrpcStatus} {GrpcDetail} in {ElapsedMs} ms",
                context.Method, context.Peer, ex.StatusCode, ex.Status.Detail, sw.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "gRPC {GrpcMethod} from {Peer}: unhandled {ExceptionType} after {ElapsedMs} ms",
                context.Method, context.Peer, ex.GetType().Name, sw.ElapsedMilliseconds);
            throw new RpcException(new Status(StatusCode.Internal, "Internal error"));
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request,
        IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await continuation(request, responseStream, context);
            _logger.LogInformation("gRPC stream {GrpcMethod} from {Peer} completed in {ElapsedMs} ms",
                context.Method, context.Peer, sw.ElapsedMilliseconds);
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "gRPC stream {GrpcMethod}: {GrpcStatus}", context.Method, ex.StatusCode);
            throw;
        }
    }
}
