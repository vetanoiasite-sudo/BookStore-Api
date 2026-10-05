using BookStore.Domain.Ordering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

/// <summary>Saved delivery addresses. Hidden rather than deleted.</summary>
public sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("Addresses");
        builder.HasKey(address => address.Id);

        builder.Property(address => address.Label).HasMaxLength(60).IsRequired();
        builder.Property(address => address.RecipientName).HasMaxLength(150).IsRequired();
        builder.Property(address => address.PhoneNumber).HasMaxLength(30).IsRequired();
        builder.Property(address => address.Country).HasMaxLength(100).IsRequired();
        builder.Property(address => address.City).HasMaxLength(100).IsRequired();
        builder.Property(address => address.District).HasMaxLength(100);
        builder.Property(address => address.Street).HasMaxLength(300).IsRequired();
        builder.Property(address => address.BuildingNumber).HasMaxLength(50);
        builder.Property(address => address.Apartment).HasMaxLength(50);
        builder.Property(address => address.PostalCode).HasMaxLength(20);
        builder.Property(address => address.Notes).HasMaxLength(500);

        // Deleted addresses stay in the table because orders point back at them, but
        // they never appear in normal queries.
        builder.HasQueryFilter(address => !address.IsDeleted);

        builder.HasIndex(address => new { address.UserId, address.IsDefault });
    }
}

/// <summary>A buyer's basket. One per user.</summary>
public sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts");
        builder.HasKey(cart => cart.Id);

        builder.HasMany(cart => cart.Items)
            .WithOne(item => item.Cart)
            .HasForeignKey(item => item.CartId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Cart.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(cart => cart.UserId).IsUnique();
    }
}

/// <summary>One copy in a basket. No quantity: a used listing is a single item.</summary>
public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("CartItems");
        builder.HasKey(item => item.Id);

        builder.Property(item => item.PriceAtAdd).HasPrecision(18, 2);

        builder.HasOne(item => item.Book)
            .WithMany()
            .HasForeignKey(item => item.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        // The same copy cannot appear twice in one basket.
        builder.HasIndex(item => new { item.CartId, item.BookId }).IsUnique();
    }
}

/// <summary>
/// A purchase. The delivery address is copied in rather than referenced, so editing
/// a saved address later cannot rewrite where a past parcel was sent.
/// </summary>
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(order => order.Id);

        builder.Property(order => order.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(order => order.Currency).HasMaxLength(3).IsRequired();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(order => order.CancellationReason).HasMaxLength(1000);

        builder.Property(order => order.Subtotal).HasPrecision(18, 2);
        builder.Property(order => order.ShippingCost).HasPrecision(18, 2);
        builder.Property(order => order.PlatformFee).HasPrecision(18, 2);
        builder.Property(order => order.Discount).HasPrecision(18, 2);
        builder.Property(order => order.Total).HasPrecision(18, 2);

        builder.OwnsOne(order => order.ShippingAddress, address =>
        {
            address.Property(value => value.RecipientName)
                .HasColumnName("ShipToRecipientName").HasMaxLength(150).IsRequired();
            address.Property(value => value.PhoneNumber)
                .HasColumnName("ShipToPhoneNumber").HasMaxLength(30).IsRequired();
            address.Property(value => value.Country)
                .HasColumnName("ShipToCountry").HasMaxLength(100).IsRequired();
            address.Property(value => value.City)
                .HasColumnName("ShipToCity").HasMaxLength(100).IsRequired();
            address.Property(value => value.District)
                .HasColumnName("ShipToDistrict").HasMaxLength(100);
            address.Property(value => value.Street)
                .HasColumnName("ShipToStreet").HasMaxLength(300).IsRequired();
            address.Property(value => value.BuildingNumber)
                .HasColumnName("ShipToBuildingNumber").HasMaxLength(50);
            address.Property(value => value.Apartment)
                .HasColumnName("ShipToApartment").HasMaxLength(50);
            address.Property(value => value.PostalCode)
                .HasColumnName("ShipToPostalCode").HasMaxLength(20);
            address.Property(value => value.Notes)
                .HasColumnName("ShipToNotes").HasMaxLength(500);
        });
        builder.Navigation(order => order.ShippingAddress).IsRequired();

        builder.HasMany(order => order.Items)
            .WithOne(item => item.Order)
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Order.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(order => order.Payment)
            .WithOne(payment => payment.Order)
            .HasForeignKey<Payment>(payment => payment.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(order => order.Shipment)
            .WithOne(shipment => shipment.Order)
            .HasForeignKey<Shipment>(shipment => shipment.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(order => order.OrderNumber).IsUnique();

        // The buyer's order list reads newest first.
        builder.HasIndex(order => new { order.BuyerId, order.CreatedAt });

        // The admin fulfilment queue reads by status.
        builder.HasIndex(order => new { order.Status, order.CreatedAt });

        // The expiry job looks for unpaid orders whose reservation has lapsed.
        builder.HasIndex(order => new { order.Status, order.ReservationExpiresAt });
    }
}

/// <summary>
/// One purchased copy. Every displayed field is a snapshot, so the order page keeps
/// working even if the listing is later edited, archived or its images removed.
/// </summary>
public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");
        builder.HasKey(item => item.Id);

        builder.Property(item => item.BookPublicId).HasMaxLength(32).IsRequired();
        builder.Property(item => item.TitleSnapshot).HasMaxLength(300).IsRequired();
        builder.Property(item => item.AuthorSnapshot).HasMaxLength(200);
        builder.Property(item => item.IsbnSnapshot).HasMaxLength(20);
        builder.Property(item => item.CoverImageSnapshot).HasMaxLength(500);
        builder.Property(item => item.ConditionSnapshot).HasConversion<string>().HasMaxLength(16);
        builder.Property(item => item.Price).HasPrecision(18, 2);
        builder.Property(item => item.PlatformFee).HasPrecision(18, 2);

        // A copy must not be deletable while an order still refers to it.
        builder.HasOne(item => item.Book)
            .WithMany()
            .HasForeignKey(item => item.BookId)
            .OnDelete(DeleteBehavior.Restrict);

        // The seller's sales list reads their own lines across all orders.
        builder.HasIndex(item => new { item.SellerId, item.CreatedAt });
        builder.HasIndex(item => item.BookId);
    }
}

