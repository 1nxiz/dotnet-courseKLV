using Feedback.Domain.Entities;
using Feedback.Domain.ValueObjects;
using Feedback.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Feedback.Infrastructure.Seeding;

/// <summary>Сідер колекції. Ідемпотентний: якщо дані вже є — нічого не вставляє.</summary>
public interface IDataSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

public sealed class ReviewSeeder : IDataSeeder
{
    private readonly MongoDbContext _context;
    private readonly ILogger<ReviewSeeder> _logger;

    public ReviewSeeder(MongoDbContext context, ILogger<ReviewSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await _context.Reviews.EstimatedDocumentCountAsync(cancellationToken: ct) > 0)
        {
            _logger.LogInformation("reviews already contain data — seeding skipped");
            return;                                                     // уже наповнено (наприклад, db/p3/seed.js)
        }

        var olena = new Author("u-olena", "Олена");
        var andrii = new Author("u-andrii", "Андрій");
        var reviews = new[]
        {
            new Review(4, "Clean Architecture", olena, new Rating(5), "Чудово пояснено правило залежностей і межі шарів.",
                photos: ["https://cdn.library.example/reviews/r1-1.jpg"]),
            new Review(4, "Clean Architecture", andrii, new Rating(4), "Корисно, але подекуди забагато теорії.",
                readingProgress: new ReadingProgress(432, true)),
            new Review(6, "Designing Data-Intensive Applications", olena, 5, "Найкраща книга про реплікацію та узгодженість."),
            new Review(1, "Кобзар", andrii, 5, "Перечитую щороку.")
        };
        reviews[0].AddComment(andrii, "Згоден, особливо розділ про компоненти.");

        await _context.Reviews.InsertManyAsync(reviews, cancellationToken: ct);     // bulk-вставка
        _logger.LogInformation("reviews seeded: {Count} documents", reviews.Length);
    }
}

public sealed class DiscussionSeeder : IDataSeeder
{
    private readonly MongoDbContext _context;
    private readonly ILogger<DiscussionSeeder> _logger;

    public DiscussionSeeder(MongoDbContext context, ILogger<DiscussionSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await _context.Discussions.EstimatedDocumentCountAsync(cancellationToken: ct) > 0)
        {
            _logger.LogInformation("discussions already contain data — seeding skipped");
            return;
        }

        var discussion = new Discussion(4, "Clean Architecture", "Чи потрібен окремий шар Application?",
            new Author("u-andrii", "Андрій"), ["architecture", "reading-club"]);
        var messages = new[]
        {
            new Message(discussion.Id, new Author("u-andrii", "Андрій"), "Читаю розділ про межі."),
            new Message(discussion.Id, new Author("u-olena", "Олена"), "Для домену з правилами — так.")
        };
        foreach (var message in messages) discussion.RegisterMessage(message);

        await _context.Discussions.InsertOneAsync(discussion, cancellationToken: ct);
        await _context.Messages.InsertManyAsync(messages, cancellationToken: ct);
        _logger.LogInformation("discussions seeded: 1 discussion, {Count} messages", messages.Length);
    }
}

/// <summary>Запускає всі сідери під час старту (після створення індексів).</summary>
public sealed class DataSeedingService : IHostedService
{
    private readonly IServiceProvider _services;

    public DataSeedingService(IServiceProvider services) => _services = services;

    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        foreach (var seeder in scope.ServiceProvider.GetServices<IDataSeeder>())
            await seeder.SeedAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Автоматичне створення індексів під час старту: складені, text і unique. Імена й параметри збігаються
/// з db/p3/collections.js, тож повторне створення безпечне (MongoDB не дублює наявний індекс).
/// </summary>
public sealed class IndexCreationService : IHostedService
{
    private readonly MongoDbContext _context;
    private readonly ILogger<IndexCreationService> _logger;

    public IndexCreationService(MongoDbContext context, ILogger<IndexCreationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var reviewKeys = Builders<Review>.IndexKeys;
        await CreateAsync(_context.Reviews, ct,
            new CreateIndexModel<Review>(reviewKeys.Ascending(r => r.BookId).Descending(r => r.CreatedAt),
                new CreateIndexOptions { Name = "ix_reviews_bookId_createdAt" }),
            new CreateIndexModel<Review>(reviewKeys.Ascending(r => r.BookId).Ascending("author.userId"),
                new CreateIndexOptions { Name = "ux_reviews_bookId_authorUserId", Unique = true }),   // один відгук на книгу від автора
            new CreateIndexModel<Review>(reviewKeys.Text(r => r.Text),
                new CreateIndexOptions { Name = "ix_reviews_text", DefaultLanguage = "none" }),         // повнотекстовий пошук
            new CreateIndexModel<Review>(reviewKeys.Ascending("author.userId").Descending(r => r.CreatedAt),
                new CreateIndexOptions { Name = "ix_reviews_authorUserId_createdAt" }));

        var discussionKeys = Builders<Discussion>.IndexKeys;
        await CreateAsync(_context.Discussions, ct,
            new CreateIndexModel<Discussion>(discussionKeys.Ascending(d => d.BookId).Descending(d => d.LastActivityAt),
                new CreateIndexOptions { Name = "ix_discussions_bookId_lastActivityAt" }),
            new CreateIndexModel<Discussion>(discussionKeys.Ascending("tags"),
                new CreateIndexOptions { Name = "ix_discussions_tags" }));

        var messageKeys = Builders<Message>.IndexKeys;
        await CreateAsync(_context.Messages, ct,
            new CreateIndexModel<Message>(messageKeys.Ascending(m => m.DiscussionId).Ascending(m => m.CreatedAt),
                new CreateIndexOptions { Name = "ix_messages_discussionId_createdAt" }),
            new CreateIndexModel<Message>(messageKeys.Ascending(m => m.ReplyToId),
                new CreateIndexOptions<Message>
                {
                    Name = "ix_messages_replyToId",
                    PartialFilterExpression = new BsonDocument("replyToId", new BsonDocument("$type", "objectId"))
                }));
    }

    private async Task CreateAsync<T>(IMongoCollection<T> collection, CancellationToken ct, params CreateIndexModel<T>[] models)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var names = await collection.Indexes.CreateManyAsync(models, ct);
                _logger.LogInformation("Indexes ensured on {Collection}: {Indexes}",
                    collection.CollectionNamespace.CollectionName, string.Join(", ", names));
                return;
            }
            catch (MongoCommandException ex) when (ex.CodeName is "IndexOptionsConflict" or "IndexKeySpecsConflict")
            {
                _logger.LogWarning(ex, "Index definition differs from the existing one on {Collection}",
                    collection.CollectionNamespace.CollectionName);
                return;
            }
            catch (Exception ex) when (attempt < 10 && ex is MongoConnectionException or TimeoutException)
            {
                _logger.LogWarning("MongoDB is not ready (attempt {Attempt}), retrying in 3 s", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
