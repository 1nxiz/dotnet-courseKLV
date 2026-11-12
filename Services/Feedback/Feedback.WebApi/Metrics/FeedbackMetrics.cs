using System.Diagnostics;
using System.Diagnostics.Metrics;
using Feedback.Application.Commands.AddComment;
using Feedback.Application.Commands.CreateReview;
using Feedback.Application.Commands.PostMessage;
using Feedback.Application.Commands.StartDiscussion;
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace Feedback.WebApi.Metrics;

/// <summary>
/// Бізнес-метрики сервісу відгуків (Meter "Feedback.WebApi"). Singleton. Теги з малою кардинальністю:
/// review.rating (1..5), request (назва типу команди/запиту — обмежений набір), outcome.
/// </summary>
public sealed class FeedbackMetrics
{
    public const string MeterName = "Feedback.WebApi";

    private readonly Counter<long> _reviews;
    private readonly Counter<long> _comments;
    private readonly Counter<long> _discussions;
    private readonly Counter<long> _messages;
    private readonly Histogram<double> _duration;

    public FeedbackMetrics(IMeterFactory meterFactory, IMemoryCache cache)
    {
        var meter = meterFactory.Create(MeterName);
        _reviews = meter.CreateCounter<long>("feedback.reviews.created", "{review}", "Created reviews by rating");
        _comments = meter.CreateCounter<long>("feedback.comments.added", "{comment}", "Comments added to reviews");
        _discussions = meter.CreateCounter<long>("feedback.discussions.started", "{discussion}", "Started discussions");
        _messages = meter.CreateCounter<long>("feedback.messages.posted", "{message}", "Posted discussion messages");
        _duration = meter.CreateHistogram<double>("feedback.request.duration", "ms", "Duration of CQRS commands and queries");
        meter.CreateObservableGauge("feedback.cache.entries",
            () => (cache as MemoryCache)?.GetCurrentStatistics()?.CurrentEntryCount ?? 0,
            "{entry}", "Entries currently held in the in-memory cache");
    }

    public void Record(object request, string outcome, double ms)
    {
        _duration.Record(ms, new KeyValuePair<string, object?>("request", request.GetType().Name),
            new KeyValuePair<string, object?>("outcome", outcome));
        if (outcome != "success") return;
        switch (request)                                       // лічильник — лише після успішної операції
        {
            case CreateReviewCommand c: _reviews.Add(1, new KeyValuePair<string, object?>("review.rating", c.Rating)); break;
            case AddCommentCommand: _comments.Add(1); break;
            case StartDiscussionCommand: _discussions.Add(1); break;
            case PostMessageCommand: _messages.Add(1); break;
        }
    }
}

/// <summary>
/// MediatR behavior шару Presentation: вимірює кожну команду/запит (успіх і збій) і рахує бізнес-події.
/// Реєструється у WebApi, тож Application лишається без залежності від метрик.
/// </summary>
public sealed class MetricsBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly FeedbackMetrics _metrics;

    public MetricsBehavior(FeedbackMetrics metrics) => _metrics = metrics;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var outcome = "failure";
        try
        {
            var response = await next();
            outcome = "success";
            return response;
        }
        finally
        {
            _metrics.Record(request, outcome, sw.Elapsed.TotalMilliseconds);
        }
    }
}
