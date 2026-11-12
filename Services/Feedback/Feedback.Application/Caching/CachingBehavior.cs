using MediatR;

namespace Feedback.Application.Caching;

/// <summary>Запит, результат якого можна кешувати: ключ обчислюється з параметрів запиту.</summary>
public interface ICachedQuery
{
    string CacheKey { get; }
    CachePolicy Policy { get; }
}

/// <summary>
/// Pipeline behavior (CQRS): для запитів з ICachedQuery виконує cache-aside L1 -> L2 -> обробник.
/// Стоїть ПІСЛЯ валідації (невалідний запит не потрапляє в кеш) і ДО обробника — попадання в кеш не звертається до MongoDB.
/// Команди інвалідують ключі явно, одразу після запису.
/// </summary>
public sealed class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly IFeedbackCache _cache;

    public CachingBehavior(IFeedbackCache cache) => _cache = cache;

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        => request is ICachedQuery query
            ? _cache.GetOrCreateAsync(query.CacheKey, _ => next(), query.Policy, ct)
            : next();                                             // команди й некешовані запити — без змін
}
