using Feedback.Application.Common.Models;

namespace Feedback.Application.Common.Interfaces;

/// <summary>
/// Сторона читання (CQRS). Обробники запитів НЕ звертаються до MongoDB напряму — лише до цього інтерфейсу;
/// реалізація в Infrastructure використовує проєкції та aggregation pipeline ($match, $sort, $skip, $limit, $project).
/// </summary>
public interface IReviewQueries
{
    Task<ReviewDetailsDto?> GetByIdAsync(string id, CancellationToken ct);

    Task<PagedResult<ReviewDto>> GetListAsync(long? bookId, string? authorUserId, int? minRating, string? search,
        int page, int pageSize, CancellationToken ct);
}

public interface IDiscussionQueries
{
    Task<DiscussionDto?> GetByIdAsync(string id, CancellationToken ct);

    /// <summary>Курсорна пагінація повідомлень: після afterId, у хронологічному порядку.</summary>
    Task<CursorPage<MessageDto>> GetMessagesAsync(string discussionId, string? afterId, int pageSize, CancellationToken ct);
}
