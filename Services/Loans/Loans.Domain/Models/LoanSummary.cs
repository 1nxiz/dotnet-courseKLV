namespace Loans.Domain.Models;

/// <summary>Рядок денормалізованого представлення dbo.vw_LoanSummary (список видач).</summary>
public class LoanSummary
{
    public long Id { get; set; }
    public string Number { get; set; } = "";
    public long ReaderId { get; set; }
    public string ReaderName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime LoanDate { get; set; }
    public DateTime? DueDate { get; set; }
    public int ItemsCount { get; set; }
    public decimal TotalAmount { get; set; }
}
