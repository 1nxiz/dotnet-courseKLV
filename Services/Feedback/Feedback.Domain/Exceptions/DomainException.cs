namespace Feedback.Domain.Exceptions;

/// <summary>Порушення доменного правила (рейтинг поза 1..5, порожній текст) -> 400.</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
