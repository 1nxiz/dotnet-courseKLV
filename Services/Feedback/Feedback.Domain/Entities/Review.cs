using Feedback.Domain.Common;
using Feedback.Domain.Exceptions;
using Feedback.Domain.ValueObjects;
using MongoDB.Bson;

namespace Feedback.Domain.Entities;

/// <summary>
/// Відгук на книгу — агрегат документа reviews з Лаб. 01: вбудований автор (VO), вбудований масив коментарів,
/// посилання на книгу (bookId, без FK) і на обговорення (discussionId), денормалізована назва книги.
/// Стан змінюється лише методами; приватні сетери.
/// </summary>
public class Review : BaseEntity
{
    public const int MaxComments = 100;
    public const int MaxTextLength = 2000;

    private List<Comment> _comments = new();                              // вбудований масив (не readonly — заповнює BSON-десеріалізатор)
    private Dictionary<string, object>? _extraElements;                   // гнучка схема: поля, яких немає в моделі

    public long BookId { get; private set; }                              // посилання на Каталог
    public string BookTitle { get; private set; } = "";                   // денормалізовано для читання
    public Author Author { get; private set; } = null!;                   // value object
    public Rating Rating { get; private set; } = null!;                   // value object
    public string Text { get; private set; } = "";
    public string[]? Photos { get; private set; }            // є лише в частини документів
    public ReadingProgress? ReadingProgress { get; private set; }         // є лише в частини документів
    public ObjectId? DiscussionId { get; private set; }                   // reference на discussions
    public IReadOnlyCollection<Comment> Comments => _comments.AsReadOnly();

    /// <summary>Додаткові поля документа, яких немає в моделі (наприклад quote) — зберігаються при оновленні.</summary>
    public IReadOnlyDictionary<string, object>? ExtraElements => _extraElements;

    private Review() { }                                                   // для десеріалізації

    public Review(long bookId, string bookTitle, Author author, Rating rating, string text,
        IEnumerable<string>? photos = null, ReadingProgress? readingProgress = null)
    {
        if (bookId <= 0) throw new DomainException("BookId must be positive");
        if (string.IsNullOrWhiteSpace(bookTitle)) throw new DomainException("BookTitle is required");
        BookId = bookId;
        BookTitle = bookTitle.Trim();
        Author = author ?? throw new DomainException("Author is required");
        Rating = rating ?? throw new DomainException("Rating is required");
        SetText(text);
        var photoList = photos?.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        Photos = photoList is { Length: > 0 } ? photoList : null;
        ReadingProgress = readingProgress;
    }

    /// <summary>Конструктор з рейтингом-числом: валідація діапазону — у value object.</summary>
    public Review(long bookId, string bookTitle, Author author, int rating, string text)
        : this(bookId, bookTitle, author, new Rating(rating), text) { }

    public void Edit(Rating rating, string text, ReadingProgress? readingProgress)
    {
        Rating = rating;
        SetText(text);
        ReadingProgress = readingProgress;
        Touch();
    }

    public Comment AddComment(Author author, string text)
    {
        if (_comments.Count >= MaxComments)
            throw new DomainException($"A review can have at most {MaxComments} comments; continue in a discussion");
        var comment = new Comment(author, text, DateTime.UtcNow);       // вкладений документ
        _comments.Add(comment);
        Touch();
        return comment;
    }

    public void LinkDiscussion(ObjectId discussionId)
    {
        DiscussionId = discussionId;
        Touch();
    }

    /// <summary>Оновлення денормалізованої назви книги (подія BookRenamed з Каталогу, Лаб. 08).</summary>
    public void RenameBook(string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle)) throw new DomainException("BookTitle is required");
        BookTitle = newTitle.Trim();
        Touch();
    }

    private void SetText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new DomainException("Review text is required");
        if (text.Length > MaxTextLength) throw new DomainException($"Review text must be at most {MaxTextLength} characters");
        Text = text.Trim();
    }
}

/// <summary>Коментар до відгуку — вкладений документ масиву comments.</summary>
public sealed class Comment
{
    public Author Author { get; }
    public string Text { get; }
    public DateTime CreatedAt { get; }

    public Comment(Author author, string text, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new DomainException("Comment text is required");
        if (text.Length > 1000) throw new DomainException("Comment text must be at most 1000 characters");
        Author = author ?? throw new DomainException("Comment author is required");
        Text = text.Trim();
        CreatedAt = createdAt;
    }
}
