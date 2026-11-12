namespace Loans.Domain.Exceptions;

/// <summary>Базовий доменний виняток. DAL перетворює помилки SQL Server на його нащадків, API — на HTTP-статуси.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Ресурс не знайдено (THROW 50001) -> 404.</summary>
public sealed class NotFoundException : DomainException
{
    public NotFoundException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Порушення бізнес-правила: недопустимий перехід статусу, нестача примірників, дублікат (THROW 50002, 2601/2627) -> 409.</summary>
public sealed class BusinessConflictException : DomainException
{
    public BusinessConflictException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Запис змінено іншим користувачем — не збігся RowVersion (THROW 50003) -> 409.</summary>
public sealed class ConcurrencyException : DomainException
{
    public ConcurrencyException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Некоректні вхідні дані, які виявила бізнес-логіка або БД (THROW 50004, CHECK 547) -> 400.</summary>
public sealed class ValidationException : DomainException
{
    public ValidationException(string message, IDictionary<string, string[]>? errors = null, Exception? inner = null)
        : base(message, inner)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public IDictionary<string, string[]> Errors { get; }
}
