using Feedback.Application.Common.Messaging;
using Feedback.Domain.Entities;
using Feedback.Domain.Interfaces;
using Feedback.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Feedback.Application.Commands.CreateReview;

/// <summary>Тіло POST /api/reviews (API-контракт). Мапиться в команду AutoMapper-ом.</summary>
public record CreateReviewRequest(long BookId, string BookTitle, string AuthorId, string AuthorName, int Rating, string Text,
    string[]? Photos = null, int? PagesRead = null, bool? Finished = null);

/// <summary>Створити відгук. Повертає Id нового документа (рядковий ObjectId).</summary>
public record CreateReviewCommand(long BookId, string BookTitle, string AuthorId, string AuthorName, int Rating, string Text,
    string[]? Photos = null, int? PagesRead = null, bool? Finished = null) : ICommand<string>;

public sealed class CreateReviewCommandValidator : AbstractValidator<CreateReviewCommand>
{
    public CreateReviewCommandValidator(IReviewRepository reviews)
    {
        RuleFor(x => x.BookId).GreaterThan(0);
        RuleFor(x => x.BookTitle).NotEmpty().MaximumLength(300);
        RuleFor(x => x.AuthorId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Rating).InclusiveBetween(Rating.Min, Rating.Max);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(Review.MaxTextLength);
        RuleForEach(x => x.Photos).NotEmpty().Must(p => Uri.IsWellFormedUriString(p, UriKind.Absolute))
            .WithMessage("Photo must be an absolute URL");
        RuleFor(x => x.PagesRead).GreaterThanOrEqualTo(0).When(x => x.PagesRead is not null);

        // асинхронне правило із запитом до MongoDB: один відгук від користувача на книгу
        RuleFor(x => x)
            .MustAsync(async (cmd, ct) => !await reviews.ExistsAsync(cmd.BookId, cmd.AuthorId, ct))
            .OverridePropertyName("AuthorId")
            .WithMessage("You have already reviewed this book")
            .When(x => x.BookId > 0 && !string.IsNullOrWhiteSpace(x.AuthorId));
    }
}

public sealed class CreateReviewCommandHandler : IRequestHandler<CreateReviewCommand, string>
{
    private readonly IReviewRepository _reviews;
    private readonly ILogger<CreateReviewCommandHandler> _logger;

    public CreateReviewCommandHandler(IReviewRepository reviews, ILogger<CreateReviewCommandHandler> logger)
    {
        _reviews = reviews;
        _logger = logger;
    }

    public async Task<string> Handle(CreateReviewCommand cmd, CancellationToken ct)
    {
        var progress = cmd.PagesRead is null ? null : new ReadingProgress(cmd.PagesRead.Value, cmd.Finished ?? false);
        var review = new Review(cmd.BookId, cmd.BookTitle, new Author(cmd.AuthorId, cmd.AuthorName),
            new Rating(cmd.Rating), cmd.Text, cmd.Photos, progress);

        await _reviews.AddAsync(review, ct);                 // унікальний індекс страхує від гонки -> 409
        _logger.LogInformation("Review {ReviewId} created for book {BookId} by {UserId} with rating {Rating}",
            review.Id, review.BookId, review.Author.UserId, review.Rating.Value);
        return review.Id.ToString();
    }
}
