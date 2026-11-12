using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Library.Loans.Grpc;
using Loans.Bll.Dtos;
using Loans.Bll.Services;
using Loans.Domain.Exceptions;

namespace Loans.Api.Grpc;

/// <summary>
/// gRPC-адаптер над BLL сервісу видач: ті самі ILoanService / IReaderService (і той самий кеш), що й у REST-контролерів.
/// Доменні винятки перетворюються на RpcException з точним кодом, а не на загальний Internal.
/// </summary>
public sealed class LoansGrpcService : LoansService.LoansServiceBase
{
    private readonly ILoanService _loans;
    private readonly IReaderService _readers;

    public LoansGrpcService(ILoanService loans, IReaderService readers)
    {
        _loans = loans;
        _readers = readers;
    }

    public override Task<LoanReply> GetLoan(GetLoanRequest request, ServerCallContext context)
        => Execute(request.Id, async ct => ToReply(await _loans.GetByIdAsync(request.Id, ct)), context);

    public override Task<ReaderReply> GetReader(GetReaderRequest request, ServerCallContext context)
        => Execute(request.Id, async ct =>
        {
            var r = await _readers.GetByIdAsync(request.Id, ct);
            return new ReaderReply { Id = r.Id, FullName = r.FullName, CardNumber = r.CardNumber };
        }, context);

    private static async Task<T> Execute<T>(long id, Func<CancellationToken, Task<T>> action, ServerCallContext context)
    {
        if (id <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "id must be positive"));
        try
        {
            return await action(context.CancellationToken);
        }
        catch (NotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (DomainException ex)                                  // конфлікти стану, конкурентність
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
    }

    private static LoanReply ToReply(LoanDto loan)
    {
        var reply = new LoanReply
        {
            Id = loan.Id,
            Number = loan.Number,
            ReaderId = loan.ReaderId,
            Status = loan.Status,
            LoanDate = ToTimestamp(loan.LoanDate),
            DueDate = loan.DueDate is { } due ? ToTimestamp(due) : null,
            Total = loan.Total
        };
        reply.Items.AddRange(loan.Items.Select(i => new LoanItemReply
        {
            BookId = i.BookId,
            BookTitle = i.BookTitle,
            UnitPrice = i.UnitPrice,
            Discount = i.Discount,
            Quantity = i.Quantity
        }));
        return reply;
    }

    // SQL Server повертає DateTime з Kind = Unspecified; у БД зберігається UTC (SYSUTCDATETIME)
    private static Timestamp ToTimestamp(DateTime value) => Timestamp.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
