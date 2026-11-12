using Books.Dal.Repositories;

namespace Books.Dal;

/// <summary>
/// Unit of Work на EF Core: репозиторії лише додають/змінюють/видаляють сутності в контексті,
/// а SaveChangesAsync() викликається ОДИН раз наприкінці бізнес-операції — усі зміни потрапляють
/// у базу в одній транзакції (як OrderingContext : IUnitOfWork в eShop).
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
        Books = new BookRepository(context);
        Genres = new GenreRepository(context);
        BookGenres = new BookGenreRepository(context);
        Authors = new AuthorRepository(context);
        ProcessedEvents = new ProcessedEventRepository(context);
    }

    public IBookRepository Books { get; }
    public IGenreRepository Genres { get; }
    public IBookGenreRepository BookGenres { get; }
    public IAuthorRepository Authors { get; }
    public IProcessedEventRepository ProcessedEvents { get; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
