using BookStore.Domain.Selling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// The selling side of an account. Kept separate from the login record so the
/// catalogue can reference a seller without touching identity data.
/// </summary>
public sealed class SellerConfiguration : IEntityTypeConfiguration<Seller>
{
    public void Configure(EntityTypeBuilder<Seller> builder)
    {
        builder.ToTable("Sellers");
        builder.HasKey(seller => seller.Id);

        builder.Property(seller => seller.PublicId).HasMaxLength(32).IsRequired();
        builder.Property(seller => seller.DisplayName).HasMaxLength(150).IsRequired();
        builder.Property(seller => seller.SuspensionReason).HasMaxLength(1000);
        builder.Property(seller => seller.RatingAverage).HasPrecision(3, 2);

        builder.HasOne(seller => seller.Wallet)
            .WithOne(wallet => wallet.Seller)
            .HasForeignKey<Wallet>(wallet => wallet.SellerId)
            .OnDelete(DeleteBehavior.Cascade);

        // The public code is what appears externally, so it must be unique and fast
        // to look up. One seller profile per login account.
        builder.HasIndex(seller => seller.PublicId).IsUnique();
        builder.HasIndex(seller => seller.UserId).IsUnique();
    }
}

/// <summary>
/// A seller's account with the platform. It stores no balance: every figure is
/// summed from the ledger, so the two can never disagree.
/// </summary>
public sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("Wallets");
        builder.HasKey(wallet => wallet.Id);

        builder.Property(wallet => wallet.Currency).HasMaxLength(3).IsRequired();

        builder.HasMany(wallet => wallet.Transactions)
            .WithOne(entry => entry.Wallet)
            .HasForeignKey(entry => entry.WalletId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Wallet.Transactions))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(wallet => wallet.SellerId).IsUnique();
    }
}

/// <summary>Immutable ledger entries. Nothing here is ever updated in place.</summary>
public sealed class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.ToTable("WalletTransactions");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(entry => entry.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(entry => entry.Amount).HasPrecision(18, 2);
        builder.Property(entry => entry.Description).HasMaxLength(500).IsRequired();

        // The statement view reads one wallet in date order.
        builder.HasIndex(entry => new { entry.WalletId, entry.CreatedAt });

        // The settlement job looks for pending entries whose date has passed.
        builder.HasIndex(entry => new { entry.Status, entry.AvailableAt });

        builder.HasIndex(entry => entry.OrderId);
    }
}

/// <summary>Payout requests, reviewed by the platform before any money moves.</summary>
public sealed class WithdrawalConfiguration : IEntityTypeConfiguration<Withdrawal>
{
    public void Configure(EntityTypeBuilder<Withdrawal> builder)
    {
        builder.ToTable("Withdrawals");
        builder.HasKey(request => request.Id);

        builder.Property(request => request.Amount).HasPrecision(18, 2);
        builder.Property(request => request.Currency).HasMaxLength(3).IsRequired();
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(request => request.PayoutDetails).HasMaxLength(1000).IsRequired();
        builder.Property(request => request.RejectionReason).HasMaxLength(1000);
        builder.Property(request => request.PaymentReference).HasMaxLength(200);

        builder.HasOne(request => request.Seller)
            .WithMany()
            .HasForeignKey(request => request.SellerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(request => new { request.SellerId, request.CreatedAt });

        // The admin payout queue reads pending requests oldest first.
        builder.HasIndex(request => new { request.Status, request.CreatedAt });
    }
}
