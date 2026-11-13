using System.Data;
using Dapper;
using Loans.Dal.Abstractions;
using Loans.Domain.Common;
using Loans.Domain.Constants;
using Loans.Domain.Models;
using Microsoft.Data.SqlClient;

namespace Loans.Dal.Dapper;

/// <summary>
/// Репозиторій агрегату «Видача» на ADO.NET + Dapper: multi-mapping, QueryMultiple,
/// виклики збережуваних процедур з Лаб. 01, вставка агрегату в транзакції Unit of Work.
/// </summary>
public sealed class LoanRepository : BaseDapperRepository<Loan>, ILoanRepository
{
    public LoanRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
        : base(connection, currentTransaction, "dbo.Loans") { }

    protected override string SelectColumns =>
        "Id, Number, ReaderId, Status, LoanDate, DueDate, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted, RowVersion";

    protected override string InsertSql => """
        INSERT INTO dbo.Loans (ReaderId, Status, DueDate, CreatedBy)
        OUTPUT INSERTED.Id
        VALUES (@ReaderId, @Status, @DueDate, @CreatedBy)
        """;

    /// <summary>
    /// Вставка всього агрегату: Loans + LoanDetails (1:1) + LoanItems (M:N, зі знімком назви/ціни) + історія.
    /// Виконується лише в транзакції Unit of Work — або все, або нічого.
    /// </summary>
    public override Task<long> AddAsync(Loan loan, CancellationToken ct = default) => GuardAsync(async () =>
    {
        if (Transaction is null)
            throw new InvalidOperationException("Loan aggregate must be saved inside a Unit of Work transaction");

        await EnsureOpenAsync(ct);

        var created = await Connection.QuerySingleAsync<CreatedLoanRow>(Command("""
            INSERT INTO dbo.Loans (ReaderId, Status, DueDate, CreatedBy)
            OUTPUT INSERTED.Id, INSERTED.Number, INSERTED.LoanDate
            VALUES (@ReaderId, @Status, @DueDate, @CreatedBy)
            """, new { loan.ReaderId, Status = LoanStatus.Submitted, loan.DueDate, loan.CreatedBy }, ct));

        loan.Id = created.Id;
        loan.Number = created.Number;
        loan.LoanDate = created.LoanDate;
        loan.Status = LoanStatus.Submitted;

        var details = loan.Details ?? new LoanDetails();
        details.LoanId = loan.Id;
        await Connection.ExecuteAsync(Command("""
            INSERT INTO dbo.LoanDetails (LoanId, DeliveryType, DeliveryAddress, Comment)
            VALUES (@LoanId, @DeliveryType, @DeliveryAddress, @Comment)
            """, new { details.LoanId, details.DeliveryType, details.DeliveryAddress, details.Comment }, ct));

        foreach (var item in loan.Items) item.LoanId = loan.Id;
        // Dapper виконує команду для кожного елемента колекції параметрів
        await Connection.ExecuteAsync(Command("""
            INSERT INTO dbo.LoanItems (LoanId, BookId, BookTitle, UnitPrice, Discount, Quantity)
            VALUES (@LoanId, @BookId, @BookTitle, @UnitPrice, @Discount, @Quantity)
            """, loan.Items.Select(i => new { i.LoanId, i.BookId, i.BookTitle, i.UnitPrice, i.Discount, i.Quantity }).ToList(), ct));

        await Connection.ExecuteAsync(Command("""
            INSERT INTO dbo.LoanStatusHistory (LoanId, FromStatus, ToStatus, ChangedBy)
            VALUES (@LoanId, NULL, @ToStatus, @ChangedBy)
            """, new { LoanId = loan.Id, ToStatus = LoanStatus.Submitted, ChangedBy = loan.CreatedBy }, ct));

        return loan.Id;
    });

    /// <summary>Видача з позиціями одним запитом (multi-mapping): кожен рядок JOIN = Loan + одна позиція.</summary>
    public Task<Loan?> GetWithItemsAsync(long loanId, CancellationToken ct = default) => GuardAsync(async () =>
    {
        const string sql = """
            SELECT l.Id, l.Number, l.ReaderId, l.Status, l.LoanDate, l.DueDate, l.RowVersion,
                   i.LoanId, i.BookId, i.BookTitle, i.UnitPrice, i.Discount, i.Quantity
            FROM dbo.Loans l
            LEFT JOIN dbo.LoanItems i ON i.LoanId = l.Id
            WHERE l.Id = @LoanId AND l.IsDeleted = 0
            """;

        await EnsureOpenAsync(ct);
        Loan? result = null;
        await Connection.QueryAsync<Loan, LoanItem?, Loan>(
            Command(sql, new { LoanId = loanId }, ct),
            (loan, item) =>
            {
                result ??= loan;
                if (item is not null) result.Items.Add(item);
                return result;
            },
            splitOn: "LoanId");                         // з цього стовпця починається LoanItem
        return result;
    });

    /// <summary>Картка видачі через usp_GetLoanById: два набори результатів читаються QueryMultipleAsync.</summary>
    public Task<Loan?> GetCardAsync(long loanId, CancellationToken ct = default) => GuardAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        using var grid = await Connection.QueryMultipleAsync(
            Command("dbo.usp_GetLoanById", new { LoanId = loanId }, ct, CommandType.StoredProcedure));

