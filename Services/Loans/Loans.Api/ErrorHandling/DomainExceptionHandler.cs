using Loans.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Loans.Api.ErrorHandling;

/// <summary>
/// Єдиний глобальний обробник: перетворює доменні винятки на HTTP-статуси у форматі ProblemDetails (RFC 9457).
/// Завершальна ланка ланцюжка «процедура (THROW 5000x) -> DAL (SqlExceptionMapper) -> BLL -> HTTP-статус».
/// Кожен виняток логується тут один раз.
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<DomainExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public DomainExceptionHandler(IProblemDetailsService problemDetails, ILogger<DomainExceptionHandler> logger,
        IHostEnvironment environment)
    {
        _problemDetails = problemDetails;
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            BusinessConflictException => (StatusCodes.Status409Conflict, "Business rule violation"),
            ConcurrencyException => (StatusCodes.Status409Conflict, "Concurrency conflict"),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Bad request"),
            OperationCanceledException when http.RequestAborted.IsCancellationRequested => (499, "Client closed request"),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error")
        };

        if (status >= 500)
            _logger.LogError(ex, "Request {Method} {Path} failed", http.Request.Method, http.Request.Path);
        else
            _logger.LogWarning("Request {Method} {Path} -> {StatusCode}: {Error}",
                http.Request.Method, http.Request.Path, status, ex.Message);

        ProblemDetails problem = ex is ValidationException { Errors.Count: > 0 } validation
            ? new ValidationProblemDetails(validation.Errors)
            : new ProblemDetails();
        problem.Status = status;
        problem.Title = title;
        problem.Type = $"https://httpstatuses.io/{status}";
        // деталі 500-х помилок — лише в Development (у Production — безпечний короткий опис)
        problem.Detail = status == StatusCodes.Status500InternalServerError && !_environment.IsDevelopment()
            ? "An unexpected error occurred"
            : ex.Message;

        http.Response.StatusCode = status;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
            Exception = ex
        });
    }
}
