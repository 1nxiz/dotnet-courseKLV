using Books.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Books.Dal.Configurations;

public class AuthorConfiguration : IEntityTypeConfiguration<Author>
{
    public void Configure(EntityTypeBuilder<Author> builder)
    {
        builder.ToTable("Authors", t => t.HasCheckConstraint("CK_Authors_BirthYear", "[BirthYear] BETWEEN 1000 AND 2100"));
        builder.HasKey(a => a.Id);
        builder.Property(a => a.FullName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Country).HasMaxLength(100);
        builder.HasIndex(a => a.FullName);
        builder.Navigation(a => a.Books).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.ToTable("Genres", t => t.HasCheckConstraint("CK_Genres_Slug", "[Slug] NOT LIKE '% %'"));
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();                      // довідник з фіксованими Id
        builder.Property(g => g.Name).IsRequired().HasMaxLength(100);
        builder.Property(g => g.Slug).IsRequired().HasMaxLength(100);
        builder.HasIndex(g => g.Name).IsUnique();
        builder.HasIndex(g => g.Slug).IsUnique();
        builder.Navigation(g => g.BookGenres).UsePropertyAccessMode(PropertyAccessMode.Field);

        // СТАТИЧНИЙ сідінг довідника (модельний, через міграцію)
        builder.HasData(
            new Genre(1, "Класика", "classic"),
            new Genre(2, "Пригоди", "adventure"),
            new Genre(3, "Роман", "novel"),
            new Genre(4, "Програмування", "programming"),
            new Genre(5, "Архітектура ПЗ", "software-architecture"),
            new Genre(6, "Поезія", "poetry"));
    }
}

public class BookGenreConfiguration : IEntityTypeConfiguration<BookGenre>
{
    public void Configure(EntityTypeBuilder<BookGenre> builder)
    {
        builder.ToTable("BookGenres");
        builder.HasKey(bg => new { bg.BookId, bg.GenreId });                     // складений ключ
        builder.Property(bg => bg.AddedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(bg => bg.Book).WithMany(b => b.BookGenres)
               .HasForeignKey(bg => bg.BookId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(bg => bg.Genre).WithMany(g => g.BookGenres)
               .HasForeignKey(bg => bg.GenreId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(bg => new { bg.GenreId, bg.AddedAt });                 // «нещодавно додані в жанр»
    }
}
