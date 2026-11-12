using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Feedback.WebApi.Controllers;

/// <summary>Базовий контролер: доступ до ISender (MediatR). Контролери лише передають команди й запити.</summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _mediator;

    protected ISender Mediator => _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();
}
