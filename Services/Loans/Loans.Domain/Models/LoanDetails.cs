namespace Loans.Domain.Models;

/// <summary>Деталі видачі — зв'язок 1:1 з Loan (dbo.LoanDetails).</summary>
public class LoanDetails
{
    public long LoanId { get; set; }
    public string DeliveryType { get; set; } = Constants.DeliveryType.Pickup;
    public string? DeliveryAddress { get; set; }
    public string? Comment { get; set; }
}
