using Feedback.Application.Commands.SyncBookTitle;
using Feedback.WebApi.IntegrationEvents.Events;
using MediatR;
using Platform.EventBus;

namespace Feedback.WebApi.IntegrationEvents.Handlers;

/// <summary>
/// Адаптер подій шару Presentation: перетворює BookRenamedEvent на команду SyncBookTitleCommand і надсилає її
/// через MediatR — команда проходить той самий pipeline (логування, валідація, метрики), що й HTTP-запити.
/// Ідемпотентність (processedEvents) і інвалідація кешу — в обробнику команди (Application).
/// </summary>
public sealed class BookRenamedHandler : IIntegrationEventHandler<BookRenamedEvent>
{
    private readonly ISender _mediator;
    private readonly ILogger<BookRenamedHandler> _logger;

    public BookRenamedHandler(ISender mediator, ILogger<BookRenamedHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task HandleAsync(BookRenamedEvent e, CancellationToken ct)
    {
        try
        {
            var updated = await _mediator.Send(new SyncBookTitleCommand(e.EventId, e.BookId, e.NewTitle, e.OccurredAt), ct);
            _logger.LogInformation("BookRenamed {EventId}: book {BookId} '{OldTitle}' -> '{NewTitle}', {ReviewsUpdated} reviews updated",
                e.EventId, e.BookId, e.OldTitle, e.NewTitle, updated);
        }
        catch (FluentValidation.ValidationException ex)
        {
            // некоректні дані події від повтору не стануть коректними -> одразу в DLQ
            throw new PermanentEventException($"Invalid BookRenamedEvent {e.EventId}: {ex.Message}", ex);
        }
    }
}
