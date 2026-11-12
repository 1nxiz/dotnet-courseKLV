namespace Platform.Aggregator.Models;

// Внутрішні моделі відповідей мікросервісів — однакові для gRPC- і HTTP-клієнтів, тож композиція не залежить від транспорту.

public record LoanItemView(long BookId, string BookTitle, decimal UnitPrice, decimal Discount, int Quantity);

public record LoanView(long Id, string Number, long ReaderId, string Status, DateTime LoanDate, DateTime? DueDate,
    decimal Total, IReadOnlyList<LoanItemView> Items);

public record BookView(int Id, string Title, string Isbn, decimal Price, int TotalCopies, bool IsPublished, string? AuthorName);

public record ReviewView(string Id, long BookId, string AuthorName, int Rating, string Text, DateTime CreatedAt);

/// <summary>Підсумок відгуків книги: кількість, середня оцінка, останні відгуки.</summary>
public record BookReviewsView(long BookId, long ReviewsCount, double AverageRating, IReadOnlyList<ReviewView> Latest);

/// <summary>Сторінка відгуків REST-API Feedback (лише для HTTP-клієнта).</summary>
public record ReviewsPage(IReadOnlyList<ReviewView> Items, long TotalCount);

/// <summary>Транспорт до мікросервісів: gRPC (типово, Лаб. 06) або HTTP REST (Лаб. 05, для порівняння).</summary>
public enum UpstreamTransport { Grpc, Http }
