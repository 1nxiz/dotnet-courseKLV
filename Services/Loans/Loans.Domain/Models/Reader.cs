namespace Loans.Domain.Models;

/// <summary>Читач бібліотеки (таблиця dbo.Readers).</summary>
public class Reader : AuditableEntity
{
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public string CardNumber { get; set; } = "";
}
