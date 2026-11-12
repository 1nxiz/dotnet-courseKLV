using Feedback.Domain.Entities;
using MongoDB.Bson;

namespace Feedback.Domain.Interfaces;

/// <summary>Контракт доступу до відгуків (реалізація — Infrastructure, MongoDB Driver).</summary>
public interface IReviewRepository
{
    Task<Review?> GetByIdAsync(ObjectId id, CancellationToken ct = default);
    Task<bool> ExistsAsync(long bookId, string authorUserId, CancellationToken ct = default);
    Task AddAsync(Review review, CancellationToken ct = default);

    /// <summary>Оновлення з перевіркою Version (optimistic concurrency); чужа зміна -> ConflictException.</summary>
    Task UpdateAsync(Review review, CancellationToken ct = default);

    Task<bool> DeleteAsync(ObjectId id, CancellationToken ct = default);

    /// <summary>Оновлення денормалізованої назви книги в усіх відгуках (eventual consistency, Лаб. 08).</summary>
    Task<long> RenameBookAsync(long bookId, string newTitle, CancellationToken ct = default);
}

public interface IDiscussionRepository
{
    Task<Discussion?> GetByIdAsync(ObjectId id, CancellationToken ct = default);
    Task AddAsync(Discussion discussion, CancellationToken ct = default);
    Task UpdateAsync(Discussion discussion, CancellationToken ct = default);
    Task AddMessageAsync(Message message, CancellationToken ct = default);
    Task<Message?> GetMessageAsync(ObjectId id, CancellationToken ct = default);
}
