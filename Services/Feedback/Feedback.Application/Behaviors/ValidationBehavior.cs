using FluentValidation;
using MediatR;

namespace Feedback.Application.Behaviors;

/// <summary>
/// 2-й behavior: виконує ВСІ валідатори FluentValidation для команди/запиту, збирає помилки разом
/// і кидає ValidationException — обробник при цьому не викликається.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!_validators.Any()) return await next();

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, ct)));
        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);           // обробник не викличеться
        return await next();
    }
}
