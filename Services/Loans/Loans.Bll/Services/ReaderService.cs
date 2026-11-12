using AutoMapper;
using Loans.Bll.Dtos;
using Loans.Dal.Abstractions;
using Loans.Domain.Exceptions;
using Loans.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Loans.Bll.Services;

public sealed class ReaderService : IReaderService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<ReaderService> _logger;

    public ReaderService(IUnitOfWork uow, IMapper mapper, ILogger<ReaderService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReaderDto>> GetAllAsync(CancellationToken ct)
        => _mapper.Map<List<ReaderDto>>(await _uow.Readers.GetAllAsync(ct));

    public async Task<ReaderDto> GetByIdAsync(long id, CancellationToken ct)
    {
        var reader = await _uow.Readers.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Reader {id} not found");
        return _mapper.Map<ReaderDto>(reader);
    }

    public async Task<ReaderDto> CreateAsync(CreateReaderDto input, string? user, CancellationToken ct)
    {
        // бізнес-правило, для якого потрібен стан БД: email унікальний серед активних читачів
        if (await _uow.Readers.GetByEmailAsync(input.Email, ct) is not null)
            throw new BusinessConflictException($"Reader with email '{input.Email}' already exists");

        var reader = new Reader
        {
            Email = input.Email.Trim(),
            FullName = input.FullName.Trim(),
            Phone = input.Phone,
            CardNumber = input.CardNumber,
            CreatedBy = user
        };
        var id = await _uow.Readers.AddAsync(reader, ct);    // дубль CardNumber -> 2627 -> 409
        _logger.LogInformation("Reader {ReaderId} registered with card {CardNumber}", id, reader.CardNumber);
        return await GetByIdAsync(id, ct);
    }

    public async Task<ReaderDto> UpdateAsync(long id, UpdateReaderDto input, string? user, CancellationToken ct)
    {
        byte[] rowVersion;
        try { rowVersion = Convert.FromBase64String(input.RowVersion); }
        catch (FormatException)
        {
            throw new ValidationException("Invalid RowVersion",
                new Dictionary<string, string[]> { ["rowVersion"] = ["RowVersion must be the Base64 value returned by GET"] });
        }

        await _uow.Readers.UpdateAsync(new Reader
        {
            Id = id,
            Email = input.Email.Trim(),
            FullName = input.FullName.Trim(),
            Phone = input.Phone,
            UpdatedBy = user,
            RowVersion = rowVersion
        }, ct);                                               // чужа зміна -> ConcurrencyException -> 409
        _logger.LogInformation("Reader {ReaderId} updated", id);
        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        if (!await _uow.Readers.DeleteAsync(id, ct))
            throw new NotFoundException($"Reader {id} not found");
        _logger.LogInformation("Reader {ReaderId} soft-deleted", id);
    }
}
