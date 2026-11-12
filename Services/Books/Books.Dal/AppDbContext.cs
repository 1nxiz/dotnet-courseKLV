using Books.Dal.Entities;
using Books.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Books.Dal;

/// <summary>Контекст бази BooksDb (Проєкт №2 з Лаб. 01), Code First.</summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Book> Books => Set<Book>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<BookGenre> BookGenres => Set<BookGenre>();
    public DbSet<BookDetail> BookDetails => Set<BookDetail>();
    public DbSet<BookImage> BookImages => Set<BookImage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();   // Лаб. 08: ідемпотентність споживача

    /// <summary>Усі IEntityTypeConfiguration&lt;T&gt; цієї збірки підключаються централізовано.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    /// <summary>Аудит: UpdatedAt заповнюється для кожної зміненої книги перед збереженням.</summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Book>().Where(e => e.State == EntityState.Modified))
            entry.Property(b => b.UpdatedAt).CurrentValue = DateTime.UtcNow;
        return base.SaveChangesAsync(cancellationToken);
    }
}
