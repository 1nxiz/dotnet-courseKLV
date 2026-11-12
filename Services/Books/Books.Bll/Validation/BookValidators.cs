using Books.Bll.Dtos;
using FluentValidation;

namespace Books.Bll.Validation;

/// <summary>
/// Складніші правила створення книги (FluentValidation). Прості правила обов'язковості й довжини — DataAnnotations на DTO;
/// тут — ціна, формат ISBN, рік, кількість примірників, жанри. Унікальність ISBN (потрібна БД) перевіряє сервіс -> 409.
/// </summary>
public class CreateBookDtoValidator : AbstractValidator<CreateBookDto>
{
    public CreateBookDtoValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title must not be empty");
        RuleFor(x => x.Isbn).NotEmpty().Matches(@"^(97[89]-?)?\d{1,5}-?\d{1,7}-?\d{1,7}-?[\dX]$")
            .WithMessage("ISBN must look like 978-966-03-4000-1");
        RuleFor(x => x.Price).GreaterThan(0).LessThanOrEqualTo(100_000);
        RuleFor(x => x.AuthorId).GreaterThan(0);
        RuleFor(x => x.PublishedYear).InclusiveBetween((short)1450, (short)(DateTime.UtcNow.Year + 1))
            .When(x => x.PublishedYear is not null);
        RuleFor(x => x.InitialCopies).InclusiveBetween(0, 10_000);
        RuleFor(x => x.Pages).GreaterThan(0).When(x => x.Pages is not null);
        RuleFor(x => x.GenreIds).Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("Genres must not repeat");
        RuleFor(x => x.Description).NotEmpty().When(x => x.Publisher is not null || x.Pages is not null)
            .WithMessage("Description is required when publisher or pages are specified");
    }
}

public class UpdateBookDtoValidator : AbstractValidator<UpdateBookDto>
{
    public UpdateBookDtoValidator()
    {
        RuleFor(x => x.Title).NotEmpty();
        RuleFor(x => x.AuthorId).GreaterThan(0);
        RuleFor(x => x.PublishedYear).InclusiveBetween((short)1450, (short)(DateTime.UtcNow.Year + 1))
            .When(x => x.PublishedYear is not null);
    }
}

public class ChangePriceDtoValidator : AbstractValidator<ChangePriceDto>
{
    public ChangePriceDtoValidator() => RuleFor(x => x.Price).GreaterThan(0).LessThanOrEqualTo(100_000);
}

public class CopiesDtoValidator : AbstractValidator<CopiesDto>
{
    public CopiesDtoValidator() => RuleFor(x => x.Quantity).InclusiveBetween(1, 10_000);
}

public class BookQueryValidator : AbstractValidator<BookQuery>
{
    private static readonly string[] SortFields = ["title", "price", "year", "createdat"];

    public BookQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.SortBy).Must(s => s is not null && SortFields.Contains(s.ToLowerInvariant()))
            .WithMessage("sortBy must be one of: title, price, year, createdAt");
        RuleFor(x => x.SortDir).Must(s => s is not null && (s.Equals("asc", StringComparison.OrdinalIgnoreCase) || s.Equals("desc", StringComparison.OrdinalIgnoreCase))).WithMessage("sortDir must be asc or desc");
        RuleFor(x => x).Must(x => x.MinPrice is null || x.MaxPrice is null || x.MinPrice <= x.MaxPrice)
            .WithName("minPrice").WithMessage("minPrice must not exceed maxPrice");
    }
}
