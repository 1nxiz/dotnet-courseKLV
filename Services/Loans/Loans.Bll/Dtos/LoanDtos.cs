using System.ComponentModel.DataAnnotations;

namespace Loans.Bll.Dtos;

public record LoanItemDto(long BookId, string BookTitle, decimal UnitPrice, decimal Discount, int Quantity, decimal LineTotal);

public record LoanDetailsDto(string DeliveryType, string? DeliveryAddress, string? Comment);

/// <summary>Картка видачі. RowVersion (Base64) передається назад при оновленні деталей.</summary>
public record LoanDto(
    long Id,
    string Number,
    long ReaderId,
    string Status,
    DateTime LoanDate,
    DateTime? DueDate,
    decimal Total,
    LoanDetailsDto? Details,
    IReadOnlyList<LoanItemDto> Items,
    string RowVersion);

public record LoanSummaryDto(long Id, string Number, long ReaderId, string ReaderName, string Status,
    DateTime LoanDate, DateTime? DueDate, int ItemsCount, decimal TotalAmount);

public record PaymentDto(long Id, long LoanId, decimal Amount, string Method, string? ExternalReference, DateTime PaidAt);

public record RowVersionDto(string RowVersion);

/// <summary>Параметри списку видач (query string).</summary>
public class LoanQuery
{
    public long? ReaderId { get; set; }

    [RegularExpression("^(Submitted|AwaitingValidation|Reserved|Paid|Issued|Returned|Cancelled)$")]
    public string? Status { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}

public class CreateLoanDto
{
    [Range(1, long.MaxValue)]
    public long ReaderId { get; set; }

    [Required, MinLength(1)]
    public List<CreateLoanItemDto> Items { get; set; } = new();

    [RegularExpression("^(Pickup|Courier)$")]
    public string DeliveryType { get; set; } = "Pickup";

    [StringLength(400)]
    public string? DeliveryAddress { get; set; }

    [StringLength(1000)]
    public string? Comment { get; set; }

    /// <summary>Дата повернення (не раніше сьогодні). Якщо не вказано — +14 днів.</summary>
    public DateTime? DueDate { get; set; }
}

public class CreateLoanItemDto
{
    [Range(1, long.MaxValue)]
    public long BookId { get; set; }

    [Range(1, 50)]
    public int Quantity { get; set; } = 1;
}

public class UpdateLoanDetailsDto
{
    [Required, RegularExpression("^(Pickup|Courier)$")]
    public string DeliveryType { get; set; } = "Pickup";

    [StringLength(400)]
    public string? DeliveryAddress { get; set; }

    [StringLength(1000)]
    public string? Comment { get; set; }

    [Required]
    public string RowVersion { get; set; } = "";
}

public class PayLoanDto
{
    [Range(typeof(decimal), "0.01", "1000000")]
    public decimal Amount { get; set; }

    [Required, RegularExpression("^(Card|Cash|Online)$")]
    public string Method { get; set; } = "Card";
}
