using Ardalis.Specification;
using Books.Bll.Dtos;
using Books.Domain.Entities;

namespace Books.Bll.Specifications;

/// <summary>
/// Специфікація каталогу: критерії фільтрації, сортування, Include і пагінація винесені з сервісу.
/// Не залежить від EF Core — репозиторій лише застосовує її (WithSpecification) і виконує ОДИН SQL-запит.
/// </summary>
public sealed class BooksFilterSpec : Specification<Book>
{
    public const int MaxPageSize = 100;

    public BooksFilterSpec(BookQuery q, bool applyPaging = true)
    {
        Query.Include(b => b.Author);
        Query.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var search = q.Search.Trim();
            Query.Where(b => b.Title.Contains(search) || b.Isbn.Contains(search));
        }
        if (q.MinPrice is not null) Query.Where(b => b.Price >= q.MinPrice);
        if (q.MaxPrice is not null) Query.Where(b => b.Price <= q.MaxPrice);
        if (q.AuthorId is not null) Query.Where(b => b.AuthorId == q.AuthorId);
        if (q.GenreId is not null) Query.Where(b => b.BookGenres.Any(bg => bg.GenreId == q.GenreId));
        if (q.IsPublished is not null) Query.Where(b => b.IsPublished == q.IsPublished);

        var desc = string.Equals(q.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        switch (q.SortBy?.ToLowerInvariant())                 // лише дозволені поля сортування
        {
            case "price":
                if (desc) Query.OrderByDescending(b => b.Price).ThenBy(b => b.Id); else Query.OrderBy(b => b.Price).ThenBy(b => b.Id);
                break;
            case "year":
                if (desc) Query.OrderByDescending(b => b.PublishedYear).ThenBy(b => b.Id); else Query.OrderBy(b => b.PublishedYear).ThenBy(b => b.Id);
                break;
            case "createdat":
                if (desc) Query.OrderByDescending(b => b.CreatedAt).ThenBy(b => b.Id); else Query.OrderBy(b => b.CreatedAt).ThenBy(b => b.Id);
                break;
            default:
                if (desc) Query.OrderByDescending(b => b.Title).ThenBy(b => b.Id); else Query.OrderBy(b => b.Title).ThenBy(b => b.Id);
                break;
        }

        if (applyPaging)
        {
            var pageSize = Math.Clamp(q.PageSize, 1, MaxPageSize);
            var page = Math.Max(q.Page, 1);
            Query.Skip((page - 1) * pageSize).Take(pageSize);  // SQL: OFFSET ... FETCH NEXT ...
        }
    }
}

/// <summary>Друга специфікація з іншими критеріями: опубліковані книги автора, найновіші першими.
/// Працює через той самий WithSpecification() без змін у репозиторії.</summary>
public sealed class PublishedBooksByAuthorSpec : Specification<Book>
{
    public PublishedBooksByAuthorSpec(int authorId, int take = 10)
    {
        Query.Where(b => b.AuthorId == authorId && b.IsPublished);
        Query.Include(b => b.Author);
        Query.OrderByDescending(b => b.PublishedYear).ThenBy(b => b.Id);
        Query.Take(take);
        Query.AsNoTracking();
    }
}
