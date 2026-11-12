using Loans.Domain.Common;
using Loans.Domain.Models;

namespace Loans.Dal.Abstractions;

/// <summary>Репозиторій агрегату «Видача» (Dapper).</summary>
public interface ILoanRepository : IGenericRepository<Loan>
{
    /// <summary>Видача з позиціями одним запитом (Dapper multi-mapping, LEFT JOIN).</summary>
    Task<Loan?> GetWithItemsAsync(long loanId, CancellationToken ct = default);

    /// <summary>Картка видачі через процедуру usp_GetLoanById (QueryMultipleAsync: заголовок + деталі, позиції).</summary>
    Task<Loan?> GetCardAsync(long loanId, CancellationToken ct = default);

    /// <summary>Сторінка списку видач із фільтрами (представлення vw_LoanSummary, OFFSET/FETCH).</summary>
    Task<PagedResult<LoanSummary>> GetPageAsync(long? readerId, string? status, int page, int pageSize,
        CancellationToken ct = default);

    /// <summary>usp_ConfirmLoan: Submitted|AwaitingValidation -> Reserved.</summary>
    Task ConfirmAsync(long loanId, string? changedBy, CancellationToken ct = default);

    /// <summary>usp_ChangeLoanStatus: транзакційна зміна статусу з перевіркою допустимого переходу.</summary>
    Task ChangeStatusAsync(long loanId, string newStatus, string? changedBy, CancellationToken ct = default);

    /// <summary>usp_UpdateLoanDetails з оптимістичною конкурентністю. Повертає новий RowVersion.</summary>
    Task<byte[]> UpdateDetailsAsync(LoanDetails details, byte[] expectedRowVersion, string? updatedBy,
        CancellationToken ct = default);

    /// <summary>usp_RegisterPayment: платіж + Reserved -> Paid; ідемпотентна за ExternalReference.</summary>
    Task<long> RegisterPaymentAsync(Payment payment, string? createdBy, CancellationToken ct = default);

    Task<IReadOnlyList<Payment>> GetPaymentsAsync(long loanId, CancellationToken ct = default);
}
