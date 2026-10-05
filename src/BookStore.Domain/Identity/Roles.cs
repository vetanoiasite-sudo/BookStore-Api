namespace BookStore.Domain.Identity;

/// <summary>
/// The three platform roles. Admin and Staff are back-office roles and are never
/// combined with <see cref="Member"/>, the single marketplace role that lets an
/// account both buy and sell.
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Staff = "Staff";
    public const string Member = "Member";

    public static readonly IReadOnlyList<string> All = [Admin, Staff, Member];

    public static readonly IReadOnlyList<string> BackOffice = [Admin, Staff];

    public static readonly IReadOnlyList<string> Marketplace = [Member];
}
