using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Platform.Aggregator.Resilience;

/// <summary>
/// Профіль стійкості для клієнта одного сервісу (Лаб. 07). Значення й обґрунтування — у README (таблиця політик).
/// Критичні сервіси (loans, books) — більше повторів і чутливіший circuit breaker: без них картку не зібрати.
/// Некритичний (feedback) — 1 повтор, короткі таймаути, толерантний breaker: краще швидко повернути часткову відповідь.
/// </summary>
public sealed record ServiceResilienceProfile(
    string Service,
    int HttpRetries,
    TimeSpan RetryDelay,
    TimeSpan AttemptTimeout,
    TimeSpan TotalTimeout,
    double BreakerFailureRatio,
    int BreakerMinimumThroughput,
    TimeSpan BreakerSamplingDuration,
    TimeSpan BreakDuration,
    int GrpcMaxAttempts)
{
    public static readonly ServiceResilienceProfile Loans = new("loans-api",
        HttpRetries: 3, RetryDelay: TimeSpan.FromMilliseconds(300), AttemptTimeout: TimeSpan.FromSeconds(3),
        TotalTimeout: TimeSpan.FromSeconds(15), BreakerFailureRatio: 0.3, BreakerMinimumThroughput: 10,
        BreakerSamplingDuration: TimeSpan.FromSeconds(30), BreakDuration: TimeSpan.FromSeconds(15), GrpcMaxAttempts: 4);

    public static readonly ServiceResilienceProfile Books = new("books-api",
        HttpRetries: 4, RetryDelay: TimeSpan.FromMilliseconds(300), AttemptTimeout: TimeSpan.FromSeconds(3),
        TotalTimeout: TimeSpan.FromSeconds(20), BreakerFailureRatio: 0.3, BreakerMinimumThroughput: 10,
        BreakerSamplingDuration: TimeSpan.FromSeconds(30), BreakDuration: TimeSpan.FromSeconds(15), GrpcMaxAttempts: 4);

    public static readonly ServiceResilienceProfile Feedback = new("feedback-api",
        HttpRetries: 1, RetryDelay: TimeSpan.FromMilliseconds(200), AttemptTimeout: TimeSpan.FromSeconds(2),
        TotalTimeout: TimeSpan.FromSeconds(5), BreakerFailureRatio: 0.7, BreakerMinimumThroughput: 10,
        BreakerSamplingDuration: TimeSpan.FromSeconds(30), BreakDuration: TimeSpan.FromSeconds(30), GrpcMaxAttempts: 2);

    /// <summary>Налаштування Standard Resilience Handler (rate limiter → total timeout → retry → circuit breaker → attempt timeout).</summary>
    public void Apply(HttpStandardResilienceOptions o, ILogger logger, bool retryOnHttpLevel)
    {
        o.TotalRequestTimeout.Timeout = TotalTimeout;
        o.AttemptTimeout.Timeout = AttemptTimeout;

        o.Retry.MaxRetryAttempts = HttpRetries;
        o.Retry.Delay = RetryDelay;
        o.Retry.BackoffType = DelayBackoffType.Exponential;
        o.Retry.UseJitter = true;                                  // проти thundering herd
        o.Retry.DisableForUnsafeHttpMethods();                     // POST/PUT/DELETE не повторюються
        if (!retryOnHttpLevel)
            o.Retry.ShouldHandle = _ => ValueTask.FromResult(false); // gRPC: повтори робить ServiceConfig за gRPC-статусами
        var service = Service;
        o.Retry.OnRetry = args =>
        {
            logger.LogWarning("Retry {Attempt} to {Service} after {DelayMs} ms: {Reason}", args.AttemptNumber + 1, service,
                args.RetryDelay.TotalMilliseconds, args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
            return ValueTask.CompletedTask;
        };

        o.CircuitBreaker.FailureRatio = BreakerFailureRatio;
        o.CircuitBreaker.MinimumThroughput = BreakerMinimumThroughput;   // навчальний стенд: мало трафіку
        o.CircuitBreaker.SamplingDuration = BreakerSamplingDuration;     // >= 2 x AttemptTimeout (валідація Polly)
        o.CircuitBreaker.BreakDuration = BreakDuration;
        o.CircuitBreaker.OnOpened = args =>
        {
            logger.LogError("Circuit to {Service} OPENED for {BreakSeconds} s: {Reason}", service, args.BreakDuration.TotalSeconds,
                args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
            return ValueTask.CompletedTask;
        };
        o.CircuitBreaker.OnHalfOpened = _ =>
        {
            logger.LogInformation("Circuit to {Service} HALF-OPEN: probing", service);
            return ValueTask.CompletedTask;
        };
        o.CircuitBreaker.OnClosed = _ =>
        {
            logger.LogInformation("Circuit to {Service} CLOSED", service);
            return ValueTask.CompletedTask;
        };
    }

    /// <summary>Retry-політика gRPC-каналу: повтор лише тимчасових статусів (у trailers, які HTTP-обробник не бачить).</summary>
    public ServiceConfig GrpcServiceConfig() => new()
    {
        MethodConfigs =
        {
            new MethodConfig
            {
                Names = { MethodName.Default },
                RetryPolicy = new RetryPolicy
                {
                    MaxAttempts = GrpcMaxAttempts,
                    InitialBackoff = TimeSpan.FromMilliseconds(200),
                    MaxBackoff = TimeSpan.FromSeconds(2),
                    BackoffMultiplier = 2,
                    RetryableStatusCodes = { StatusCode.Unavailable, StatusCode.ResourceExhausted }
                }
            }
        }
    };
}
