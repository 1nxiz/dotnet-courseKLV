namespace Loans.Domain.Models;

/// <summary>
/// Позиція видачі — проміжна таблиця M:N Loans &lt;-&gt; Books (dbo.LoanItems).
/// BookTitle і UnitPrice — ЗНІМОК на момент оформлення (не оновлюється, політика дублювання Лаб. 01).
/// </summary>
public class LoanItem
{
    public long LoanId { get; set; }
    public long BookId { get; set; }
    public string BookTitle { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public int Quantity { get; set; }
}
