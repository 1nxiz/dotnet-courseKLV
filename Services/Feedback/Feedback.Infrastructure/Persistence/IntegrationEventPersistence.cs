using Feedback.Application.Common.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Feedback.Infrastructure.Persistence;

/// <summary>Колекція processedEvents: _id = EventId; дублікат ключа (код 11000) = подію вже оброблено.</summary>
public sealed class ProcessedEventStore : IProcessedEventStore
{
    private readonly IMongoCollection<BsonDocument> _collection;

    public ProcessedEventStore(MongoDbContext context)
        => _collection = context.Database.GetCollection<BsonDocument>(MongoDbContext.ProcessedEventsCollection);

    public async Task<bool> TryMarkProcessedAsync(Guid eventId, string eventType, CancellationToken ct)
    {
        try
        {
            await _collection.InsertOneAsync(new BsonDocument
            {
                { "_id", eventId.ToString() },
                { "eventType", eventType },
                { "processedAt", DateTime.UtcNow }
            }, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public Task UnmarkAsync(Guid eventId, CancellationToken ct)
        => _collection.DeleteOneAsync(new BsonDocument("_id", eventId.ToString()), ct);
}

/// <summary>
/// Денормалізовані копії назви книги (reviews / discussions / bookRatings). Оновлення — updateMany за bookId з умовою
/// «копію ще не оновлювала новіша подія» (поле bookTitleUpdatedAt): пізня старіша подія нічого не перезапише.
/// </summary>
public sealed class BookCopiesWriter : IBookCopiesWriter
{
    private readonly MongoDbContext _context;

    public BookCopiesWriter(MongoDbContext context) => _context = context;

    public async Task<BookTitleSyncResult> UpdateBookTitleAsync(long bookId, string title, DateTime occurredAt, CancellationToken ct)
    {
        var filter = new BsonDocument
        {
            { "bookId", bookId },
            { "$or", new BsonArray
                {
                    new BsonDocument("bookTitleUpdatedAt", new BsonDocument("$exists", false)),
                    new BsonDocument("bookTitleUpdatedAt", new BsonDocument("$lt", occurredAt))
                }
            }
        };
        var update = new BsonDocument("$set", new BsonDocument { { "bookTitle", title }, { "bookTitleUpdatedAt", occurredAt } });

        var reviews = _context.Database.GetCollection<BsonDocument>(MongoDbContext.ReviewsCollection);
        var affectedIds = await reviews.Find(filter).Project(new BsonDocument("_id", 1)).ToListAsync(ct);
        await reviews.UpdateManyAsync(filter, update, cancellationToken: ct);

        var discussions = await _context.Database.GetCollection<BsonDocument>(MongoDbContext.DiscussionsCollection)
            .UpdateManyAsync(filter, update, cancellationToken: ct);

        // bookRatings: _id = bookId (computed pattern)
        var ratingsFilter = new BsonDocument(filter) { ["_id"] = bookId };
        ratingsFilter.Remove("bookId");
        var ratings = await _context.Database.GetCollection<BsonDocument>(MongoDbContext.BookRatingsCollection)
            .UpdateManyAsync(ratingsFilter, update, cancellationToken: ct);

        return new BookTitleSyncResult(affectedIds.Select(d => d["_id"].ToString()!).ToList(),
            discussions.ModifiedCount, ratings.ModifiedCount);
    }
}
