namespace Loans.Domain.Models;

/// <summary>
/// Локальна копія книги з Каталогу (таблиця dbo.Books у LoansDb). Id — той самий, що в BooksDb.
/// Title / Isbn / Price — копія для читання; AvailableCopies — власні дані контексту «Видачі».
/// </summary>
public class Book : AuditableEntity
{
    public string Title { get; set; } = "";
    public string? Isbn { get; set; }
    public decimal Price { get; set; }
    public int AvailableCopies { get; set; }
    public DateTime? SourceUpdatedAt { get; set; }
}
