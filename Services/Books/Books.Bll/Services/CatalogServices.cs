using AutoMapper;
using Books.Bll.Caching;
using Books.Bll.Dtos;
using Books.Dal.Repositories;
using Books.Domain.Entities;
using Books.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Books.Bll.Services;

public class GenreService : IGenreService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly IBooksCache _cache;

    public GenreService(IUnitOfWork uow, IMapper mapper, IBooksCache cache)
    {
        _uow = uow;
        _mapper = mapper;
        _cache = cache;
    }

    /// <summary>Довідник жанрів (задається міграцією) — absolute expiration, прогрівається під час старту.</summary>
    public Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken ct)
        => _cache.GetOrCreateAsync<IReadOnlyList<GenreDto>>(CacheKeys.Genres,
            async token => _mapper.Map<List<GenreDto>>(await _uow.Genres.GetAllAsync(token)), CachePolicy.Reference, ct);

    public async Task<GenreWithBooksDto> GetWithBooksAsync(int id, CancellationToken ct)
    {
        var genre = await _uow.Genres.GetWithBooksAsync(id, ct)            // explicit loading: 2 SQL-запити
                    ?? throw new EntityNotFoundException($"Genre {id} not found");
        return _mapper.Map<GenreWithBooksDto>(genre);
    }

    public async Task<IReadOnlyList<BookDto>> GetRecentlyAddedAsync(string slug, DateTime since, CancellationToken ct)
        => (await _uow.BookGenres.GetRecentlyAddedAsync(slug, since, ct))
            .Select(x => _mapper.Map<BookDto>(x.Book) with { AuthorName = x.AuthorName })
            .ToList();

    public async Task<IReadOnlyList<GenreStatisticsDto>> GetStatisticsAsync(CancellationToken ct)
        => (await _uow.BookGenres.GetGenreStatisticsAsync(ct))
            .Select(x => new GenreStatisticsDto(x.Genre.Id, x.Genre.Name, x.BooksCount))
            .ToList();
}

public class AuthorService : IAuthorService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly IBooksCache _cache;
    private readonly ILogger<AuthorService> _logger;

    public AuthorService(IUnitOfWork uow, IMapper mapper, IBooksCache cache, ILogger<AuthorService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _cache = cache;
        _logger = logger;
    }

    public Task<IReadOnlyList<AuthorDto>> GetAllAsync(CancellationToken ct)
        => _cache.GetOrCreateAsync<IReadOnlyList<AuthorDto>>(CacheKeys.Authors,
            async token => _mapper.Map<List<AuthorDto>>(await _uow.Authors.GetAllAsync(token)), CachePolicy.Reference, ct);

    public async Task<AuthorDto> GetByIdAsync(int id, CancellationToken ct)
        => _mapper.Map<AuthorDto>(await _uow.Authors.GetByIdAsync(id, ct)
                                  ?? throw new EntityNotFoundException($"Author {id} not found"));

    public async Task<AuthorDto> CreateAsync(CreateAuthorDto input, CancellationToken ct)
    {
        var author = new Author(input.FullName, input.Country, input.BirthYear);
        await _uow.Authors.AddAsync(author, ct);
        await _uow.SaveChangesAsync(ct);
        await _cache.InvalidateAsync("AuthorCreated", ct, CacheKeys.Authors);
        _logger.LogInformation("Author {AuthorId} created: {FullName}", author.Id, author.FullName);
        return _mapper.Map<AuthorDto>(author);
    }
}
