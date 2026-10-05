using System.Security.Claims;
using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Identity;

namespace BookStore.Api.Common;

/// <summary>
/// Reads the caller identity from the claims on the current request. Claim names
/// match what the token service issues, because inbound claim mapping is turned off.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(JwtClaimNames.Subject), out var id) ? id : null;

    public string? PublicId => Principal?.FindFirstValue(JwtClaimNames.PublicId);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(JwtClaimNames.Role).Select(claim => claim.Value).ToArray() ?? [];

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var value = _accessor.HttpContext?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public Guid RequireUserId() =>
        UserId ?? throw new UnauthorizedAccessException("This action requires an authenticated user.");
}
