using Feedback.Domain.Common;
using Feedback.Domain.Exceptions;
using Feedback.Domain.ValueObjects;
using MongoDB.Bson;

namespace Feedback.Domain.Entities;

/// <summary>
/// Обговорення книги — ГІБРИДНА модель (subset pattern): заголовок + 3 останні повідомлення вбудовані,
/// а всі повідомлення зберігаються окремою колекцією messages (посилання discussionId).
/// </summary>
public class Discussion : BaseEntity
{
    public const int LastMessagesLimit = 3;

    private List<string> _tags = new();
    private List<MessagePreview> _lastMessages = new();

    public long BookId { get; private set; }
    public string BookTitle { get; private set; } = "";
    public string Title { get; private set; } = "";
    public Author StartedBy { get; private set; } = null!;
    public IReadOnlyCollection<string> Tags => _tags.AsReadOnly();
    public int MessagesCount { get; private set; }
    public IReadOnlyCollection<MessagePreview> LastMessages => _lastMessages.AsReadOnly();
    public DateTime LastActivityAt { get; private set; }

    private Discussion() { }

    public Discussion(long bookId, string bookTitle, string title, Author startedBy, IEnumerable<string>? tags = null)
    {
        if (bookId <= 0) throw new DomainException("BookId must be positive");
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length < 3) throw new DomainException("Title must have at least 3 characters");
        BookId = bookId;
        BookTitle = bookTitle.Trim();
        Title = title.Trim();
        StartedBy = startedBy ?? throw new DomainException("Author is required");
        _tags.AddRange((tags ?? Enumerable.Empty<string>()).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct());
        LastActivityAt = CreatedAt;
    }

    /// <summary>Реєструє нове повідомлення: лічильник + вбудований «хвіст» з 3 останніх повідомлень.</summary>
    public void RegisterMessage(Message message)
    {
        if (message.DiscussionId != Id) throw new DomainException("Message belongs to another discussion");
        MessagesCount++;
        _lastMessages.Add(new MessagePreview(message.Id, message.Author, message.Text, message.CreatedAt));
        if (_lastMessages.Count > LastMessagesLimit)
            _lastMessages.RemoveRange(0, _lastMessages.Count - LastMessagesLimit);
        LastActivityAt = message.CreatedAt;
        Touch();
    }

    public void RenameBook(string newTitle)
    {
        BookTitle = newTitle.Trim();
        Touch();
    }
}

/// <summary>Короткий знімок повідомлення, вбудований в обговорення (lastMessages).</summary>
public sealed class MessagePreview
{
    public ObjectId MessageId { get; }
    public Author Author { get; }
    public string Text { get; }
    public DateTime CreatedAt { get; }

    public MessagePreview(ObjectId messageId, Author author, string text, DateTime createdAt)
    {
        MessageId = messageId;
        Author = author;
        Text = text;
        CreatedAt = createdAt;
    }
}

/// <summary>Повідомлення обговорення — окрема колекція (необмежена кількість, REFERENCE на discussions).</summary>
public class Message : BaseEntity
{
    public ObjectId DiscussionId { get; private set; }
    public Author Author { get; private set; } = null!;
    public string Text { get; private set; } = "";
    public ObjectId? ReplyToId { get; private set; }
    public DateTime? EditedAt { get; private set; }

    private Message() { }

    public Message(ObjectId discussionId, Author author, string text, ObjectId? replyToId = null)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new DomainException("Message text is required");
        if (text.Length > 4000) throw new DomainException("Message is too long");
        DiscussionId = discussionId;
        Author = author ?? throw new DomainException("Author is required");
        Text = text.Trim();
        ReplyToId = replyToId;
    }
}
