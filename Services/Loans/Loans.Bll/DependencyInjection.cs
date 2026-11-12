using Loans.Bll.Mapping;
using Loans.Bll.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Loans.Bll;

public static class DependencyInjection
{
    public static IServiceCollection AddLoansBll(this IServiceCollection services)
    {
        // усі профілі AutoMapper з цієї збірки — централізовано
        services.AddAutoMapper(typeof(LoanProfile).Assembly);

        services.AddScoped<ILoanService, LoanService>();
        services.AddScoped<IReaderService, ReaderService>();
        services.AddScoped<IBookCopyService, BookCopyService>();
        return services;
    }
}
