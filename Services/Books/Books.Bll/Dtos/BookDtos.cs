using System.ComponentModel.DataAnnotations;

namespace Books.Bll.Dtos;

public record BookDto(int Id, string Title, string Isbn, decimal Price, int TotalCopies, short? PublishedYear,
    bool IsPublished, int AuthorId, string? AuthorName);

public record BookImageDto(int Id, string Url, int SortOrder);

public record BookGenreDto(int GenreId, string GenreName, DateTime AddedAt);

public record BookDetailDto(string Description, string? Publisher, int? Pages, string Language);

/// <summary>Повна картка книги (eager loading).</summary>
public record BookCardDto(int Id, string Title, string Isbn, decimal Price, int TotalCopies, short? PublishedYear,
    bool IsPublished, int AuthorId, string? AuthorName, BookDetailDto? Detail,
    IReadOnlyList<BookImageDto> Images, IReadOnlyList<BookGenreDto> Genres);

/// <summary>
/// Створення книги. Прості правила — DataAnnotations (атрибути нижче), складні — CreateBookDtoValidator
/// (формат ISBN, рік, жанри). Одне правило не дублюється в обох місцях.
/// </summary>
public class CreateBookDto
{
    [Required, StringLength(300)]
    public string Title { get; set; } = "";

    [Required, StringLength(20)]
    public string Isbn { get; set; } = "";

    public decimal Price { get; set; }

    public int AuthorId { get; set; }

    public short? PublishedYear { get; set; }

    public int InitialCopies { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }

    [StringLength(200)]
    public string? Publisher { get; set; }

    public int? Pages { get; set; }

    public List<int> GenreIds { get; set; } = new();
}

public class UpdateBookDto
{
    [Required, StringLength(300)]
    public string Title { get; set; } = "";

    public int AuthorId { get; set; }

    public short? PublishedYear { get; set; }
}

public class ChangePriceDto
{
    public decimal Price { get; set; }
}

public class CopiesDto
{
    public int Quantity { get; set; }
}

/// <summary>
/// Контракт запиту колекції: page (з 1), pageSize (за замовчуванням 20, максимум 100),
/// фільтри (search, minPrice, maxPrice, authorId, genreId, isPublished) і сортування (sortBy + sortDir).
/// </summary>
public class BookQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public int? AuthorId { get; set; }
    public int? GenreId { get; set; }
    public bool? IsPublished { get; set; }

    /// <summary>Поле сортування: title | price | year | createdAt.</summary>
    public string SortBy { get; set; } = "title";

    /// <summary>Напрям: asc | desc.</summary>
    public string SortDir { get; set; } = "asc";
}

/// <summary>Контракт відповіді колекції: items + метадані пагінації (також дублюються в заголовках X-Pagination-*).</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}
