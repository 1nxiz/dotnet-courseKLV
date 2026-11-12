using Books.Bll.Dtos;

namespace Books.Bll.Services;

/// <summary>Бізнес-операції каталогу. Сервіс працює лише через Unit of Work і репозиторії — без прямого DbContext.</summary>
public interface IBookService
{
    Task<PagedResult<BookDto>> GetListAsync(BookQuery query, CancellationToken ct);
    Task<BookCardDto> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<BookDto>> GetPublishedByAuthorAsync(int authorId, CancellationToken ct);
    Task<BookCardDto> CreateAsync(CreateBookDto input, CancellationToken ct);
    Task<BookDto> UpdateAsync(int id, UpdateBookDto input, CancellationToken ct);
    Task<BookDto> ChangePriceAsync(int id, decimal newPrice, CancellationToken ct);
    Task<BookDto> AddCopiesAsync(int id, int quantity, CancellationToken ct);
    Task<BookDto> WriteOffCopiesAsync(int id, int quantity, CancellationToken ct);
    Task<BookDto> PublishAsync(int id, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public interface IGenreService
{
    Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken ct);
    Task<GenreWithBooksDto> GetWithBooksAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<BookDto>> GetRecentlyAddedAsync(string slug, DateTime since, CancellationToken ct);
    Task<IReadOnlyList<GenreStatisticsDto>> GetStatisticsAsync(CancellationToken ct);
}

public interface IAuthorService
{
    Task<IReadOnlyList<AuthorDto>> GetAllAsync(CancellationToken ct);
    Task<AuthorDto> GetByIdAsync(int id, CancellationToken ct);
    Task<AuthorDto> CreateAsync(CreateAuthorDto input, CancellationToken ct);
}
