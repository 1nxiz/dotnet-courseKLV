namespace Loans.Domain.Models;

/// <summary>Агрегат «Видача» (спрощений аналог Order з eShop): Loan + LoanDetails + LoanItems.</summary>
public class Loan : AuditableEntity
{
    public string Number { get; set; } = "";
    public long ReaderId { get; set; }
    public string Status { get; set; } = Constants.LoanStatus.Submitted;
    public DateTime LoanDate { get; set; }
    public DateTime? DueDate { get; set; }

    public LoanDetails? Details { get; set; }
    public List<LoanItem> Items { get; set; } = new();

    public decimal Total => Items.Sum(i => i.Quantity * (i.UnitPrice - i.Discount));
}
