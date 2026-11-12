using Feedback.Application.Common.Exceptions;
using Feedback.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Feedback.WebApi.ErrorHandling;

/// <summary>
/// Глобальний обробник: доменні, валідаційні та MongoDB-специфічні винятки -> ProblemDetails з правильним статусом.
/// NotFound 404 · Validation 400 · Domain 400 · Conflict 409 · DuplicateKey 409 · інші помилки запису 400 ·
/// MongoConnection / timeout вибору сервера 503.
/// </summary>
public sealed class FeedbackExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<FeedbackExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public FeedbackExceptionHandler(IProblemDetailsService problemDetails, ILogger<FeedbackExceptionHandler> logger,
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
            FluentValidation.ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            DomainException => (StatusCodes.Status400BadRequest, "Domain rule violated"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey } => (StatusCodes.Status409Conflict, "Duplicate key"),
            MongoWriteException => (StatusCodes.Status400BadRequest, "Write rejected by database"),
            MongoConnectionException => (StatusCodes.Status503ServiceUnavailable, "Database unavailable"),
            TimeoutException => (StatusCodes.Status503ServiceUnavailable, "Database unavailable"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Bad request"),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error")
        };

        if (status >= 500)
            _logger.LogError(ex, "Request {Method} {Path} failed with {StatusCode}", http.Request.Method, http.Request.Path, status);
        else
            _logger.LogWarning("Request {Method} {Path} -> {StatusCode} {ExceptionType}: {Error}",
                http.Request.Method, http.Request.Path, status, ex.GetType().Name, ex.Message);

        ProblemDetails problem = ex is FluentValidation.ValidationException validation
            ? new ValidationProblemDetails(validation.Errors
                .GroupBy(e => string.IsNullOrEmpty(e.PropertyName) ? "request" : e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()))
            : new ProblemDetails();
        problem.Status = status;
        problem.Title = title;
        problem.Type = $"https://httpstatuses.io/{status}";
        problem.Detail = status == StatusCodes.Status500InternalServerError && !_environment.IsDevelopment()
            ? "An unexpected error occurred"
            : ex is FluentValidation.ValidationException ? "One or more validation errors occurred" : ex.Message;

        http.Response.StatusCode = status;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem, Exception = ex });
    }
}
