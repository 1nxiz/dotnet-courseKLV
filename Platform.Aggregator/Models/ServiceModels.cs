namespace Platform.Aggregator.Models;

// Моделі відповідей мікросервісів — лише поля, потрібні для агрегації (System.Text.Json, camelCase).

public record LoanItemView(long BookId, string BookTitle, decimal UnitPrice, decimal Discount, int Quantity);

public record LoanView(long Id, string Number, long ReaderId, string Status, DateTime LoanDate, DateTime? DueDate,
    decimal Total, IReadOnlyList<LoanItemView> Items);

public record BookView(int Id, string Title, string Isbn, decimal Price, int TotalCopies, bool IsPublished, string? AuthorName);

public record ReviewView(string Id, long BookId, string AuthorName, int Rating, string Text, DateTime CreatedAt);

public record ReviewsPage(IReadOnlyList<ReviewView> Items, long TotalCount);
