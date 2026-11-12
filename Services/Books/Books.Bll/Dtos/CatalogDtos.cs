using System.ComponentModel.DataAnnotations;

namespace Books.Bll.Dtos;

public record GenreDto(int Id, string Name, string Slug);

public record GenreWithBooksDto(int Id, string Name, string Slug, IReadOnlyList<BookDto> Books);

public record GenreStatisticsDto(int Id, string Name, int BooksCount);

public record AuthorDto(int Id, string FullName, string? Country, short? BirthYear);

public class CreateAuthorDto
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string FullName { get; set; } = "";

    [StringLength(100)]
    public string? Country { get; set; }

    [Range(1000, 2100)]
    public short? BirthYear { get; set; }
}
