using MongoDB.Driver.Core.Extensions.DiagnosticSources;
using Feedback.Application.Common.Interfaces;
using Feedback.Domain.Interfaces;
using Feedback.Infrastructure.Persistence;
using Feedback.Infrastructure.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Feedback.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        MongoMappings.Register();                                    // один раз, до першого звернення до БД

        services.Configure<MongoDbOptions>(configuration.GetSection(MongoDbOptions.SectionName));
        // рядок підключення від оркестратора (Aspire: ConnectionStrings:FeedbackDb) має пріоритет
        services.PostConfigure<MongoDbOptions>(options =>
        {
            var connectionString = configuration.GetConnectionString("FeedbackDb");
            if (string.IsNullOrWhiteSpace(connectionString)) return;
            options.ConnectionString = connectionString;
            var database = MongoUrl.Create(connectionString).DatabaseName;
            if (!string.IsNullOrWhiteSpace(database)) options.DatabaseName = database;
        });

        services.AddSingleton<IMongoClient>(sp =>
        {
            var settings = MongoClientSettings.FromConnectionString(sp.GetRequiredService<IOptions<MongoDbOptions>>().Value.ConnectionString);
            // Лаб. 05: кожна команда MongoDB стає span-ом (ActivitySource "MongoDB.Driver.Core.Extensions.DiagnosticSources")
            settings.ClusterConfigurator = cb => cb.Subscribe(new DiagnosticsActivityEventSubscriber(
                new InstrumentationOptions { CaptureCommandText = true }));
            return new MongoClient(settings);
        });
        services.AddSingleton<MongoDbContext>();

        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IDiscussionRepository, DiscussionRepository>();
        services.AddScoped<IReviewQueries, ReviewQueries>();
        services.AddScoped<IDiscussionQueries, DiscussionQueries>();
        services.AddScoped<IProcessedEventStore, ProcessedEventStore>();   // Лаб. 08
        services.AddScoped<IBookCopiesWriter, BookCopiesWriter>();

        services.AddScoped<IDataSeeder, ReviewSeeder>();
        services.AddScoped<IDataSeeder, DiscussionSeeder>();
        services.AddHostedService<IndexCreationService>();            // спершу індекси
        services.AddHostedService<DataSeedingService>();              // потім сідінг
        return services;
    }
}
