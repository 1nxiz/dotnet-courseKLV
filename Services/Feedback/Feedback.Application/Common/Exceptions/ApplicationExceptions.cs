namespace Feedback.Application.Common.Exceptions;

/// <summary>Документ не знайдено -> 404.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string entity, object key) : base($"{entity} '{key}' was not found") { }
}

/// <summary>Конфлікт: документ змінено іншим запитом (Version / ETag) або порушено унікальність -> 409.</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
