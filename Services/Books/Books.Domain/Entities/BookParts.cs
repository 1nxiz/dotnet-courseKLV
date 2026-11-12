using Books.Domain.Exceptions;

namespace Books.Domain.Entities;

/// <summary>1:1 — розширений опис книги. PK = FK на Book.</summary>
public class BookDetail
{
    public int BookId { get; private set; }
    public string Description { get; private set; } = "";
    public string? Publisher { get; private set; }
    public int? Pages { get; private set; }
    public string Language { get; private set; } = "uk";

    private BookDetail() { }

    internal BookDetail(string description, string? publisher, int? pages, string language)
        => Update(description, publisher, pages, language);

    internal void Update(string description, string? publisher, int? pages, string language)
    {
        Description = description.Trim();
        Publisher = publisher;
        Pages = pages;
        Language = string.IsNullOrWhiteSpace(language) ? "uk" : language;
    }
}

/// <summary>1:N — зображення книги.</summary>
public class BookImage
{
    public int Id { get; private set; }
    public int BookId { get; private set; }
    public string Url { get; private set; } = "";
    public int SortOrder { get; private set; }

    private BookImage() { }

    internal BookImage(string url, int sortOrder)
    {
        Url = url;
        SortOrder = sortOrder;
    }
}

/// <summary>
/// Проміжна сутність M:N Book &lt;-&gt; Genre з ВЛАСНИМ полем AddedAt (коли книгу додано в жанрову добірку).
/// Автоматичне M:N EF Core такого поля не зберегло б.
/// </summary>
public class BookGenre
{
    public int BookId { get; private set; }
    public Book Book { get; private set; } = null!;
    public int GenreId { get; private set; }
    public Genre Genre { get; private set; } = null!;
    public DateTime AddedAt { get; private set; }

    private BookGenre() { }

    internal BookGenre(int genreId, DateTime addedAt)
    {
        GenreId = genreId;
        AddedAt = addedAt;
    }
}

/// <summary>Автор (1:N до Book).</summary>
public class Author
{
    private readonly List<Book> _books = new();

    public int Id { get; private set; }
    public string FullName { get; private set; } = "";
    public string? Country { get; private set; }
    public short? BirthYear { get; private set; }
    public IReadOnlyCollection<Book> Books => _books.AsReadOnly();

    private Author() { }

    public Author(string fullName, string? country = null, short? birthYear = null)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainException("Author name is required");
        FullName = fullName.Trim();
        Country = country;
        BirthYear = birthYear;
    }
}

/// <summary>Жанр — довідник з фіксованими Id (статичний сідінг HasData).</summary>
public class Genre
{
    private readonly List<BookGenre> _bookGenres = new();

    public int Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Slug { get; private set; } = "";
    public IReadOnlyCollection<BookGenre> BookGenres => _bookGenres.AsReadOnly();

    private Genre() { }

    public Genre(int id, string name, string slug)
    {
        Id = id;
        Name = name;
        Slug = slug;
    }
}
