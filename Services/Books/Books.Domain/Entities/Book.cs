using Books.Domain.Exceptions;

namespace Books.Domain.Entities;

/// <summary>
/// Книга каталогу (аналог CatalogItem з eShop). Бізнес-правила реалізовано МЕТОДАМИ сутності, а не
/// відкритими сетерами: ціна задається в конструкторі й перевіряється, кількість примірників
/// змінюється лише методами AddCopies / WriteOffCopies.
/// </summary>
public class Book
{
    private readonly List<BookImage> _images = new();
    private readonly List<BookGenre> _bookGenres = new();

    public int Id { get; private set; }
    public string Title { get; private set; } = "";
    public string Isbn { get; private set; } = "";
    public decimal Price { get; private set; }
    public int TotalCopies { get; private set; }
    public short? PublishedYear { get; private set; }
    public bool IsPublished { get; private set; }
    /// <summary>Скільки примірників видано читачам за весь час — оновлюється подіями сервісу видач (Лаб. 08).</summary>
    public int TimesLoaned { get; private set; }
    public int AuthorId { get; private set; }
    public Author? Author { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public BookDetail? Detail { get; private set; }                                    // 1:1
    public IReadOnlyCollection<BookImage> Images => _images.AsReadOnly();              // 1:N
    public IReadOnlyCollection<BookGenre> BookGenres => _bookGenres.AsReadOnly();      // M:N через проміжну сутність

    private Book() { }                                                                 // для EF Core

    public Book(string title, string isbn, decimal price, int authorId, short? publishedYear = null)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Title is required");
        if (string.IsNullOrWhiteSpace(isbn)) throw new DomainException("ISBN is required");
        if (price <= 0) throw new DomainException("Price must be positive");
        if (authorId <= 0) throw new DomainException("Author is required");

        Title = title.Trim();
        Isbn = isbn.Trim();
        Price = price;
        AuthorId = authorId;
        PublishedYear = publishedYear;
    }

    /// <summary>Змінює ціну; повертає попередню (для події BookPriceChanged у Лаб. 08).</summary>
    public decimal ChangePrice(decimal newPrice)
    {
        if (newPrice <= 0) throw new DomainException("Price must be positive");
        var old = Price;
        Price = newPrice;
        return old;
    }

    public void Rename(string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle)) throw new DomainException("Title is required");
        Title = newTitle.Trim();
    }

    public void ChangePublishedYear(short? year) => PublishedYear = year;

    public void ChangeAuthor(int authorId)
    {
        if (authorId <= 0) throw new DomainException("Author is required");
        AuthorId = authorId;
    }

    /// <summary>Надходження нових примірників у фонд.</summary>
    public void AddCopies(int quantity)
    {
        if (quantity <= 0) throw new DomainException("Quantity must be positive");
        TotalCopies += quantity;
    }

    /// <summary>Списання примірників (зношені, втрачені). Не можна списати більше, ніж є.</summary>
    public void WriteOffCopies(int quantity)
    {
        if (quantity <= 0) throw new DomainException("Quantity must be positive");
        if (TotalCopies < quantity) throw new DomainException($"Only {TotalCopies} copies available");
        TotalCopies -= quantity;
    }

    /// <summary>Облік видачі (подія LoanStatusChanged зі статусом Issued від сервісу видач).</summary>
    public void RegisterLoan(int quantity)
    {
        if (quantity <= 0) throw new DomainException("Quantity must be positive");
        TimesLoaned += quantity;
    }

    public void Publish()
    {
        if (IsPublished) throw new DomainException("Book is already published");
        IsPublished = true;
    }

    public void SetDetail(string description, string? publisher, int? pages, string language = "uk")
    {
        if (string.IsNullOrWhiteSpace(description)) throw new DomainException("Description is required");
        if (pages is <= 0) throw new DomainException("Pages must be positive");
        if (Detail is null)
            Detail = new BookDetail(description, publisher, pages, language);
        else
            Detail.Update(description, publisher, pages, language);
    }

    public void AddImage(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new DomainException("Image URL is required");
        var nextOrder = _images.Count == 0 ? 0 : _images.Max(i => i.SortOrder) + 1;
        _images.Add(new BookImage(url, nextOrder));
    }

    public void AddToGenre(int genreId, DateTime? addedAt = null)
    {
        if (_bookGenres.Any(bg => bg.GenreId == genreId)) return;
        _bookGenres.Add(new BookGenre(genreId, addedAt ?? DateTime.UtcNow));
    }
}
