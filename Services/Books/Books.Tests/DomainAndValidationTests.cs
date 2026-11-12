using Ardalis.Specification;
using AutoMapper;
using Books.Bll.Dtos;
using Books.Bll.Mapping;
using Books.Bll.Specifications;
using Books.Bll.Validation;
using Books.Domain.Entities;
using Books.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Books.Tests;

public class BookEntityTests
{
    [Fact]
    public void Price_must_be_positive()
        => Assert.Throws<DomainException>(() => new Book("Title", "978-0-00-000000-0", 0m, 1));

    [Fact]
    public void Cannot_write_off_more_copies_than_available()
    {
        var book = new Book("Title", "978-0-00-000000-0", 10m, 1);
        book.AddCopies(2);
        Assert.Throws<DomainException>(() => book.WriteOffCopies(3));
        book.WriteOffCopies(2);
        Assert.Equal(0, book.TotalCopies);
    }

    [Fact]
    public void ChangePrice_returns_old_price()
    {
        var book = new Book("Title", "978-0-00-000000-0", 10m, 1);
        Assert.Equal(10m, book.ChangePrice(12.5m));
        Assert.Equal(12.5m, book.Price);
    }

    [Fact]
    public void Adding_same_genre_twice_keeps_single_link()
    {
        var book = new Book("Title", "978-0-00-000000-0", 10m, 1);
        book.AddToGenre(1);
        book.AddToGenre(1);
        Assert.Single(book.BookGenres);
    }
}

public class ValidatorTests
{
    [Fact]
    public void Empty_title_and_bad_values_produce_errors_per_field()
    {
        var result = new CreateBookDtoValidator().Validate(new CreateBookDto { Title = "", Isbn = "bad", Price = -1, AuthorId = 0 });
        var errors = result.ToDictionary();
        Assert.Contains("Title", errors.Keys);
        Assert.Contains("Isbn", errors.Keys);
        Assert.Contains("Price", errors.Keys);
        Assert.Contains("AuthorId", errors.Keys);
    }

    [Fact]
    public void Query_rejects_unknown_sort_field_and_inverted_price_range()
    {
        var result = new BookQueryValidator().Validate(new BookQuery { SortBy = "hack", MinPrice = 10, MaxPrice = 5 });
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }
}

public class SpecificationTests
{
    private static readonly List<Book> Books = Enumerable.Range(1, 12)
        .Select(i => new Book($"Book {i:00}", $"978-0-00-0000{i:00}-0", i * 10m, i % 2 == 0 ? 1 : 2))
        .ToList();

    [Fact]
    public void Filter_sort_and_paging_are_described_by_specification()
    {
        var spec = new BooksFilterSpec(new BookQuery { Page = 2, PageSize = 3, MinPrice = 20, SortBy = "price", SortDir = "desc" });

        var page = spec.Evaluate(Books).ToList();                 // та сама специфікація без EF — в пам'яті

        Assert.Equal(new[] { 90m, 80m, 70m }, page.Select(b => b.Price));
    }

    [Fact]
    public void Second_specification_uses_other_criteria()
    {
        var spec = new PublishedBooksByAuthorSpec(authorId: 1);
        Assert.Empty(spec.Evaluate(Books));                         // жодна книга ще не опублікована
    }
}

public class MappingTests
{
    [Fact]
    public void AutoMapper_configuration_is_valid()
        => new MapperConfiguration(cfg => cfg.AddMaps(typeof(CatalogProfile).Assembly), NullLoggerFactory.Instance)
            .AssertConfigurationIsValid();
}
