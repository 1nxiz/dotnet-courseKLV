using Books.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Books.Api.ErrorHandling;

/// <summary>
/// Централізована обробка помилок: кожен невиловлений виняток -> стандартизований ProblemDetails
/// з коректним статусом; деталі 500-х — лише в Development. Виняток логується ОДИН раз — тут.
/// </summary>
public sealed class ExceptionToProblemDetailsHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<ExceptionToProblemDetailsHandler> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionToProblemDetailsHandler(IProblemDetailsService problemDetails,
        ILogger<ExceptionToProblemDetailsHandler> logger, IHostEnvironment environment)
    {
        _problemDetails = problemDetails;
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            EntityNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            FluentValidation.ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            DomainException => (StatusCodes.Status409Conflict, "Business rule violation"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrency conflict"),
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } => (StatusCodes.Status409Conflict, "Duplicate key"),
            DbUpdateException { InnerException: SqlException { Number: 547 } } => (StatusCodes.Status400BadRequest, "Constraint violation"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Bad request"),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error")
        };

        if (status >= 500)
            _logger.LogError(ex, "Request {Method} {Path} failed", http.Request.Method, http.Request.Path);
        else
            _logger.LogWarning("Request {Method} {Path} -> {StatusCode} {ExceptionType}: {Error}",
                http.Request.Method, http.Request.Path, status, ex.GetType().Name, ex.Message);

        ProblemDetails problem = ex switch
        {
            ValidationException { Errors.Count: > 0 } v => new ValidationProblemDetails(v.Errors),
            FluentValidation.ValidationException fv => new ValidationProblemDetails(
                fv.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),
            _ => new ProblemDetails()
        };
        problem.Status = status;
        problem.Title = title;
        problem.Type = $"https://httpstatuses.io/{status}";
        problem.Detail = status >= 500 && !_environment.IsDevelopment()
            ? "An unexpected error occurred"
            : ex is DbUpdateException && status < 500 ? title : ex.Message;

        http.Response.StatusCode = status;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
            Exception = ex
        });
    }
}
