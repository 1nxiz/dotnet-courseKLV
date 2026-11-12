using Books.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Books.Dal.Repositories;

public class BookRepository : GenericRepository<Book>, IBookRepository
{
    public BookRepository(AppDbContext context) : base(context) { }

    /// <summary>EAGER LOADING: книга + деталі + зображення + автор + жанри одним запитом (JOIN).</summary>
    public Task<Book?> GetWithDetailsAsync(int id, CancellationToken ct = default)
        => Set.Include(b => b.Detail)
              .Include(b => b.Author)
              .Include(b => b.Images)
              .Include(b => b.BookGenres).ThenInclude(bg => bg.Genre)
              .FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<bool> ExistsByIsbnAsync(string isbn, CancellationToken ct = default)
        => Set.AnyAsync(b => b.Isbn == isbn, ct);
}

public class GenreRepository : GenericRepository<Genre>, IGenreRepository
{
    public GenreRepository(AppDbContext context) : base(context) { }

    /// <summary>EXPLICIT LOADING: 1-й запит — жанр; 2-й — його книги (через проміжну сутність) лише тоді, коли потрібні.</summary>
    public async Task<Genre?> GetWithBooksAsync(int id, CancellationToken ct = default)
    {
        var genre = await Set.FirstOrDefaultAsync(g => g.Id == id, ct);          // 1-й SQL-запит
        if (genre is null) return null;

        await Context.Entry(genre)
            .Collection(g => g.BookGenres)
            .Query()
            .Include(bg => bg.Book)
            .LoadAsync(ct);                                                      // 2-й SQL-запит
        return genre;
    }
}

public class AuthorRepository : GenericRepository<Author>, IAuthorRepository
{
    public AuthorRepository(AppDbContext context) : base(context) { }
}

/// <summary>LINQ to Entities: вибірки M:N через проміжну сутність BookGenre та її власне поле AddedAt.</summary>
public class BookGenreRepository : IBookGenreRepository
{
    private readonly AppDbContext _context;

    public BookGenreRepository(AppDbContext context) => _context = context;

    public async Task<IReadOnlyList<RecentlyAddedBook>> GetRecentlyAddedAsync(string genreSlug, DateTime since, CancellationToken ct = default)
        => await _context.BookGenres
            .Where(bg => bg.Genre.Slug == genreSlug && bg.AddedAt >= since)      // фільтр за ВЛАСНИМ полем M:N
            .OrderByDescending(bg => bg.AddedAt)
            .Select(bg => new RecentlyAddedBook(bg.Book, bg.Book.Author!.FullName, bg.AddedAt))
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<(Genre Genre, int BooksCount)>> GetGenreStatisticsAsync(CancellationToken ct = default)
    {
        var rows = await _context.Genres
            .Select(g => new { Genre = g, BooksCount = g.BookGenres.Count() })
            .OrderByDescending(x => x.BooksCount)
            .AsNoTracking()
            .ToListAsync(ct);
        return rows.Select(r => (r.Genre, r.BooksCount)).ToList();
    }
}
