using AutoMapper;
using Loans.Bll.Dtos;
using Loans.Dal.Abstractions;
using Loans.Domain.Exceptions;
using Loans.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Loans.Bll.Services;

public sealed class BookCopyService : IBookCopyService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<BookCopyService> _logger;

    public BookCopyService(IUnitOfWork uow, IMapper mapper, ILogger<BookCopyService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BookCopyDto>> GetAllAsync(CancellationToken ct)
        => _mapper.Map<List<BookCopyDto>>(await _uow.Books.GetAllAsync(ct));

    public async Task<BookCopyDto> GetByIdAsync(long id, CancellationToken ct)
    {
        var book = await _uow.Books.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Book {id} not found");
        return _mapper.Map<BookCopyDto>(book);
    }

    public async Task<(BookCopyDto Book, bool Created)> UpsertAsync(UpsertBookCopyDto input, string? user, CancellationToken ct)
    {
        var book = new Book
        {
            Id = input.Id,
            Title = input.Title.Trim(),
            Isbn = input.Isbn,
            Price = input.Price,
            AvailableCopies = input.AvailableCopies,
            CreatedBy = user,
            UpdatedBy = user
        };

        var created = false;
        if (!await _uow.Books.UpdateAsync(book, ct))
        {
            await _uow.Books.AddAsync(book, ct);
            created = true;
        }
        _logger.LogInformation("Book copy {BookId} {Action}: price {Price}, available {AvailableCopies}",
            book.Id, created ? "created" : "updated", book.Price, book.AvailableCopies);
        return (await GetByIdAsync(book.Id, ct), created);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        if (!await _uow.Books.DeleteAsync(id, ct))
            throw new NotFoundException($"Book {id} not found");
    }
}
