using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Books.Dal;

/// <summary>
/// Фабрика для інструмента dotnet-ef (міграції), щоб не залежати від запуску хосту API.
/// Рядок підключення — зі змінної ConnectionStrings__BooksDb або локальний за замовчуванням.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__BooksDb")
            ?? "Server=localhost,1433;Database=BooksDb;User Id=sa;Password=Library_Pass123;TrustServerCertificate=True;Encrypt=False";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        return new AppDbContext(options);
    }
}
