namespace BookStore.Application.Common.Abstractions;

/// <summary>
/// The caller behind the current request, resolved from the JWT. Application
/// services depend on this rather than on <c>HttpContext</c>.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Internal user id, or <c>null</c> for anonymous callers.</summary>
    Guid? UserId { get; }

    /// <summary>Public, non-guessable id safe to expose in API responses.</summary>
    string? PublicId { get; }

    bool IsAuthenticated { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool IsInRole(string role);

    /// <summary>Caller address, recorded on audit entries for investigations.</summary>
    string? IpAddress { get; }

    /// <summary>Caller user agent, recorded on audit entries.</summary>
    string? UserAgent { get; }

    /// <summary>Throws when the caller is anonymous; returns the id otherwise.</summary>
    Guid RequireUserId();
}
