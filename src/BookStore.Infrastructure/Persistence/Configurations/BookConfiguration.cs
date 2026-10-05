using BookStore.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// The books table. This is the busiest table in the system, so its indexes follow
/// the queries the catalogue actually runs rather than every column individually.
/// </summary>
public sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("Books");
        builder.HasKey(book => book.Id);

        builder.Property(book => book.PublicId).HasMaxLength(32).IsRequired();
        builder.Property(book => book.Title).HasMaxLength(300).IsRequired();
        builder.Property(book => book.Slug).HasMaxLength(120).IsRequired();
        builder.Property(book => book.Description).HasMaxLength(4000);
        builder.Property(book => book.Isbn).HasMaxLength(20);
        builder.Property(book => book.RejectionReason).HasMaxLength(1000);
        builder.Property(book => book.Price).HasPrecision(18, 2);

        builder.Property(book => book.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(book => book.Language).HasConversion<string>().HasMaxLength(16);

        // Optimistic concurrency: two buyers reaching checkout for the same copy at
        // the same moment must not both succeed. The second write fails here.
        builder.Property(book => book.RowVersion).IsRowVersion();

        // The condition lives in the same row: it is meaningless without its book,
        // and every read of a book needs it.
        builder.OwnsOne(book => book.Condition, condition =>
        {
            condition.Property(value => value.Grade)
                .HasColumnName("ConditionGrade").HasConversion<string>().HasMaxLength(16).IsRequired();
            condition.Property(value => value.CoverCondition)
                .HasColumnName("ConditionCover").HasConversion<string>().HasMaxLength(16).IsRequired();
            condition.Property(value => value.PagesCondition)
                .HasColumnName("ConditionPages").HasConversion<string>().HasMaxLength(16).IsRequired();
            condition.Property(value => value.HasWritingInside).HasColumnName("ConditionHasWritingInside");
            condition.Property(value => value.HasHighlighting).HasColumnName("ConditionHasHighlighting");
            condition.Property(value => value.HasTornPages).HasColumnName("ConditionHasTornPages");
            condition.Property(value => value.HasMissingPages).HasColumnName("ConditionHasMissingPages");
            condition.Property(value => value.HasYellowing).HasColumnName("ConditionHasYellowing");
            condition.Property(value => value.OtherDamage).HasColumnName("ConditionOtherDamage").HasMaxLength(1000);
            condition.Property(value => value.Notes).HasColumnName("ConditionNotes").HasMaxLength(2000);
        });
        builder.Navigation(book => book.Condition).IsRequired();

        builder.HasOne(book => book.Category)
            .WithMany()
            .HasForeignKey(book => book.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(book => book.Author)
            .WithMany()
            .HasForeignKey(book => book.AuthorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(book => book.Publisher)
            .WithMany()
            .HasForeignKey(book => book.PublisherId)
            .OnDelete(DeleteBehavior.SetNull);

        // Photographs and history belong to the book and go with it.
        builder.HasMany(book => book.Images)
            .WithOne(image => image.Book)
            .HasForeignKey(image => image.BookId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Book.Images))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(book => book.StatusHistory)
            .WithOne(history => history.Book)
            .HasForeignKey(history => history.BookId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Book.StatusHistory))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Public code is how every external caller addresses a book.
        builder.HasIndex(book => book.PublicId).IsUnique();

        // Catalogue browsing always filters on status first.
        builder.HasIndex(book => book.Status);
        builder.HasIndex(book => new { book.Status, book.CategoryId });
        builder.HasIndex(book => new { book.Status, book.Price });
        builder.HasIndex(book => new { book.Status, book.PublishedAt });

        // The seller dashboard lists a seller's books grouped by status.
        builder.HasIndex(book => new { book.SellerId, book.Status });

        // Search by ISBN and by title.
        builder.HasIndex(book => book.Isbn);
        builder.HasIndex(book => book.Title);

        builder.HasIndex(book => book.InventoryLocationId);
    }
}
