using AutoMapper;
using Feedback.Application.Common.Interfaces;
using Feedback.Application.Common.Models;
using Feedback.Domain.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Feedback.Infrastructure.Persistence;

/// <summary>
/// Сторона читання: aggregation pipeline ($match -> $sort -> $skip -> $limit -> $project) повертає з БД лише поля
/// ReviewDto (commentsCount рахує $size у БД). Фільтрація й пагінація — засобами MongoDB, а не в пам'яті.
/// </summary>
public sealed class ReviewQueries : IReviewQueries
{
    private readonly MongoDbContext _context;
    private readonly IMapper _mapper;

    public ReviewQueries(MongoDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<ReviewDetailsDto?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out var objectId)) return null;
        var review = await _context.Reviews.Find(r => r.Id == objectId).FirstOrDefaultAsync(ct);
        return review is null ? null : _mapper.Map<ReviewDetailsDto>(review);
    }

    public async Task<PagedResult<ReviewDto>> GetListAsync(long? bookId, string? authorUserId, int? minRating, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        var collection = _context.Database.GetCollection<BsonDocument>(MongoDbContext.ReviewsCollection);

        var match = new BsonDocument();
        if (!string.IsNullOrWhiteSpace(search)) match["$text"] = new BsonDocument("$search", search);   // text-індекс
        if (bookId is not null) match["bookId"] = bookId.Value;
        if (!string.IsNullOrWhiteSpace(authorUserId)) match["author.userId"] = authorUserId;
        if (minRating is not null) match["rating"] = new BsonDocument("$gte", minRating.Value);

        var total = await collection.CountDocumentsAsync(match, cancellationToken: ct);

        var documents = await collection.Aggregate()
            .Match(match)
            .Sort(new BsonDocument { { "createdAt", -1 }, { "_id", -1 } })        // підкріплено індексом {bookId, createdAt}
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .Project(new BsonDocument
            {
                { "bookId", 1 },
                { "bookTitle", 1 },
                { "authorName", "$author.displayName" },
                { "rating", 1 },
                { "text", 1 },
                { "commentsCount", new BsonDocument("$size", new BsonDocument("$ifNull", new BsonArray { "$comments", new BsonArray() })) },
                { "version", new BsonDocument("$ifNull", new BsonArray { "$version", 0 }) },
                { "createdAt", 1 }
            })
            .ToListAsync(ct);

        var items = documents.Select(d => new ReviewDto(
            d["_id"].AsObjectId.ToString(),
            d["bookId"].ToInt64(),
            d["bookTitle"].AsString,
            d["authorName"].AsString,
            d["rating"].ToInt32(),
            d["text"].AsString,
            d["commentsCount"].ToInt32(),
            d["version"].ToInt32(),
            d["createdAt"].ToUniversalTime())).ToList();

        return new PagedResult<ReviewDto>(items, total, page, pageSize);
    }

    /// <summary>Кількість і середня оцінка — $group у БД; останні відгуки — та сама проєкція, що й у списку.</summary>
    public async Task<BookReviewsSummaryDto> GetBookSummaryAsync(long bookId, int latest, CancellationToken ct)
    {
        var collection = _context.Database.GetCollection<BsonDocument>(MongoDbContext.ReviewsCollection);
        var stats = await collection.Aggregate()
            .Match(new BsonDocument("bookId", bookId))
            .Group(new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "count", new BsonDocument("$sum", 1) },
                { "avg", new BsonDocument("$avg", "$rating") }
            })
            .FirstOrDefaultAsync(ct);

        var latestPage = await GetListAsync(bookId, null, null, null, 1, latest, ct);
        return new BookReviewsSummaryDto(bookId,
            stats is null ? 0 : stats["count"].ToInt64(),
            stats is null ? 0 : Math.Round(stats["avg"].ToDouble(), 2),
            latestPage.Items);
    }
}

public sealed class DiscussionQueries : IDiscussionQueries
{
    private readonly MongoDbContext _context;
    private readonly IMapper _mapper;

    public DiscussionQueries(MongoDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<DiscussionDto?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out var objectId)) return null;
        var discussion = await _context.Discussions.Find(d => d.Id == objectId).FirstOrDefaultAsync(ct);
        return discussion is null ? null : _mapper.Map<DiscussionDto>(discussion);
    }

    /// <summary>Курсорна пагінація: наступна сторінка починається ПІСЛЯ останнього отриманого _id (а не через skip).</summary>
    public async Task<CursorPage<MessageDto>> GetMessagesAsync(string discussionId, string? afterId, int pageSize, CancellationToken ct)
    {
        var filter = Builders<Message>.Filter.Eq(m => m.DiscussionId, ObjectId.Parse(discussionId));
        if (afterId is not null)
            filter &= Builders<Message>.Filter.Gt(m => m.Id, ObjectId.Parse(afterId));

        var messages = await _context.Messages.Find(filter)
            .SortBy(m => m.Id)                                  // ObjectId зростає разом із часом створення
            .Limit(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = messages.Count > pageSize;
        var pageItems = messages.Take(pageSize).ToList();
        return new CursorPage<MessageDto>(_mapper.Map<List<MessageDto>>(pageItems),
            hasMore ? pageItems[^1].Id.ToString() : null);
    }
}
