using Feedback.Domain.Common;
using Feedback.Domain.Exceptions;

namespace Feedback.Domain.ValueObjects;

/// <summary>Автор відгуку/коментаря — незмінний value object (вбудований документ author).</summary>
public sealed class Author : ValueObject
{
    public string UserId { get; }
    public string DisplayName { get; }

    public Author(string userId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new DomainException("UserId is required");
        if (string.IsNullOrWhiteSpace(displayName)) throw new DomainException("DisplayName is required");
        if (displayName.Length > 100) throw new DomainException("DisplayName is too long");
        UserId = userId.Trim();
        DisplayName = displayName.Trim();
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return UserId;
        yield return DisplayName;
    }

    public override string ToString() => DisplayName;
}
