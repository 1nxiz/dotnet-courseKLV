using AutoMapper;
using Loans.Bll.Dtos;
using Loans.Domain.Models;

namespace Loans.Bll.Mapping;

/// <summary>Усі профілі мапінгу зберігаються в BLL і реєструються централізовано (AddAutoMapper за збіркою BLL).</summary>
public class LoanProfile : Profile
{
    public LoanProfile()
    {
        CreateMap<LoanItem, LoanItemDto>()
            .ForCtorParam(nameof(LoanItemDto.LineTotal), o => o.MapFrom(s => s.Quantity * (s.UnitPrice - s.Discount)));
        CreateMap<LoanDetails, LoanDetailsDto>();
        CreateMap<Loan, LoanDto>()
            .ForCtorParam(nameof(LoanDto.RowVersion), o => o.MapFrom(s => Convert.ToBase64String(s.RowVersion)));
        CreateMap<LoanSummary, LoanSummaryDto>();
        CreateMap<Payment, PaymentDto>();
    }
}

public class ReaderProfile : Profile
{
    public ReaderProfile()
    {
        CreateMap<Reader, ReaderDto>()
            .ForCtorParam(nameof(ReaderDto.RowVersion), o => o.MapFrom(s => Convert.ToBase64String(s.RowVersion)));
    }
}

public class BookCopyProfile : Profile
{
    public BookCopyProfile()
    {
        CreateMap<Book, BookCopyDto>();
    }
}
