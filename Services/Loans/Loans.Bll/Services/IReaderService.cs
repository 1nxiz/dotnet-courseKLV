using Loans.Bll.Dtos;

namespace Loans.Bll.Services;

public interface IReaderService
{
    Task<IReadOnlyList<ReaderDto>> GetAllAsync(CancellationToken ct);
    Task<ReaderDto> GetByIdAsync(long id, CancellationToken ct);
    Task<ReaderDto> CreateAsync(CreateReaderDto input, string? user, CancellationToken ct);
    Task<ReaderDto> UpdateAsync(long id, UpdateReaderDto input, string? user, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
}
