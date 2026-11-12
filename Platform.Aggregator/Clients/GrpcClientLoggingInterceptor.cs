using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Platform.Aggregator.Clients;

/// <summary>Клієнтський interceptor: метод, статус і тривалість кожного вихідного gRPC-виклику (структуровано).</summary>
public sealed class GrpcClientLoggingInterceptor : Interceptor
{
    private readonly ILogger<GrpcClientLoggingInterceptor> _logger;

    public GrpcClientLoggingInterceptor(ILogger<GrpcClientLoggingInterceptor> logger) => _logger = logger;

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var sw = Stopwatch.StartNew();
        var call = continuation(request, context);
        return new AsyncUnaryCall<TResponse>(Log(call.ResponseAsync, context.Method.FullName, sw),
            call.ResponseHeadersAsync, call.GetStatus, call.GetTrailers, call.Dispose);
    }

    private async Task<TResponse> Log<TResponse>(Task<TResponse> response, string method, Stopwatch sw)
    {
        try
        {
            var result = await response;
            _logger.LogInformation("gRPC call {GrpcMethod}: {GrpcStatus} in {ElapsedMs} ms", method, StatusCode.OK, sw.ElapsedMilliseconds);
            return result;
        }
        catch (RpcException ex)
        {
            _logger.LogWarning("gRPC call {GrpcMethod}: {GrpcStatus} {GrpcDetail} in {ElapsedMs} ms",
                method, ex.StatusCode, ex.Status.Detail, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
