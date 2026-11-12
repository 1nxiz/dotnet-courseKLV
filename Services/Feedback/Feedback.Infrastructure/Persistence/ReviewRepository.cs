using Feedback.Application.Common.Exceptions;
using Feedback.Domain.Entities;
using Feedback.Domain.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Feedback.Infrastructure.Persistence;

/// <summary>Репозиторій відгуків на MongoDB Driver з оптимістичною конкурентністю за полем version.</summary>
public sealed class ReviewRepository : IReviewRepository
{
    private readonly IMongoCollection<Review> _reviews;

    public ReviewRepository(MongoDbContext context) => _reviews = context.Reviews;

    public async Task<Review?> GetByIdAsync(ObjectId id, CancellationToken ct = default)
        => await _reviews.Find(r => r.Id == id).FirstOrDefaultAsync(ct);

    public Task<bool> ExistsAsync(long bookId, string authorUserId, CancellationToken ct = default)
        => _reviews.Find(r => r.BookId == bookId && r.Author.UserId == authorUserId).AnyAsync(ct);

    public Task AddAsync(Review review, CancellationToken ct = default)
        => _reviews.InsertOneAsync(review, cancellationToken: ct);

    public async Task UpdateAsync(Review review, CancellationToken ct = default)
    {
        var expectedVersion = review.Version;
        review.BumpVersion();

        // оновлення застосовується лише до документа з ОЧІКУВАНОЮ версією
        var result = await _reviews.ReplaceOneAsync(
            r => r.Id == review.Id && r.Version == expectedVersion,
            review, cancellationToken: ct);

        if (result.MatchedCount == 0)                       // хтось змінив (або видалив) документ раніше
            throw new ConflictException($"Review {review.Id} was modified by another request (expected version {expectedVersion})");
    }

    public async Task<bool> DeleteAsync(ObjectId id, CancellationToken ct = default)
        => (await _reviews.DeleteOneAsync(r => r.Id == id, ct)).DeletedCount > 0;

    public async Task<long> RenameBookAsync(long bookId, string newTitle, CancellationToken ct = default)
    {
        var update = Builders<Review>.Update
            .Set(r => r.BookTitle, newTitle)
            .Set(r => r.UpdatedAt, DateTime.UtcNow)
            .Inc(r => r.Version, 1);
        var result = await _reviews.UpdateManyAsync(r => r.BookId == bookId, update, cancellationToken: ct);
        return result.ModifiedCount;
    }
}

public sealed class DiscussionRepository : IDiscussionRepository
{
    private readonly MongoDbContext _context;

    public DiscussionRepository(MongoDbContext context) => _context = context;

    public async Task<Discussion?> GetByIdAsync(ObjectId id, CancellationToken ct = default)
        => await _context.Discussions.Find(d => d.Id == id).FirstOrDefaultAsync(ct);

    public Task AddAsync(Discussion discussion, CancellationToken ct = default)
        => _context.Discussions.InsertOneAsync(discussion, cancellationToken: ct);

    public async Task UpdateAsync(Discussion discussion, CancellationToken ct = default)
    {
        var expectedVersion = discussion.Version;
        discussion.BumpVersion();
        var result = await _context.Discussions.ReplaceOneAsync(
            d => d.Id == discussion.Id && d.Version == expectedVersion, discussion, cancellationToken: ct);
        if (result.MatchedCount == 0)
            throw new ConflictException($"Discussion {discussion.Id} was modified by another request");
    }

    public Task AddMessageAsync(Message message, CancellationToken ct = default)
        => _context.Messages.InsertOneAsync(message, cancellationToken: ct);

    public async Task<Message?> GetMessageAsync(ObjectId id, CancellationToken ct = default)
        => await _context.Messages.Find(m => m.Id == id).FirstOrDefaultAsync(ct);
}
