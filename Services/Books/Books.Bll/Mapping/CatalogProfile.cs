using AutoMapper;
using Books.Bll.Dtos;
using Books.Domain.Entities;

namespace Books.Bll.Mapping;

/// <summary>Проєкції Entity -> DTO. Профілі реєструються централізовано (AddMaps за збіркою BLL).</summary>
public class CatalogProfile : Profile
{
    public CatalogProfile()
    {
        CreateMap<Book, BookDto>()
            .ForCtorParam(nameof(BookDto.AuthorName), o => o.MapFrom(s => s.Author != null ? s.Author.FullName : null));
        CreateMap<BookDetail, BookDetailDto>();
        CreateMap<BookImage, BookImageDto>();
        CreateMap<BookGenre, BookGenreDto>()
            .ForCtorParam(nameof(BookGenreDto.GenreName), o => o.MapFrom(s => s.Genre != null ? s.Genre.Name : ""));
        CreateMap<Book, BookCardDto>()
            .ForCtorParam(nameof(BookCardDto.AuthorName), o => o.MapFrom(s => s.Author != null ? s.Author.FullName : null))
            .ForCtorParam(nameof(BookCardDto.Images), o => o.MapFrom(s => s.Images.OrderBy(i => i.SortOrder)))
            .ForCtorParam(nameof(BookCardDto.Genres), o => o.MapFrom(s => s.BookGenres));
        CreateMap<Genre, GenreDto>();
        CreateMap<Genre, GenreWithBooksDto>()
            .ForCtorParam(nameof(GenreWithBooksDto.Books), o => o.MapFrom(s => s.BookGenres.Select(bg => bg.Book)));
        CreateMap<Author, AuthorDto>();
    }
}
