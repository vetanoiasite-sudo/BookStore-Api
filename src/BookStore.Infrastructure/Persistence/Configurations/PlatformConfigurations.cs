using BookStore.Domain.Inventory;
using BookStore.Domain.Platform;
using BookStore.Domain.Support;
using BookStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

/// <summary>Warehouse shelves, addressed down to the box.</summary>
public sealed class InventoryLocationConfiguration : IEntityTypeConfiguration<InventoryLocation>
{
    public void Configure(EntityTypeBuilder<InventoryLocation> builder)
    {
        builder.ToTable("InventoryLocations");
        builder.HasKey(location => location.Id);

        builder.Property(location => location.Warehouse).HasMaxLength(100).IsRequired();
        builder.Property(location => location.Zone).HasMaxLength(50);
        builder.Property(location => location.Rack).HasMaxLength(50);
        builder.Property(location => location.Shelf).HasMaxLength(50);
        builder.Property(location => location.Box).HasMaxLength(50);
        builder.Property(location => location.Code).HasMaxLength(200).IsRequired();

        // The code is what a scanner or a typed lookup resolves.
        builder.HasIndex(location => location.Code).IsUnique();
        builder.HasIndex(location => new { location.Warehouse, location.IsActive });
    }
}

/// <summary>One physical copy held by the warehouse.</summary>
public sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("InventoryItems");
        builder.HasKey(item => item.Id);

        builder.Property(item => item.Notes).HasMaxLength(1000);

        builder.HasOne(item => item.Book)
            .WithMany()
            .HasForeignKey(item => item.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.Location)
            .WithMany()
            .HasForeignKey(item => item.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(item => item.Movements)
            .WithOne(movement => movement.InventoryItem)
            .HasForeignKey(movement => movement.InventoryItemId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(InventoryItem.Movements))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(item => item.BookId).IsUnique();
        builder.HasIndex(item => new { item.LocationId, item.DispatchedAt });
    }
}

/// <summary>Every shelf a copy has occupied.</summary>
public sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ToTable("InventoryMovements");
        builder.HasKey(movement => movement.Id);

        builder.Property(movement => movement.Reason).HasMaxLength(300).IsRequired();

        builder.HasIndex(movement => new { movement.InventoryItemId, movement.CreatedAt });
    }
}

/// <summary>Support conversations between one user and the platform.</summary>
public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        builder.ToTable("SupportTickets");
        builder.HasKey(ticket => ticket.Id);

        builder.Property(ticket => ticket.Reference).HasMaxLength(32).IsRequired();
        builder.Property(ticket => ticket.Subject).HasMaxLength(300).IsRequired();
        builder.Property(ticket => ticket.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(ticket => ticket.Priority).HasConversion<string>().HasMaxLength(20);

        builder.HasMany(ticket => ticket.Messages)
            .WithOne(message => message.Ticket)
            .HasForeignKey(message => message.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(SupportTicket.Messages))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(ticket => ticket.Reference).IsUnique();
        builder.HasIndex(ticket => new { ticket.UserId, ticket.CreatedAt });

        // The support queue reads open tickets by how long they have been waiting.
        builder.HasIndex(ticket => new { ticket.Status, ticket.LastActivityAt });
    }
}

/// <summary>Messages inside a ticket, including staff-only internal notes.</summary>
public sealed class SupportMessageConfiguration : IEntityTypeConfiguration<SupportMessage>
{
    public void Configure(EntityTypeBuilder<SupportMessage> builder)
    {
        builder.ToTable("SupportMessages");
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Body).HasMaxLength(4000).IsRequired();

        builder.HasIndex(message => new { message.TicketId, message.CreatedAt });
    }
}

/// <summary>In-app notifications.</summary>
public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(notification => notification.Id);

        builder.Property(notification => notification.Title).HasMaxLength(200).IsRequired();
        builder.Property(notification => notification.Message).HasMaxLength(1000).IsRequired();
        builder.Property(notification => notification.Link).HasMaxLength(300);
        builder.Property(notification => notification.Type).HasConversion<string>().HasMaxLength(40);

        // The bell icon needs the unread count for one user, quickly.
        builder.HasIndex(notification => new { notification.UserId, notification.IsRead, notification.CreatedAt });
    }
}

/// <summary>
/// The audit trail. Rows are never updated, and the writer strips any property
/// marked as sensitive before the entry is created.
/// </summary>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Action).HasConversion<string>().HasMaxLength(40);
        builder.Property(entry => entry.EntityName).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.EntityId).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.OldValues).HasMaxLength(4000);
        builder.Property(entry => entry.NewValues).HasMaxLength(4000);
        builder.Property(entry => entry.IpAddress).HasMaxLength(60);
        builder.Property(entry => entry.UserAgent).HasMaxLength(512);
        builder.Property(entry => entry.Description).HasMaxLength(500);

        builder.HasIndex(entry => new { entry.EntityName, entry.EntityId });
        builder.HasIndex(entry => new { entry.UserId, entry.CreatedAt });
        builder.HasIndex(entry => entry.CreatedAt);
    }
}

/// <summary>Runtime settings an administrator can change without a deployment.</summary>
public sealed class PlatformSettingConfiguration : IEntityTypeConfiguration<PlatformSetting>
{
    public void Configure(EntityTypeBuilder<PlatformSetting> builder)
    {
        builder.ToTable("PlatformSettings");
        builder.HasKey(setting => setting.Id);

        builder.Property(setting => setting.Key).HasMaxLength(150).IsRequired();
        builder.Property(setting => setting.Value).HasMaxLength(1000).IsRequired();
        builder.Property(setting => setting.Description).HasMaxLength(500);

        builder.HasIndex(setting => setting.Key).IsUnique();
    }
}

/// <summary>Login accounts. Only the hash of a refresh token is ever stored.</summary>
public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.PublicId).HasMaxLength(32).IsRequired();
        builder.Property(user => user.DisplayName).HasMaxLength(150).IsRequired();
        builder.Property(user => user.PreferredLanguage).HasMaxLength(5).IsRequired();

        builder.HasIndex(user => user.PublicId).IsUnique();
        builder.HasIndex(user => user.IsActive);
    }
}

/// <summary>Rotating refresh tokens, stored hashed.</summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(token => token.Id);

        builder.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(token => token.CreatedByIp).HasMaxLength(60);
        builder.Property(token => token.RevokedReason).HasMaxLength(200);

        builder.HasOne(token => token.User)
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Refresh takes the hash and nothing else, so this lookup must be exact and fast.
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => new { token.UserId, token.ExpiresAt });
    }
}
