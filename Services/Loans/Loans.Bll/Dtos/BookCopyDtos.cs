using System.ComponentModel.DataAnnotations;

namespace Loans.Bll.Dtos;

/// <summary>Книга з локальної копії довідника Каталогу + власна кількість примірників на полиці.</summary>
public record BookCopyDto(long Id, string Title, string? Isbn, decimal Price, int AvailableCopies);

/// <summary>Створення/оновлення локальної копії книги (до появи подій Каталогу в Лаб. 08 — вручну).</summary>
public class UpsertBookCopyDto
{
    [Range(1, long.MaxValue)]
    public long Id { get; set; }

    [Required, StringLength(300, MinimumLength = 1)]
    public string Title { get; set; } = "";

    [StringLength(20)]
    public string? Isbn { get; set; }

    [Range(typeof(decimal), "0.01", "100000")]
    public decimal Price { get; set; }

    [Range(0, 100000)]
    public int AvailableCopies { get; set; }
}
