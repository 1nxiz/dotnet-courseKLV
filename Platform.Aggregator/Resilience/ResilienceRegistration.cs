using Grpc.Core;
using Microsoft.Extensions.Http.Resilience;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Security;

namespace Platform.Aggregator.Resilience;

/// <summary>
/// Реєстрація клієнтів із власними політиками. ServiceDefaults додає Standard Resilience Handler до ВСІХ HttpClient;
/// щоб не нашаровувати обробники, для кожного клієнта спершу RemoveAllResilienceHandlers(), потім — один свій.
/// </summary>
public static class ResilienceRegistration
{
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers — експериментальний API Microsoft.Extensions.Http.Resilience

    /// <summary>gRPC-клієнт: retry — gRPC ServiceConfig (за статусами), HTTP-рівень — circuit breaker і таймаути.</summary>
    public static IHttpClientBuilder AddResilientGrpcClient<TClient>(this IServiceCollection services, ServiceResilienceProfile profile,
        IConfiguration configuration)
        where TClient : ClientBase
        => services.AddGrpcClient<TClient>(o => o.Address = new Uri($"http://_grpc.{profile.Service}"))
            .ConfigureChannel(c => c.ServiceConfig = profile.GrpcServiceConfig())
            .AddInterceptor<GrpcClientLoggingInterceptor>()
            .RemoveAllResilienceHandlers()
            .ApplyProfile(profile, retryOnHttpLevel: false)
            .AddUpstreamToken(configuration);                       // Лаб. 09: Bearer-токен сервісу (Client Credentials)

    /// <summary>HTTP-клієнт: увесь Standard Resilience Handler із параметрами профілю.</summary>
    public static IHttpClientBuilder AddResilientHttpClient<TClient>(this IServiceCollection services, ServiceResilienceProfile profile,
        IConfiguration configuration)
        where TClient : class
        => services.AddHttpClient<TClient>(c => c.BaseAddress = new Uri($"https+http://{profile.Service}"))
            .RemoveAllResilienceHandlers()
            .ApplyProfile(profile, retryOnHttpLevel: true)
            .AddUpstreamToken(configuration);

#pragma warning restore EXTEXP0001

    private static IHttpClientBuilder ApplyProfile(this IHttpClientBuilder builder, ServiceResilienceProfile profile, bool retryOnHttpLevel)
    {
        builder.AddStandardResilienceHandler().Configure((options, sp) =>
            profile.Apply(options, sp.GetRequiredService<ILoggerFactory>().CreateLogger("Platform.Aggregator.Resilience"), retryOnHttpLevel));
        return builder;
    }
}
