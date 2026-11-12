using Feedback.Application.Caching;
using Feedback.Application.Common.Interfaces;
using Feedback.Application.Common.Messaging;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Feedback.Application.Commands.SyncBookTitle;

/// <summary>
/// Реплікація назви книги з Каталогу (подія BookRenamed, Лаб. 08): оновити bookTitle у всіх відгуках, обговореннях
/// і рейтингах цієї книги. Ідемпотентна за EventId. Повертає кількість змінених відгуків (0 — дублікат або нічого змінювати).
/// </summary>
public record SyncBookTitleCommand(Guid EventId, long BookId, string NewTitle, DateTime OccurredAt) : ICommand<int>;

public sealed class SyncBookTitleCommandValidator : AbstractValidator<SyncBookTitleCommand>
{
    public SyncBookTitleCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.BookId).GreaterThan(0);
        RuleFor(x => x.NewTitle).NotEmpty().MaximumLength(300);
    }
}

public sealed class SyncBookTitleCommandHandler : IRequestHandler<SyncBookTitleCommand, int>
{
    private readonly IProcessedEventStore _processed;
    private readonly IBookCopiesWriter _writer;
    private readonly IFeedbackCache _cache;
    private readonly ILogger<SyncBookTitleCommandHandler> _logger;

    public SyncBookTitleCommandHandler(IProcessedEventStore processed, IBookCopiesWriter writer, IFeedbackCache cache,
        ILogger<SyncBookTitleCommandHandler> logger)
    {
        _processed = processed;
        _writer = writer;
        _cache = cache;
        _logger = logger;
    }

    public async Task<int> Handle(SyncBookTitleCommand cmd, CancellationToken ct)
    {
        if (!await _processed.TryMarkProcessedAsync(cmd.EventId, "BookRenamedEvent", ct))
        {
            _logger.LogInformation("Duplicate event {EventId} for book {BookId} skipped", cmd.EventId, cmd.BookId);
            return 0;
        }

        BookTitleSyncResult result;
        try
        {
            result = await _writer.UpdateBookTitleAsync(cmd.BookId, cmd.NewTitle, cmd.OccurredAt, ct);
        }
        catch
        {
            await _processed.UnmarkAsync(cmd.EventId, CancellationToken.None);   // щоб повтор знову обробив подію
            throw;
        }

        // кеш: змінені відгуки й підсумок книги містять bookTitle
        var keys = result.ReviewIds.Select(CacheKeys.Review).Append(CacheKeys.BookSummary(cmd.BookId)).ToArray();
        await _cache.InvalidateAsync("BookRenamed", ct, keys);

        _logger.LogInformation("Book {BookId} renamed to {Title}: {Reviews} reviews, {Discussions} discussions, {Ratings} ratings updated",
            cmd.BookId, cmd.NewTitle, result.ReviewIds.Count, result.DiscussionsUpdated, result.RatingsUpdated);
        return result.ReviewIds.Count;
    }
}
