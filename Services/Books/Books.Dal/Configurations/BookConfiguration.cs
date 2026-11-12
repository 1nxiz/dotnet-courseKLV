using Books.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Books.Dal.Configurations;

public class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("Books", t =>
        {
            t.HasCheckConstraint("CK_Books_Price", "[Price] > 0");
            t.HasCheckConstraint("CK_Books_TotalCopies", "[TotalCopies] >= 0");
            t.HasCheckConstraint("CK_Books_PublishedYear", "[PublishedYear] BETWEEN 1450 AND 2100");
        });
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Title).IsRequired().HasMaxLength(300);
        builder.Property(b => b.Isbn).IsRequired().HasMaxLength(20);
        builder.Property(b => b.Price).HasColumnType("decimal(18,2)");
        builder.Property(b => b.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(b => b.UpdatedAt).HasColumnType("datetime2(3)");
        builder.Property(b => b.RowVersion).IsRowVersion();                    // оптимістична конкурентність

        builder.HasIndex(b => b.Isbn).IsUnique();                               // перевірка дубліката ISBN -> 409
        builder.HasIndex(b => b.Title);                                         // пошук/сортування за назвою
        builder.HasIndex(b => b.Price);                                         // фільтр діапазону цін

        builder.HasOne(b => b.Author)                                           // N:1, без каскаду
               .WithMany(a => a.Books)
               .HasForeignKey(b => b.AuthorId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Detail)                                           // 1:1 — PK BookDetail = FK
               .WithOne()
               .HasForeignKey<BookDetail>(d => d.BookId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.Images)                                          // 1:N
               .WithOne()
               .HasForeignKey(i => i.BookId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(b => b.Images).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(b => b.BookGenres).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class BookDetailConfiguration : IEntityTypeConfiguration<BookDetail>
{
    public void Configure(EntityTypeBuilder<BookDetail> builder)
    {
        builder.ToTable("BookDetails", t => t.HasCheckConstraint("CK_BookDetails_Pages", "[Pages] > 0"));
        builder.HasKey(d => d.BookId);
        builder.Property(d => d.BookId).ValueGeneratedNever();
        builder.Property(d => d.Description).IsRequired().HasMaxLength(4000);
        builder.Property(d => d.Publisher).HasMaxLength(200);
        builder.Property(d => d.Language).IsRequired().HasMaxLength(10).HasDefaultValue("uk");
    }
}

public class BookImageConfiguration : IEntityTypeConfiguration<BookImage>
{
    public void Configure(EntityTypeBuilder<BookImage> builder)
    {
        builder.ToTable("BookImages", t => t.HasCheckConstraint("CK_BookImages_SortOrder", "[SortOrder] >= 0"));
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Url).IsRequired().HasMaxLength(500);
        builder.HasIndex(i => new { i.BookId, i.SortOrder }).IsUnique();       // галерея в порядку
    }
}
