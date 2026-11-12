using System.Data;
using System.Diagnostics;
using AutoMapper;
using Loans.Bll.Diagnostics;
using Loans.Bll.Dtos;
using Loans.Dal.Abstractions;
using Loans.Domain.Common;
using Loans.Domain.Constants;
using Loans.Domain.Exceptions;
using Loans.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Loans.Bll.Services;

public sealed class LoanService : ILoanService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<LoanService> _logger;

    public LoanService(IUnitOfWork uow, IMapper mapper, ILogger<LoanService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<PagedResult<LoanSummaryDto>> GetPageAsync(LoanQuery query, CancellationToken ct)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var page = Math.Max(query.Page, 1);
        var result = await _uow.Loans.GetPageAsync(query.ReaderId, query.Status, page, pageSize, ct);
        return new PagedResult<LoanSummaryDto>(
            _mapper.Map<List<LoanSummaryDto>>(result.Items), result.TotalCount, result.Page, result.PageSize);
    }

    public async Task<LoanDto> GetByIdAsync(long id, CancellationToken ct)
    {
        var loan = await _uow.Loans.GetCardAsync(id, ct)
                   ?? throw new NotFoundException($"Loan {id} not found");
        return _mapper.Map<LoanDto>(loan);
    }

    public async Task<IReadOnlyList<PaymentDto>> GetPaymentsAsync(long id, CancellationToken ct)
    {
        _ = await _uow.Loans.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Loan {id} not found");
        return _mapper.Map<List<PaymentDto>>(await _uow.Loans.GetPaymentsAsync(id, ct));
    }

    public async Task<LoanDto> CreateAsync(CreateLoanDto input, string? user, CancellationToken ct)
    {
        using var activity = LoansDiagnostics.ActivitySource.StartActivity("CreateLoan");
        activity?.SetTag("reader.id", input.ReaderId);
        activity?.SetTag("loan.items_count", input.Items.Count);
        // --- бізнес-валідація: правила, для яких потрібен стан БД або кілька полів одразу ---
        var errors = new Dictionary<string, string[]>();
        if (input.Items.GroupBy(i => i.BookId).Any(g => g.Count() > 1))
            errors["items"] = ["The same book must not appear twice in one loan"];
        if (input.DeliveryType == DeliveryType.Courier && string.IsNullOrWhiteSpace(input.DeliveryAddress))
            errors["deliveryAddress"] = ["Delivery address is required for courier delivery"];
        var dueDate = (input.DueDate ?? DateTime.UtcNow.AddDays(14)).Date;
        if (dueDate < DateTime.UtcNow.Date)
            errors["dueDate"] = ["Due date must not be in the past"];

        if (!await _uow.Readers.ExistsAsync(input.ReaderId, ct))
            errors["readerId"] = [$"Reader {input.ReaderId} not found"];

        var books = await _uow.Books.GetByIdsAsync(input.Items.Select(i => i.BookId), ct);
        var missing = input.Items.Select(i => i.BookId).Except(books.Select(b => b.Id)).ToList();
        if (missing.Count > 0)
            errors["items"] = [$"Books not found: {string.Join(", ", missing)}"];

        if (errors.Count > 0)
            throw new ValidationException("Loan cannot be created", errors);

        // --- знімок назви й ціни з локального довідника (політика дублювання Лаб. 01) ---
        var booksById = books.ToDictionary(b => b.Id);
        var loan = new Loan
        {
            ReaderId = input.ReaderId,
            DueDate = dueDate,
            CreatedBy = user,
            Details = new LoanDetails
            {
                DeliveryType = input.DeliveryType,
                DeliveryAddress = input.DeliveryAddress,
                Comment = input.Comment
            },
            Items = input.Items.Select(i => new LoanItem
            {
                BookId = i.BookId,
                BookTitle = booksById[i.BookId].Title,      // знімок — не оновлюватиметься
                UnitPrice = booksById[i.BookId].Price,      // знімок ціни на момент оформлення
                Quantity = i.Quantity
            }).ToList()
        };

        await _uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await _uow.Loans.AddAsync(loan, ct);             // Loans + LoanDetails + LoanItems + історія
            await _uow.CommitAsync(ct);
        }
        catch
        {
            await _uow.RollbackAsync(ct);
            throw;
        }

        activity?.SetTag("loan.id", loan.Id);
        _logger.LogInformation("Loan {LoanId} ({LoanNumber}) created for reader {ReaderId}: {ItemsCount} items, total {Total}",
            loan.Id, loan.Number, loan.ReaderId, loan.Items.Count, loan.Total);
        return await GetByIdAsync(loan.Id, ct);
    }

    public async Task<RowVersionDto> UpdateDetailsAsync(long id, UpdateLoanDetailsDto input, string? user, CancellationToken ct)
    {
        if (input.DeliveryType == DeliveryType.Courier && string.IsNullOrWhiteSpace(input.DeliveryAddress))
            throw new ValidationException("Invalid delivery details",
                new Dictionary<string, string[]> { ["deliveryAddress"] = ["Delivery address is required for courier delivery"] });

        var expected = ParseRowVersion(input.RowVersion);
        var newVersion = await _uow.Loans.UpdateDetailsAsync(new LoanDetails
        {
            LoanId = id,
            DeliveryType = input.DeliveryType,
            DeliveryAddress = input.DeliveryAddress,
            Comment = input.Comment
        }, expected, user, ct);                               // 50003 -> ConcurrencyException -> 409

        _logger.LogInformation("Loan {LoanId} delivery details updated to {DeliveryType}", id, input.DeliveryType);
        return new RowVersionDto(Convert.ToBase64String(newVersion));
    }

    public async Task ConfirmAsync(long id, string? user, CancellationToken ct)
    {
        using var activity = LoansDiagnostics.ActivitySource.StartActivity("ConfirmLoan");
        activity?.SetTag("loan.id", id);
        await _uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var loan = await _uow.Loans.GetWithItemsAsync(id, ct)
                       ?? throw new NotFoundException($"Loan {id} not found");

            if (loan.Items.Count == 0)
                throw new BusinessConflictException($"Loan {id} has no items");

            // 1) статус -> Reserved; допустимість переходу перевіряє процедура usp_ConfirmLoan (THROW 50002)
            await _uow.Loans.ConfirmAsync(id, user, ct);

            // 2) резервування примірників; нестача -> BusinessConflictException -> відкат кроку 1
            foreach (var item in loan.Items)
                await _uow.Books.ReserveCopiesAsync(item.BookId, item.Quantity, ct);

            await _uow.CommitAsync(ct);
            _logger.LogInformation("Loan {LoanId} confirmed, {ItemsCount} items reserved, status {Status}",
                id, loan.Items.Count, LoanStatus.Reserved);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await _uow.RollbackAsync(ct);                     // статус повертається назад
            throw;
        }
    }

    public async Task<PaymentDto> PayAsync(long id, PayLoanDto input, string? idempotencyKey, string? user, CancellationToken ct)
    {
        using var activity = LoansDiagnostics.ActivitySource.StartActivity("PayLoan");
        activity?.SetTag("loan.id", id);
        activity?.SetTag("payment.method", input.Method);
        var paymentId = await _uow.Loans.RegisterPaymentAsync(new Payment
        {
            LoanId = id,
            Amount = input.Amount,
            Method = input.Method,
            ExternalReference = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim()
        }, user, ct);                                         // процедура: платіж + Reserved -> Paid атомарно

        var payments = await _uow.Loans.GetPaymentsAsync(id, ct);
        var payment = payments.Single(p => p.Id == paymentId);
        _logger.LogInformation("Loan {LoanId} paid: payment {PaymentId}, amount {Amount}, method {Method}",
            id, paymentId, payment.Amount, payment.Method);
        return _mapper.Map<PaymentDto>(payment);
    }

    public async Task IssueAsync(long id, string? user, CancellationToken ct)
    {
        await _uow.Loans.ChangeStatusAsync(id, LoanStatus.Issued, user, ct);   // Paid -> Issued, інакше 409
        _logger.LogInformation("Loan {LoanId} issued, status {Status}", id, LoanStatus.Issued);
    }

    public async Task ReturnAsync(long id, string? user, CancellationToken ct)
    {
        await _uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var loan = await _uow.Loans.GetWithItemsAsync(id, ct)
                       ?? throw new NotFoundException($"Loan {id} not found");

            await _uow.Loans.ChangeStatusAsync(id, LoanStatus.Returned, user, ct);    // лише Issued -> Returned
            foreach (var item in loan.Items)
                await _uow.Books.ReturnCopiesAsync(item.BookId, item.Quantity, ct);

            await _uow.CommitAsync(ct);
            _logger.LogInformation("Loan {LoanId} returned, {ItemsCount} items back on shelf", id, loan.Items.Count);
        }
        catch
        {
            await _uow.RollbackAsync(ct);
            throw;
        }
    }

    public async Task CancelAsync(long id, string? user, CancellationToken ct)
    {
        await _uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var loan = await _uow.Loans.GetWithItemsAsync(id, ct)
                       ?? throw new NotFoundException($"Loan {id} not found");
            var copiesWereReserved = loan.Status == LoanStatus.Reserved;

            await _uow.Loans.ChangeStatusAsync(id, LoanStatus.Cancelled, user, ct);   // після оплати -> 409
            if (copiesWereReserved)
                foreach (var item in loan.Items)
                    await _uow.Books.ReturnCopiesAsync(item.BookId, item.Quantity, ct);

            await _uow.CommitAsync(ct);
            _logger.LogInformation("Loan {LoanId} cancelled (from {PreviousStatus})", id, loan.Status);
        }
        catch
        {
            await _uow.RollbackAsync(ct);
            throw;
        }
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        var loan = await _uow.Loans.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Loan {id} not found");
        if (loan.Status is not (LoanStatus.Cancelled or LoanStatus.Returned))
            throw new BusinessConflictException($"Only cancelled or returned loans can be deleted (current status {loan.Status})");

        await _uow.Loans.DeleteAsync(id, ct);                 // м'яке видалення
        _logger.LogInformation("Loan {LoanId} soft-deleted", id);
    }

    private static byte[] ParseRowVersion(string value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length == 8) return bytes;
        }
        catch (FormatException) { }
        throw new ValidationException("Invalid RowVersion",
            new Dictionary<string, string[]> { ["rowVersion"] = ["RowVersion must be the Base64 value returned by GET"] });
    }
}
