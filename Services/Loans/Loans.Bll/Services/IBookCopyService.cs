using Loans.Bll.Dtos;

namespace Loans.Bll.Services;

/// <summary>Локальна копія довідника книг у контексті «Видачі».</summary>
public interface IBookCopyService
{
    Task<IReadOnlyList<BookCopyDto>> GetAllAsync(CancellationToken ct);
    Task<BookCopyDto> GetByIdAsync(long id, CancellationToken ct);

    /// <summary>Створює копію книги, якщо її ще немає, інакше оновлює. Повертає (dto, created).</summary>
    Task<(BookCopyDto Book, bool Created)> UpsertAsync(UpsertBookCopyDto input, string? user, CancellationToken ct);

    Task DeleteAsync(long id, CancellationToken ct);
}
