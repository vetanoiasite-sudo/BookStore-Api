using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Identity;
using BookStore.Domain.Platform;
using BookStore.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BookStore.Infrastructure.Persistence;

/// <summary>
/// Puts in place what the platform cannot run without: the roles, the platform
/// settings and the first administrator. No sample accounts or catalogue; the
/// administrator builds the catalogue through the back office. It is idempotent and
/// only ever adds what is missing, so it is safe to run on every startup.
/// </summary>
/// <remarks>
/// The administrator's address and password come from configuration (<c>Admin:Email</c>
/// and <c>Admin:Password</c>), never from code. Once an administrator exists the
/// section is no longer read and can be removed.
/// </remarks>
public sealed class DatabaseSeeder
{
    private readonly AppDbContext _context;
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<ApplicationRole> _roles;
    private readonly IPublicIdProvider _publicIds;
    private readonly IDateTimeProvider _clock;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        AppDbContext context,
        UserManager<ApplicationUser> users,
        RoleManager<ApplicationRole> roles,
        IPublicIdProvider publicIds,
        IDateTimeProvider clock,
        IConfiguration configuration,
        ILogger<DatabaseSeeder> logger)
    {
        _context = context;
        _users = users;
        _roles = roles;
        _publicIds = publicIds;
        _clock = clock;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync();
        await SeedSettingsAsync(_clock.UtcNow, cancellationToken);
        await EnsureAdministratorAsync();
    }

    private async Task SeedRolesAsync()
    {
        foreach (var role in Roles.All)
        {
            if (!await _roles.RoleExistsAsync(role))
            {
                await _roles.CreateAsync(new ApplicationRole(role)
                {
                    Description = role switch
                    {
                        Roles.Admin => "Full access to the back office.",
                        Roles.Staff => "Reviews books, processes orders and answers support.",
                        _ => "Buys and sells books through the platform.",
                    },
                });
            }
        }
    }

    /// <summary>
    /// Creates the first administrator from configuration. Skipped once any
    /// administrator exists, so a changed password is never put back.
    /// </summary>
    private async Task EnsureAdministratorAsync()
    {
        if ((await _users.GetUsersInRoleAsync(Roles.Admin)).Count > 0)
        {
            return;
        }

        var email = _configuration["Admin:Email"];
        var password = _configuration["Admin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning(
                "There is no administrator yet. Set Admin:Email and Admin:Password to create one.");
            return;
        }

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = _configuration["Admin:DisplayName"] ?? "مدير المنصة",
            PublicId = _publicIds.NewUserPublicId(),
            CreatedAt = _clock.UtcNow,
            IsActive = true,
        };

        var result = await _users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Could not create the administrator {email}: {errors}");
        }

        await _users.AddToRoleAsync(user, Roles.Admin);
        _logger.LogInformation("Created the administrator {Email}.", email);
    }

    private async Task SeedSettingsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await _context.PlatformSettings.AnyAsync(cancellationToken))
        {
            return;
        }

        _context.PlatformSettings.AddRange(
            PlatformSetting.Create("platform.currency", "EGP", now, "Currency every price is quoted in."),
            PlatformSetting.Create("platform.feePercent", "10", now, "Commission the platform takes from each sale."),
            PlatformSetting.Create("shipping.flatCost", "30", now, "Flat shipping charge added to an order."),
            PlatformSetting.Create("checkout.reservationMinutes", "30", now, "How long a copy is held during checkout."),
            PlatformSetting.Create("wallet.settlementDays", "7", now, "Days after completion before earnings can be withdrawn."),
            PlatformSetting.Create("orders.autoCompleteDays", "14", now, "Days after delivery before an order completes itself."));

        await _context.SaveChangesAsync(cancellationToken);
    }
}
