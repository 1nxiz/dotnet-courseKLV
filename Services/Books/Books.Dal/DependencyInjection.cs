using Books.Dal.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Books.Dal;

public static class DependencyInjection
{
    public static IServiceCollection AddBooksDal(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString,
            sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services;
    }
}
