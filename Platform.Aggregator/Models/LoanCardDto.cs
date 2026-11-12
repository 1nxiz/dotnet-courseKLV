namespace Platform.Aggregator.Models;

/// <summary>
/// Композитна відповідь під бізнес-сценарій «повна картка видачі»: видача (loans-api) + актуальні дані книг
/// (books-api) + свіжі відгуки на ці книги (feedback-api) + попередження про недоступні джерела.
/// null-значення не серіалізуються.
/// </summary>
public record LoanCardDto(
    long LoanId,
    string Number,
    string Status,
    long ReaderId,
    DateTime LoanDate,
    DateTime? DueDate,
    decimal Total,
    IReadOnlyList<LoanCardBookDto> Books,
    IReadOnlyList<string> Warnings);

/// <summary>Книга у видачі: знімок із позиції видачі + поточні дані каталогу + відгуки.</summary>
public record LoanCardBookDto(
    long BookId,
    string TitleAtLoan,
    decimal PriceAtLoan,
    int Quantity,
    CatalogInfoDto? Catalog,
    double? AverageRating,
    IReadOnlyList<ReviewView>? LatestReviews);

public record CatalogInfoDto(string Title, string? AuthorName, decimal CurrentPrice, int TotalCopies, bool IsPublished);
