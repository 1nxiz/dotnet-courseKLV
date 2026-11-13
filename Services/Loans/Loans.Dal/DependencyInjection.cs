using Loans.Dal.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Loans.Dal;

public static class DependencyInjection
{
    /// <summary>
    /// Unit of Work реєструється як Scoped: один екземпляр і одне з'єднання на HTTP-запит.
    /// Контейнер викличе DisposeAsync наприкінці запиту — з'єднання повернеться в пул.
    /// </summary>
    public static IServiceCollection AddLoansDal(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<IUnitOfWork>(_ => new UnitOfWork(connectionString));
        return services;
    }
}