/// <summary>A payment attempt. Only the provider may move it to paid.</summary>
public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.Provider).HasMaxLength(50).IsRequired();
        builder.Property(payment => payment.TransactionId).HasMaxLength(200);
        builder.Property(payment => payment.Currency).HasMaxLength(3).IsRequired();
        builder.Property(payment => payment.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(payment => payment.FailureReason).HasMaxLength(1000);
        builder.Property(payment => payment.Amount).HasPrecision(18, 2);
        builder.Property(payment => payment.RefundedAmount).HasPrecision(18, 2);

        builder.HasIndex(payment => payment.OrderId).IsUnique();

        // A provider webhook arrives with nothing but its own reference.
        builder.HasIndex(payment => new { payment.Provider, payment.TransactionId });
        builder.HasIndex(payment => payment.Status);
    }
}

/// <summary>The parcel the platform sends. Tracking is shown to the buyer only.</summary>
public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipments");
        builder.HasKey(shipment => shipment.Id);

        builder.Property(shipment => shipment.Carrier).HasMaxLength(100).IsRequired();
        builder.Property(shipment => shipment.TrackingNumber).HasMaxLength(100);
        builder.Property(shipment => shipment.TrackingUrl).HasMaxLength(500);
        builder.Property(shipment => shipment.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(shipment => shipment.Notes).HasMaxLength(1000);
        builder.Property(shipment => shipment.Cost).HasPrecision(18, 2);

        builder.HasIndex(shipment => shipment.OrderId).IsUnique();
        builder.HasIndex(shipment => shipment.TrackingNumber);
        builder.HasIndex(shipment => shipment.Status);
    }
}

/// <summary>
/// A buyer's rating of a purchased copy. The unique index on the order line is what
/// stops a book being reviewed twice, or reviewed without being bought.
/// </summary>
public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("Reviews");
        builder.HasKey(review => review.Id);

        builder.Property(review => review.Comment).HasMaxLength(2000);
        builder.Property(review => review.HiddenReason).HasMaxLength(500);

        builder.HasOne(review => review.OrderItem)
            .WithOne()
            .HasForeignKey<Review>(review => review.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // One review per purchased line, enforced by the database rather than by code.
        builder.HasIndex(review => review.OrderItemId).IsUnique();

        builder.HasIndex(review => new { review.BookId, review.IsHidden });
        builder.HasIndex(review => new { review.SellerId, review.IsHidden });
    }
}
