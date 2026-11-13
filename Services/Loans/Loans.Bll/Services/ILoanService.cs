using Loans.Bll.Dtos;
using Loans.Domain.Common;

namespace Loans.Bll.Services;

/// <summary>Бізнес-операції з видачами. Контролери лише викликають ці методи — рішень не приймають.</summary>
public interface ILoanService
{
    Task<PagedResult<LoanSummaryDto>> GetPageAsync(LoanQuery query, CancellationToken ct);
    Task<LoanDto> GetByIdAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<PaymentDto>> GetPaymentsAsync(long id, CancellationToken ct);
    Task<LoanDto> CreateAsync(CreateLoanDto input, string? user, CancellationToken ct);
    Task<RowVersionDto> UpdateDetailsAsync(long id, UpdateLoanDetailsDto input, string? user, CancellationToken ct);

    /// <summary>Підтвердити видачу: статус -> Reserved і резервування примірників в ОДНІЙ транзакції.</summary>
    Task ConfirmAsync(long id, string? user, CancellationToken ct);

    /// <summary>Оплатити (ідемпотентно за ключем Idempotency-Key).</summary>
    Task<PaymentDto> PayAsync(long id, PayLoanDto input, string? idempotencyKey, string? user, CancellationToken ct);

    Task IssueAsync(long id, string? user, CancellationToken ct);

    /// <summary>Повернення: статус -> Returned і примірники назад на полицю в одній транзакції.</summary>
    Task ReturnAsync(long id, string? user, CancellationToken ct);

    /// <summary>Скасування (лише до оплати); зарезервовані примірники повертаються на полицю.</summary>
    Task CancelAsync(long id, string? user, CancellationToken ct);

    Task DeleteAsync(long id, CancellationToken ct);
}
