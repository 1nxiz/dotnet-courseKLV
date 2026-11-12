using AutoMapper;
using Books.Bll.Dtos;
using Books.Bll.Specifications;
using Books.Dal.Repositories;
using Books.Domain.Entities;
using Books.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Books.Bll.Services;

public class BookService : IBookService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<BookService> _logger;

    public BookService(IUnitOfWork uow, IMapper mapper, ILogger<BookService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<PagedResult<BookDto>> GetListAsync(BookQuery query, CancellationToken ct)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, BooksFilterSpec.MaxPageSize);
        var page = Math.Max(query.Page, 1);

        // і фільтр, і сортування, і Skip/Take виконуються в SQL; COUNT(*) теж рахує БД
        var total = await _uow.Books.CountAsync(new BooksFilterSpec(query, applyPaging: false), ct);
        var items = await _uow.Books.ListAsync(new BooksFilterSpec(query), ct);

        return new PagedResult<BookDto>(_mapper.Map<List<BookDto>>(items), total, page, pageSize);
    }

    public async Task<BookCardDto> GetByIdAsync(int id, CancellationToken ct)
    {
        var book = await _uow.Books.GetWithDetailsAsync(id, ct)
                   ?? throw new EntityNotFoundException($"Book {id} not found");
        return _mapper.Map<BookCardDto>(book);
    }

    public async Task<IReadOnlyList<BookDto>> GetPublishedByAuthorAsync(int authorId, CancellationToken ct)
    {
        _ = await _uow.Authors.GetByIdAsync(authorId, ct) ?? throw new EntityNotFoundException($"Author {authorId} not found");
        return _mapper.Map<List<BookDto>>(await _uow.Books.ListAsync(new PublishedBooksByAuthorSpec(authorId), ct));
    }

    public async Task<BookCardDto> CreateAsync(CreateBookDto input, CancellationToken ct)
    {
        // правило, для якого потрібні інші дані з БД, перевіряє сервіс (правила самої сутності — сутність)
        if (await _uow.Books.ExistsByIsbnAsync(input.Isbn.Trim(), ct))
            throw new ConflictException($"Book with ISBN '{input.Isbn}' already exists");
        if (await _uow.Authors.GetByIdAsync(input.AuthorId, ct) is null)
            throw new ValidationException("Book cannot be created",
                new Dictionary<string, string[]> { ["authorId"] = [$"Author {input.AuthorId} not found"] });

        var knownGenres = (await _uow.Genres.GetAllAsync(ct)).Select(g => g.Id).ToHashSet();
        var unknown = input.GenreIds.Where(id => !knownGenres.Contains(id)).ToList();
        if (unknown.Count > 0)
            throw new ValidationException("Book cannot be created",
                new Dictionary<string, string[]> { ["genreIds"] = [$"Unknown genres: {string.Join(", ", unknown)}"] });

        var book = new Book(input.Title, input.Isbn, input.Price, input.AuthorId, input.PublishedYear);
        if (input.InitialCopies > 0) book.AddCopies(input.InitialCopies);
        if (!string.IsNullOrWhiteSpace(input.Description))
            book.SetDetail(input.Description, input.Publisher, input.Pages);
        foreach (var genreId in input.GenreIds)
            book.AddToGenre(genreId);

        await _uow.Books.AddAsync(book, ct);
        await _uow.SaveChangesAsync(ct);                                   // одна транзакція: Book + Detail + BookGenres

        _logger.LogInformation("Book {BookId} created: {Title}, ISBN {Isbn}, price {Price}", book.Id, book.Title, book.Isbn, book.Price);
        return await GetByIdAsync(book.Id, ct);
    }

    public async Task<BookDto> UpdateAsync(int id, UpdateBookDto input, CancellationToken ct)
    {
        var book = await LoadAsync(id, ct);
        if (await _uow.Authors.GetByIdAsync(input.AuthorId, ct) is null)
            throw new ValidationException("Book cannot be updated",
                new Dictionary<string, string[]> { ["authorId"] = [$"Author {input.AuthorId} not found"] });

        book.Rename(input.Title);
        book.ChangeAuthor(input.AuthorId);
        book.ChangePublishedYear(input.PublishedYear);
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Book {BookId} updated", id);
        return _mapper.Map<BookDto>(book);
    }

    public async Task<BookDto> ChangePriceAsync(int id, decimal newPrice, CancellationToken ct)
    {
        var book = await LoadAsync(id, ct);
        var oldPrice = book.ChangePrice(newPrice);                         // інваріант (ціна > 0) перевіряє сутність
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Book {BookId} price changed {OldPrice} -> {NewPrice}", id, oldPrice, newPrice);
        return _mapper.Map<BookDto>(book);
    }

    public async Task<BookDto> AddCopiesAsync(int id, int quantity, CancellationToken ct)
    {
        var book = await LoadAsync(id, ct);
        book.AddCopies(quantity);
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Book {BookId}: {Quantity} copies added, total {TotalCopies}", id, quantity, book.TotalCopies);
        return _mapper.Map<BookDto>(book);
    }

    public async Task<BookDto> WriteOffCopiesAsync(int id, int quantity, CancellationToken ct)
    {
        var book = await LoadAsync(id, ct);
        book.WriteOffCopies(quantity);                                     // нестача -> DomainException -> 409
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Book {BookId}: {Quantity} copies written off, total {TotalCopies}", id, quantity, book.TotalCopies);
        return _mapper.Map<BookDto>(book);
    }

    public async Task<BookDto> PublishAsync(int id, CancellationToken ct)
    {
        var book = await LoadAsync(id, ct);
        book.Publish();
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Book {BookId} published", id);
        return _mapper.Map<BookDto>(book);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var book = await LoadAsync(id, ct);
        _uow.Books.Delete(book);                                           // деталі, зображення, жанри — каскадно
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Book {BookId} deleted", id);
    }

    private async Task<Book> LoadAsync(int id, CancellationToken ct)
        => await _uow.Books.GetByIdAsync(id, ct) ?? throw new EntityNotFoundException($"Book {id} not found");
}
