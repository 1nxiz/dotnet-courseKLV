namespace Loans.Domain.Models;

/// <summary>Аудитні колонки, м'яке видалення і токен оптимістичної конкурентності (Лаб. 01, крок 6).</summary>
public abstract class AuditableEntity
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>SQL Server ROWVERSION (8 байт); змінюється при кожному UPDATE рядка.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
