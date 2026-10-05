using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Application.Common.Models;
using BookStore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Identity;

/// <summary>
/// Reads accounts for the back office. Goes to the database rather than through the
/// user manager because the screens that need this need a page of accounts with their
/// roles, and asking the manager for the roles of each row in turn would be one query
/// per person on the page.
/// </summary>
public sealed class UserDirectory : IUserDirectory
{
    private readonly AppDbContext _context;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IDateTimeProvider _clock;

    public UserDirectory(
        AppDbContext context,
        UserManager<ApplicationUser> users,
        IDateTimeProvider clock)
    {
        _context = context;
        _users = users;
        _clock = clock;
    }

    public async Task<PagedResult<UserSummary>> SearchAsync(
        UserSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var accounts = _context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            var term = query.Term.Trim();

            accounts = accounts.Where(user =>
                EF.Functions.Like(user.DisplayName, $"%{term}%")
                || EF.Functions.Like(user.Email!, $"%{term}%")
                || EF.Functions.Like(user.PublicId, $"%{term}%"));
        }

        if (query.IsActive is { } isActive)
        {
            accounts = accounts.Where(user => user.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var role = query.Role.Trim().ToUpperInvariant();

            // Expressed as a subquery rather than a join so the paging below still
            // counts people rather than role assignments.
            accounts = accounts.Where(user => _context.UserRoles
                .Any(assignment => assignment.UserId == user.Id
                                   && _context.Roles.Any(candidate =>
                                       candidate.Id == assignment.RoleId
                                       && candidate.NormalizedName == role)));
        }

        var total = await accounts.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<UserSummary>.Empty(query.Page, query.PageSize);
        }

        var rows = await accounts
            .OrderByDescending(user => user.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var roles = await RolesForAsync([.. rows.Select(user => user.Id)], cancellationToken);

        return new PagedResult<UserSummary>(
            [.. rows.Select(user => Describe(user, roles))],
            query.Page,
            query.PageSize,
            total);
    }

    public async Task<IReadOnlyDictionary<Guid, UserSummary>> FindManyAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, UserSummary>();
        }

        var rows = await _context.Users
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToListAsync(cancellationToken);

        var roles = await RolesForAsync([.. rows.Select(user => user.Id)], cancellationToken);

        return rows.ToDictionary(user => user.Id, user => Describe(user, roles));
    }

    public async Task<UserSummary?> FindAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var found = await FindManyAsync([userId], cancellationToken);
        return found.GetValueOrDefault(userId);
    }

    public async Task SetActiveAsync(
        Guid userId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(
                       candidate => candidate.Id == userId, cancellationToken)
                   ?? throw new NotFoundException("User", userId);

        user.IsActive = isActive;
        user.UpdatedAt = _clock.UtcNow;

        // Closing an account has to end the sessions it already has. The access token
        // lives for minutes and cannot be recalled, but rotating the stamp stops the
        // refresh token from buying another one.
        await _users.UpdateSecurityStampAsync(user);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Role names for a set of accounts, in one query rather than one each.</summary>
    private async Task<ILookup<Guid, string>> RolesForAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var assignments = await _context.UserRoles
            .AsNoTracking()
            .Where(assignment => userIds.Contains(assignment.UserId))
            .Join(
                _context.Roles.AsNoTracking(),
                assignment => assignment.RoleId,
                role => role.Id,
                (assignment, role) => new { assignment.UserId, Name = role.Name! })
            .ToListAsync(cancellationToken);

        return assignments.ToLookup(entry => entry.UserId, entry => entry.Name);
    }

    private static UserSummary Describe(ApplicationUser user, ILookup<Guid, string> roles) =>
        new(
            user.Id,
            user.PublicId,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.EmailConfirmed,
            user.IsActive,
            [.. roles[user.Id]],
            user.CreatedAt,
            user.LastLoginAt);
}
