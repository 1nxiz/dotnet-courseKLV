namespace Books.Domain.Exceptions;

/// <summary>Порушення інваріанта сутності (ціна &lt;= 0, нестача примірників) -> 409.</summary>
public class DomainException : Exception
{
    public DomainException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Сутність не знайдено -> 404.</summary>
public sealed class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message) { }
}

/// <summary>Конфлікт із поточним станом даних (дублікат ISBN, конфлікт версій) -> 409.</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Некоректні вхідні дані (бізнес-валідація) -> 400.</summary>
public sealed class ValidationException : Exception
{
    public ValidationException(string message, IDictionary<string, string[]>? errors = null) : base(message)
        => Errors = errors ?? new Dictionary<string, string[]>();

    public IDictionary<string, string[]> Errors { get; }
}
