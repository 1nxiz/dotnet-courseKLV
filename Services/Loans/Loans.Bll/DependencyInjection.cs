using Loans.Bll.Mapping;
using Loans.Bll.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Loans.Bll;

public static class DependencyInjection
{
    /// <param name="autoMapperLicenseKey">
    /// Ключ безкоштовної Community-ліцензії AutoMapper 15+ (automapper.io). Без нього застосунок працює,
    /// але записує в лог повідомлення про відсутню ліцензію.
    /// </param>
    public static IServiceCollection AddLoansBll(this IServiceCollection services, string? autoMapperLicenseKey = null)
    {
        // усі профілі AutoMapper з цієї збірки — централізовано
        services.AddAutoMapper(cfg =>
        {
            cfg.LicenseKey = autoMapperLicenseKey;
            cfg.AddMaps(typeof(LoanProfile).Assembly);
        });

        services.AddScoped<ILoanService, LoanService>();
        services.AddScoped<IReaderService, ReaderService>();
        services.AddScoped<IBookCopyService, BookCopyService>();
        return services;
    }
}
