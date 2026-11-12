using Books.Bll.Mapping;
using Books.Bll.Services;
using Books.Bll.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Books.Bll;

public static class DependencyInjection
{
    public static IServiceCollection AddBooksBll(this IServiceCollection services, string? autoMapperLicenseKey = null)
    {
        services.AddAutoMapper(cfg =>
        {
            cfg.LicenseKey = autoMapperLicenseKey;          // Community-ліцензія AutoMapper 15+ (automapper.io)
            cfg.AddMaps(typeof(CatalogProfile).Assembly);
        });

        // FluentValidation: валідатори реєструються, а викликаються явно (AddFluentValidation застарів)
        services.AddValidatorsFromAssemblyContaining<CreateBookDtoValidator>();

        services.AddScoped<IBookService, BookService>();
        services.AddScoped<IGenreService, GenreService>();
        services.AddScoped<IAuthorService, AuthorService>();
        return services;
    }
}
