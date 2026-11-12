using Books.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Books.Dal.Seeding;

/// <summary>
/// Застосування міграцій і ПРОГРАМНИЙ сідінг (довідник Genres сідиться статично через HasData у міграції).
/// Ідемпотентно: якщо книги вже є — нічого не додається. Порядок дотримується цілісності FK:
/// автори -> книги -> деталі/зображення/жанри. Книги зберігаються по одній, щоб Id 1..8 збігалися
/// з локальною копією довідника в LoansDb (Лаб. 01-02).
/// </summary>
public static class DbInitializer
{
    public static async Task MigrateAsync(AppDbContext db, bool recreateIfNotMigrated, ILogger logger, CancellationToken ct = default)
    {
        // База могла бути створена скриптом db/p2/schema.sql з Лаб. 01 (без історії міграцій EF).
        // У Development її безпечно перестворити міграціями — інакше MigrateAsync впаде на «object already exists».
        if (recreateIfNotMigrated && await db.Database.CanConnectAsync(ct))
        {
            var applied = await db.Database.GetAppliedMigrationsAsync(ct);
            var hasTables = await TableExistsAsync(db, "Books", ct);
            if (!applied.Any() && hasTables)
            {
                logger.LogWarning("BooksDb exists but was not created by EF migrations — recreating it (Development only)");
                await db.Database.EnsureDeletedAsync(ct);
            }
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(ct);
                logger.LogInformation("BooksDb migrations applied");
                return;
            }
            catch (Exception ex) when (attempt < 10 && ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Database is not ready (attempt {Attempt}), retrying in 3 s", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Books.AnyAsync(ct))
        {
            logger.LogInformation("BooksDb already seeded — skipping");
            return;                                                     // уже наповнено
        }

        var authors = new[]
        {
            new Author("Тарас Шевченко", "Україна", 1814),
            new Author("Іван Багряний", "Україна", 1906),
            new Author("Валер'ян Підмогильний", "Україна", 1901),
            new Author("Robert C. Martin", "USA", 1952),
            new Author("Eric Evans", "USA"),
            new Author("Martin Kleppmann", "Germany"),
            new Author("Леся Українка", "Україна", 1871),
            new Author("Михайло Коцюбинський", "Україна", 1864)
        };
        foreach (var author in authors)
        {
            db.Authors.Add(author);
            await db.SaveChangesAsync(ct);
        }

        var seed = new (string Title, string Isbn, decimal Price, int Copies, short Year, bool Published, int AuthorIdx,
            string Description, string Publisher, int Pages, string Language, int[] Genres, string AddedAt)[]
        {
            ("Кобзар", "978-966-03-4000-1", 25.00m, 6, 1840, true, 0, "Збірка поетичних творів Тараса Шевченка.", "Дніпро", 624, "uk", [1, 6], "2026-01-10"),
            ("Тигролови", "978-966-03-4000-2", 30.00m, 4, 1944, true, 1, "Пригодницький роман про родину Сірків на Далекому Сході.", "Фоліо", 352, "uk", [2, 3], "2026-02-01"),
            ("Місто", "978-966-03-4000-3", 28.50m, 6, 1928, true, 2, "Урбаністичний роман про Київ 1920-х років.", "Фоліо", 288, "uk", [3, 1], "2026-03-15"),
            ("Clean Architecture", "978-0-13-449416-6", 60.00m, 3, 2017, true, 3, "A Craftsman's Guide to Software Structure and Design.", "Prentice Hall", 432, "en", [4, 5], "2026-09-01"),
            ("Domain-Driven Design", "978-0-321-12521-7", 65.00m, 3, 2003, true, 4, "Tackling Complexity in the Heart of Software.", "Addison-Wesley", 560, "en", [5], "2026-09-20"),
            ("Designing Data-Intensive Applications", "978-1-4493-7332-0", 70.00m, 4, 2017, true, 5, "The Big Ideas Behind Reliable, Scalable, and Maintainable Systems.", "O'Reilly", 616, "en", [4, 5], "2026-10-05"),
            ("Лісова пісня", "978-966-03-4000-7", 20.00m, 7, 1911, true, 6, "Драма-феєрія у трьох діях.", "Знання", 192, "uk", [1, 6], "2026-04-01"),
            ("Intermezzo", "978-966-03-4000-8", 15.00m, 2, 1908, false, 7, "Новела Михайла Коцюбинського.", "Знання", 64, "uk", [1], "2026-10-30")
        };

        foreach (var s in seed)
        {
            var book = new Book(s.Title, s.Isbn, s.Price, authors[s.AuthorIdx].Id, s.Year);
            book.AddCopies(s.Copies);
            if (s.Published) book.Publish();
            book.SetDetail(s.Description, s.Publisher, s.Pages, s.Language);
            book.AddImage($"https://cdn.library.example/books/{book.Isbn}/cover.jpg");
            foreach (var genreId in s.Genres)
                book.AddToGenre(genreId, DateTime.SpecifyKind(DateTime.Parse(s.AddedAt), DateTimeKind.Utc));
            db.Books.Add(book);
            await db.SaveChangesAsync(ct);                              // по одній — Id 1..8 у порядку списку
        }

        logger.LogInformation("BooksDb seeded: {Authors} authors, {Books} books", authors.Length, seed.Length);
    }

    private static async Task<bool> TableExistsAsync(AppDbContext db, string table, CancellationToken ct)
    {
        var count = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = {table}")
            .SingleAsync(ct);
        return count > 0;
    }
}
