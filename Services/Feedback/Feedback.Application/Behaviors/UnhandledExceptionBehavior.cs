using FluentValidation;
using Feedback.Application.Common.Exceptions;
using Feedback.Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Feedback.Application.Behaviors;

/// <summary>Централізоване логування НЕОЧІКУВАНИХ винятків на рівні MediatR (очікувані — валідація, 404, 409 — пропускаються).
/// Перетворення на HTTP-статуси — в обробнику WebApi.</summary>
public sealed class UnhandledExceptionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> _logger;

    public UnhandledExceptionBehavior(ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger) => _logger = logger;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        try
        {
            return await next();
        }
        catch (Exception ex) when (ex is not (ValidationException or NotFoundException or ConflictException
                                              or DomainException or OperationCanceledException))
        {
            _logger.LogError(ex, "Unhandled exception in {RequestName}", typeof(TRequest).Name);
            throw;
        }
    }
}
