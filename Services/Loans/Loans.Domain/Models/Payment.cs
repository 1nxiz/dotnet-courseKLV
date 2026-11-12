namespace Loans.Domain.Models;

/// <summary>Платіж за видачу (dbo.Payments, 1:N до Loans).</summary>
public class Payment
{
    public long Id { get; set; }
    public long LoanId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = Constants.PaymentMethod.Card;
    public string? ExternalReference { get; set; }
    public DateTime PaidAt { get; set; }
}
