using BookStore.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

/// <summary>Categories, which form a tree the storefront navigates.</summary>
public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.HasKey(category => category.Id);

        builder.Property(category => category.NameAr).HasMaxLength(150).IsRequired();
        builder.Property(category => category.NameEn).HasMaxLength(150).IsRequired();
        builder.Property(category => category.Slug).HasMaxLength(150).IsRequired();

        // Deleting a parent must not silently take its children with it.
        builder.HasOne(category => category.Parent)
            .WithMany(category => category.Children)
            .HasForeignKey(category => category.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(category => category.Slug).IsUnique();
        builder.HasIndex(category => category.ParentId);
        builder.HasIndex(category => category.IsActive);
    }
}

/// <summary>Authors, used as a search field and a catalogue filter.</summary>
public sealed class AuthorConfiguration : IEntityTypeConfiguration<Author>
{
    public void Configure(EntityTypeBuilder<Author> builder)
    {
        builder.ToTable("Authors");
        builder.HasKey(author => author.Id);

        builder.Property(author => author.Name).HasMaxLength(200).IsRequired();
        builder.Property(author => author.NameEn).HasMaxLength(200);
        builder.Property(author => author.Slug).HasMaxLength(200).IsRequired();
        builder.Property(author => author.Bio).HasMaxLength(2000);

        builder.HasIndex(author => author.Slug).IsUnique();
        builder.HasIndex(author => author.Name);
    }
}

/// <summary>Publishers, used the same way as authors.</summary>
public sealed class PublisherConfiguration : IEntityTypeConfiguration<Publisher>
{
    public void Configure(EntityTypeBuilder<Publisher> builder)
    {
        builder.ToTable("Publishers");
        builder.HasKey(publisher => publisher.Id);

        builder.Property(publisher => publisher.Name).HasMaxLength(200).IsRequired();
        builder.Property(publisher => publisher.NameEn).HasMaxLength(200);
        builder.Property(publisher => publisher.Slug).HasMaxLength(200).IsRequired();

        builder.HasIndex(publisher => publisher.Slug).IsUnique();
        builder.HasIndex(publisher => publisher.Name);
    }
}

/// <summary>Book photographs. Only the storage path and metadata are stored.</summary>
public sealed class BookImageConfiguration : IEntityTypeConfiguration<BookImage>
{
    public void Configure(EntityTypeBuilder<BookImage> builder)
    {
        builder.ToTable("BookImages");
        builder.HasKey(image => image.Id);

        builder.Property(image => image.Path).HasMaxLength(500).IsRequired();
        builder.Property(image => image.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(image => image.AltText).HasMaxLength(300);
        builder.Property(image => image.Type).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(image => new { image.BookId, image.SortOrder });
    }
}

/// <summary>Every recorded step in a copy's lifecycle.</summary>
public sealed class BookStatusHistoryConfiguration : IEntityTypeConfiguration<BookStatusHistory>
{
    public void Configure(EntityTypeBuilder<BookStatusHistory> builder)
    {
        builder.ToTable("BookStatusHistory");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.FromStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(entry => entry.ToStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(entry => entry.Reason).HasMaxLength(1000);

        builder.HasIndex(entry => new { entry.BookId, entry.CreatedAt });
    }
}

/// <summary>Saved books. One row per user and book, enforced by the index.</summary>
public sealed class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> builder)
    {
        builder.ToTable("Favorites");
        builder.HasKey(favorite => favorite.Id);

        builder.HasOne(favorite => favorite.Book)
            .WithMany()
            .HasForeignKey(favorite => favorite.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        // A double click cannot create a second row.
        builder.HasIndex(favorite => new { favorite.UserId, favorite.BookId }).IsUnique();

        // Counting favorites feeds the "most popular" sort.
        builder.HasIndex(favorite => favorite.BookId);
    }
}
