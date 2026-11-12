namespace Feedback.Application.Common.Models;

public record PagedResult<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

/// <summary>Сторінка з курсорною пагінацією: наступна починається після NextCursor (Id останнього елемента).</summary>
public record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

public record AuthorDto(string UserId, string DisplayName);

public record CommentDto(AuthorDto Author, string Text, DateTime CreatedAt);

public record ReadingProgressDto(int PagesRead, bool Finished);

/// <summary>Елемент списку відгуків (проєкція з БД — лише потрібні поля).</summary>
public record ReviewDto(string Id, long BookId, string BookTitle, string AuthorName, int Rating, string Text,
    int CommentsCount, int Version, DateTime CreatedAt);

/// <summary>Повний відгук з вкладеними коментарями.</summary>
public record ReviewDetailsDto(string Id, long BookId, string BookTitle, AuthorDto Author, int Rating, string Text,
    IReadOnlyList<string>? Photos, ReadingProgressDto? ReadingProgress, string? DiscussionId,
    IReadOnlyList<CommentDto> Comments, int Version, DateTime CreatedAt, DateTime? UpdatedAt);

public record MessagePreviewDto(string MessageId, AuthorDto Author, string Text, DateTime CreatedAt);

public record MessageDto(string Id, string DiscussionId, AuthorDto Author, string Text, string? ReplyToId, DateTime CreatedAt);

public record DiscussionDto(string Id, long BookId, string BookTitle, string Title, AuthorDto StartedBy,
    IReadOnlyList<string> Tags, int MessagesCount, IReadOnlyList<MessagePreviewDto> LastMessages, DateTime LastActivityAt);

public record DiscussionWithMessagesDto(DiscussionDto Discussion, CursorPage<MessageDto> Messages);
