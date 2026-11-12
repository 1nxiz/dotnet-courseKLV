using AutoMapper;
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

    public GenreService(IUnitOfWork uow, IMapper mapper)
    {
        _uow = uow;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken ct)
        => _mapper.Map<List<GenreDto>>(await _uow.Genres.GetAllAsync(ct));

    public async Task<GenreWithBooksDto> GetWithBooksAsync(int id, CancellationToken ct)
    {
        var genre = await _uow.Genres.GetWithBooksAsync(id, ct)            // explicit loading: 2 SQL-запити
                    ?? throw new EntityNotFoundException($"Genre {id} not found");
        return _mapper.Map<GenreWithBooksDto>(genre);
    }

    public async Task<IReadOnlyList<BookDto>> GetRecentlyAddedAsync(string slug, DateTime since, CancellationToken ct)
        => _mapper.Map<List<BookDto>>(await _uow.BookGenres.GetRecentlyAddedAsync(slug, since, ct));

    public async Task<IReadOnlyList<GenreStatisticsDto>> GetStatisticsAsync(CancellationToken ct)
        => (await _uow.BookGenres.GetGenreStatisticsAsync(ct))
            .Select(x => new GenreStatisticsDto(x.Genre.Id, x.Genre.Name, x.BooksCount))
            .ToList();
}

public class AuthorService : IAuthorService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<AuthorService> _logger;

    public AuthorService(IUnitOfWork uow, IMapper mapper, ILogger<AuthorService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AuthorDto>> GetAllAsync(CancellationToken ct)
        => _mapper.Map<List<AuthorDto>>(await _uow.Authors.GetAllAsync(ct));

    public async Task<AuthorDto> GetByIdAsync(int id, CancellationToken ct)
        => _mapper.Map<AuthorDto>(await _uow.Authors.GetByIdAsync(id, ct)
                                  ?? throw new EntityNotFoundException($"Author {id} not found"));

    public async Task<AuthorDto> CreateAsync(CreateAuthorDto input, CancellationToken ct)
    {
        var author = new Author(input.FullName, input.Country, input.BirthYear);
        await _uow.Authors.AddAsync(author, ct);
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Author {AuthorId} created: {FullName}", author.Id, author.FullName);
        return _mapper.Map<AuthorDto>(author);
    }
}
