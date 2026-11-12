using Feedback.Application.Caching;
using AutoMapper;
using Feedback.Application.Common.Exceptions;
using Feedback.Application.Common.Messaging;
using Feedback.Application.Common.Models;
using Feedback.Domain.Entities;
using Feedback.Domain.Interfaces;
using Feedback.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using MongoDB.Bson;

namespace Feedback.Application.Commands.UpdateReview;

/// <summary>Тіло PUT /api/reviews/{id}.</summary>
public record UpdateReviewRequest(int Rating, string Text, int? PagesRead = null, bool? Finished = null);

/// <summary>Редагувати відгук. ExpectedVersion (із заголовка If-Match) — оптимістична конкурентність.</summary>
public record UpdateReviewCommand(string Id, int Rating, string Text, int? PagesRead, bool? Finished, int? ExpectedVersion)
    : ICommand<ReviewDetailsDto>;

public sealed class UpdateReviewCommandValidator : AbstractValidator<UpdateReviewCommand>
{
    public UpdateReviewCommandValidator()
    {
        RuleFor(x => x.Id).Must(id => ObjectId.TryParse(id, out _)).WithMessage("Id must be a valid ObjectId");
        RuleFor(x => x.Rating).InclusiveBetween(Rating.Min, Rating.Max);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(Review.MaxTextLength);
        RuleFor(x => x.PagesRead).GreaterThanOrEqualTo(0).When(x => x.PagesRead is not null);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0).When(x => x.ExpectedVersion is not null);
    }
}

public sealed class UpdateReviewCommandHandler : IRequestHandler<UpdateReviewCommand, ReviewDetailsDto>
{
    private readonly IReviewRepository _reviews;
    private readonly IMapper _mapper;
    private readonly IFeedbackCache _cache;

    public UpdateReviewCommandHandler(IReviewRepository reviews, IMapper mapper, IFeedbackCache cache)
    {
        _reviews = reviews;
        _mapper = mapper;
        _cache = cache;
    }

    public async Task<ReviewDetailsDto> Handle(UpdateReviewCommand cmd, CancellationToken ct)
    {
        var review = await _reviews.GetByIdAsync(ObjectId.Parse(cmd.Id), ct) ?? throw new NotFoundException("Review", cmd.Id);
        if (cmd.ExpectedVersion is not null && cmd.ExpectedVersion != review.Version)
            throw new ConflictException($"Review {cmd.Id} has version {review.Version}, expected {cmd.ExpectedVersion}");

        var progress = cmd.PagesRead is null ? null : new ReadingProgress(cmd.PagesRead.Value, cmd.Finished ?? false);
        review.Edit(new Rating(cmd.Rating), cmd.Text, progress);
        await _reviews.UpdateAsync(review, ct);             // фільтр за Version: паралельна зміна -> ConflictException
        await _cache.InvalidateAsync("ReviewUpdated", ct, CacheKeys.Review(cmd.Id), CacheKeys.BookSummary(review.BookId));
        return _mapper.Map<ReviewDetailsDto>(review);
    }
}
