namespace BookStore.Domain.Identity;

/// <summary>Named authorization policies referenced by controllers.</summary>
public static class AuthorizationPolicies
{
    public const string RequireAdmin = nameof(RequireAdmin);
    public const string RequireStaff = nameof(RequireStaff);
    public const string RequireMember = nameof(RequireMember);
    public const string RequireVerifiedEmail = nameof(RequireVerifiedEmail);
}
