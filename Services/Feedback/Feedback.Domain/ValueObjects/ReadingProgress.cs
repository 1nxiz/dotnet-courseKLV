using Feedback.Domain.Common;
using Feedback.Domain.Exceptions;

namespace Feedback.Domain.ValueObjects;

/// <summary>Прогрес читання, яким автор може супроводити відгук (поле є лише в частини документів).</summary>
public sealed class ReadingProgress : ValueObject
{
    public int PagesRead { get; }
    public bool Finished { get; }

    public ReadingProgress(int pagesRead, bool finished)
    {
        if (pagesRead < 0) throw new DomainException("PagesRead must not be negative");
        PagesRead = pagesRead;
        Finished = finished;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return PagesRead;
        yield return Finished;
    }
}