        var header = await grid.ReadSingleOrDefaultAsync<LoanHeaderRow>();
        if (header is null) return null;

        var items = (await grid.ReadAsync<LoanItem>()).AsList();
        return new Loan
        {
            Id = header.Id,
            Number = header.Number,
            ReaderId = header.ReaderId,
            Status = header.Status,
            LoanDate = header.LoanDate,
            DueDate = header.DueDate,
            RowVersion = header.RowVersion,
            Details = header.DeliveryType is null ? null : new LoanDetails
            {
                LoanId = header.Id,
                DeliveryType = header.DeliveryType,
                DeliveryAddress = header.DeliveryAddress,
                Comment = header.Comment
            },
            Items = items
        };
    });

    public Task<PagedResult<LoanSummary>> GetPageAsync(long? readerId, string? status, int page, int pageSize,
        CancellationToken ct = default) => GuardAsync(async () =>
    {
        const string sql = """
            SELECT COUNT(*) FROM dbo.vw_LoanSummary
            WHERE (@ReaderId IS NULL OR ReaderId = @ReaderId) AND (@Status IS NULL OR Status = @Status);

            SELECT Id, Number, ReaderId, ReaderName, Status, LoanDate, DueDate, ItemsCount, TotalAmount
            FROM dbo.vw_LoanSummary
            WHERE (@ReaderId IS NULL OR ReaderId = @ReaderId) AND (@Status IS NULL OR Status = @Status)
            ORDER BY LoanDate DESC, Id DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;

        await EnsureOpenAsync(ct);
        using var grid = await Connection.QueryMultipleAsync(Command(sql, new
        {
            ReaderId = readerId,
            Status = status,
            Skip = (page - 1) * pageSize,
            Take = pageSize
        }, ct));

        var total = await grid.ReadSingleAsync<int>();
        var items = (await grid.ReadAsync<LoanSummary>()).AsList();
        return new PagedResult<LoanSummary>(items, total, page, pageSize);
    });

    public Task ConfirmAsync(long loanId, string? changedBy, CancellationToken ct = default) => GuardAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        await Connection.ExecuteAsync(Command("dbo.usp_ConfirmLoan",
            new { LoanId = loanId, ChangedBy = changedBy }, ct, CommandType.StoredProcedure));
    });

    public Task ChangeStatusAsync(long loanId, string newStatus, string? changedBy, CancellationToken ct = default) =>
        GuardAsync(async () =>
        {
            await EnsureOpenAsync(ct);
            await Connection.ExecuteAsync(Command("dbo.usp_ChangeLoanStatus",
                new { LoanId = loanId, NewStatus = newStatus, ChangedBy = changedBy }, ct, CommandType.StoredProcedure));
        });

    public Task<byte[]> UpdateDetailsAsync(LoanDetails details, byte[] expectedRowVersion, string? updatedBy,
        CancellationToken ct = default) => GuardAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        return await Connection.QuerySingleAsync<byte[]>(Command("dbo.usp_UpdateLoanDetails", new
        {
            details.LoanId,
            details.DeliveryType,
            details.DeliveryAddress,
            details.Comment,
            ExpectedRowVersion = expectedRowVersion,
            UpdatedBy = updatedBy
        }, ct, CommandType.StoredProcedure));
    });

    public Task<long> RegisterPaymentAsync(Payment payment, string? createdBy, CancellationToken ct = default) =>
        GuardAsync(async () =>
        {
            var parameters = new DynamicParameters(new
            {
                payment.LoanId,
                payment.Amount,
                payment.Method,
                payment.ExternalReference,
                CreatedBy = createdBy
            });
            parameters.Add("PaymentId", dbType: DbType.Int64, direction: ParameterDirection.Output);

            await EnsureOpenAsync(ct);
            await Connection.ExecuteAsync(Command("dbo.usp_RegisterPayment", parameters, ct, CommandType.StoredProcedure));
            return parameters.Get<long>("PaymentId");
        });

    public Task<IReadOnlyList<Payment>> GetPaymentsAsync(long loanId, CancellationToken ct = default) => GuardAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        var rows = await Connection.QueryAsync<Payment>(Command("""
            SELECT Id, LoanId, Amount, Method, ExternalReference, PaidAt
            FROM dbo.Payments WHERE LoanId = @LoanId ORDER BY PaidAt
            """, new { LoanId = loanId }, ct));
        return (IReadOnlyList<Payment>)rows.AsList();
    });

    internal sealed class CreatedLoanRow
    {
        public long Id { get; set; }
        public string Number { get; set; } = "";
        public DateTime LoanDate { get; set; }
    }

    internal sealed class LoanHeaderRow
    {
        public long Id { get; set; }
        public string Number { get; set; } = "";
        public long ReaderId { get; set; }
        public string Status { get; set; } = "";
        public DateTime LoanDate { get; set; }
        public DateTime? DueDate { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public string? DeliveryType { get; set; }
        public string? DeliveryAddress { get; set; }
        public string? Comment { get; set; }
    }
}
