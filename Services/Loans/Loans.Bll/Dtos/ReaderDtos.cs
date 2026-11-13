using System.ComponentModel.DataAnnotations;

namespace Loans.Bll.Dtos;

/// <summary>Читач (вихідний контракт). RowVersion — Base64 для оптимістичної конкурентності.</summary>
public record ReaderDto(long Id, string Email, string FullName, string? Phone, string CardNumber, string RowVersion);

public class CreateReaderDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    [Required, StringLength(200, MinimumLength = 1)]
    public string FullName { get; set; } = "";

    [StringLength(30)]
    public string? Phone { get; set; }

    /// <summary>Номер читацького квитка, наприклад LIB-0042.</summary>
    [Required, RegularExpression(@"^LIB-\d{4,10}$", ErrorMessage = "CardNumber must look like LIB-0001")]
    public string CardNumber { get; set; } = "";
}

public class UpdateReaderDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    [Required, StringLength(200, MinimumLength = 1)]
    public string FullName { get; set; } = "";

    [StringLength(30)]
    public string? Phone { get; set; }

    /// <summary>RowVersion, отриманий разом із читачем (Base64).</summary>
    [Required]
    public string RowVersion { get; set; } = "";
}
