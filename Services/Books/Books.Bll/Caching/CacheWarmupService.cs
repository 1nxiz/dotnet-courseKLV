using Books.Bll.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Books.Bll.Caching;

/// <summary>
/// Прогрівання кешу: довідники (жанри, автори) завантажуються під час старту й оновлюються періодично,
/// тож перші запити після розгортання вже не йдуть у БД.
/// </summary>
public sealed class CacheWarmupService : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(9);     // трохи менше за L1 TTL довідників

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CacheWarmupService> _logger;

    public CacheWarmupService(IServiceScopeFactory scopes, ILogger<CacheWarmupService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);         // дати міграціям і сідінгу завершитися
        using var timer = new PeriodicTimer(Period);
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var genres = await scope.ServiceProvider.GetRequiredService<IGenreService>().GetAllAsync(stoppingToken);
                var authors = await scope.ServiceProvider.GetRequiredService<IAuthorService>().GetAllAsync(stoppingToken);
                _logger.LogInformation("Cache warm-up done: {GenresCount} genres, {AuthorsCount} authors", genres.Count, authors.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Cache warm-up failed, will retry in {Period}", Period);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
