using Feedback.Application.Common.Exceptions;
using Feedback.Application.Common.Models;
using Feedback.Application.Queries.GetBookReviewsSummary;
using Feedback.Application.Queries.GetReviewById;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Library.Feedback.Grpc;
using MediatR;

namespace Feedback.WebApi.Grpc;

/// <summary>
/// gRPC-адаптер Presentation-шару: як і контролери, лише надсилає запити через ISender (MediatR), тож проходить
/// увесь pipeline — логування, валідацію і CachingBehavior. Винятки Application-шару -> RpcException з точним кодом.
/// </summary>
[RequirePermission(Permissions.Feedback.Read)]   // Лаб. 09: Aggregator викликає з токеном Client Credentials (scope feedback:read)
public sealed class FeedbackGrpcService : FeedbackService.FeedbackServiceBase
{
    private const int MaxBooks = 50;
    private readonly ISender _mediator;

    public FeedbackGrpcService(ISender mediator) => _mediator = mediator;

    public override async Task<BooksReviewsReply> GetBooksReviews(GetBooksReviewsRequest request, ServerCallContext context)
    {
        if (request.BookIds.Count is 0 or > MaxBooks || request.BookIds.Any(id => id <= 0))
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"book_ids: 1..{MaxBooks} positive values expected"));
        var latest = request.LatestPerBook switch
        {
            <= 0 => 3,
            > GetBookReviewsSummaryQuery.MaxLatest => GetBookReviewsSummaryQuery.MaxLatest,
            var n => n
        };

        var reply = new BooksReviewsReply();
        foreach (var bookId in request.BookIds.Distinct())            // кожен підсумок — окремий ключ кешу
        {
            var summary = await Send(new GetBookReviewsSummaryQuery(bookId), context);
            var book = new BookReviews { BookId = bookId, ReviewsCount = summary.ReviewsCount, AverageRating = summary.AverageRating };
            book.Latest.AddRange(summary.Latest.Take(latest).Select(ToReply));
            reply.Books.Add(book);
        }
        return reply;
    }

    public override async Task<ReviewReply> GetReview(GetReviewRequest request, ServerCallContext context)
    {
        var r = await Send(new GetReviewByIdQuery(request.Id), context);
        return new ReviewReply
        {
            Id = r.Id, BookId = r.BookId, AuthorName = r.Author.DisplayName, Rating = r.Rating, Text = r.Text,
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc)),
            CommentsCount = r.Comments.Count
        };
    }

    private async Task<T> Send<T>(IRequest<T> query, ServerCallContext context)
    {
        try
        {
            return await _mediator.Send(query, context.CancellationToken);
        }
        catch (FluentValidation.ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, string.Join("; ", ex.Errors.Select(e => e.ErrorMessage))));
        }
        catch (NotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (ConflictException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
    }

    private static ReviewReply ToReply(ReviewDto r) => new()
    {
        Id = r.Id, BookId = r.BookId, AuthorName = r.AuthorName, Rating = r.Rating, Text = r.Text,
        CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc)),
        CommentsCount = r.CommentsCount
    };
}
