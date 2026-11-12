using Books.Bll.Dtos;
using Books.Bll.Services;
using Books.Domain.Exceptions;
using Grpc.Core;
using Library.Books.Grpc;

namespace Books.Api.Grpc;

/// <summary>
/// gRPC-адаптер над BLL (як контролер, але для внутрішніх клієнтів): валідує запит, викликає IBookService
/// (з кешем L1/L2), перетворює DTO на protobuf-повідомлення, доменні винятки — на RpcException з точним кодом.
/// </summary>
public sealed class BooksGrpcService : BooksCatalog.BooksCatalogBase
{
    private const int MaxBatch = 100;
    private readonly IBookService _books;
    private readonly ILogger<BooksGrpcService> _logger;

    public BooksGrpcService(IBookService books, ILogger<BooksGrpcService> logger)
    {
        _books = books;
        _logger = logger;
    }

    public override async Task<BookReply> GetBook(GetBookRequest request, ServerCallContext context)
    {
        if (request.Id <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "id must be positive"));
        try
        {
            return ToReply(await _books.GetByIdAsync(request.Id, context.CancellationToken));
        }
        catch (EntityNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public override async Task<BooksReply> GetBooksByIds(GetBooksByIdsRequest request, ServerCallContext context)
    {
        if (request.Ids.Count is 0 or > MaxBatch)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"ids: from 1 to {MaxBatch} values expected"));
        if (request.Ids.Any(id => id <= 0))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "ids must be positive"));

        var reply = new BooksReply();
        // послідовно: DbContext у scope запиту не потокобезпечний; більшість читань — попадання в кеш
        foreach (var id in request.Ids.Distinct())
        {
            try
            {
                reply.Books.Add(ToReply(await _books.GetByIdAsync(id, context.CancellationToken)));
            }
            catch (EntityNotFoundException)
            {
                reply.NotFoundIds.Add(id);
            }
        }
        _logger.LogInformation("gRPC GetBooksByIds: {Requested} requested, {Found} found, {NotFound} not found",
            request.Ids.Count, reply.Books.Count, reply.NotFoundIds.Count);
        return reply;
    }

    private static BookReply ToReply(BookCardDto b)
    {
        var reply = new BookReply
        {
            Id = b.Id,
            Title = b.Title,
            Isbn = b.Isbn,
            Price = b.Price,                                   // implicit decimal -> DecimalValue
            TotalCopies = b.TotalCopies,
            IsPublished = b.IsPublished,
            AuthorId = b.AuthorId,
            AuthorName = b.AuthorName,                         // StringValue: null -> поле відсутнє
            PublishedYear = b.PublishedYear
        };
        reply.Genres.AddRange(b.Genres.Select(g => g.GenreName));
        return reply;
    }
}
