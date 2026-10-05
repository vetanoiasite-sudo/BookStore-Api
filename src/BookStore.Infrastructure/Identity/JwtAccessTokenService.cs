using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BookStore.Infrastructure.Identity;

/// <summary>
/// Issues signed access tokens. The payload is deliberately minimal: an identifier,
/// a display name, the roles and whether the address is confirmed. No email address,
/// no phone number, nothing that would leak if a token ended up in a log or a proxy.
/// </summary>
public sealed class JwtAccessTokenService : IAccessTokenService
{
    private readonly JwtOptions _options;
    private readonly IDateTimeProvider _clock;
    private readonly SigningCredentials _credentials;

    public JwtAccessTokenService(IOptions<JwtOptions> options, IDateTimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public AccessToken Issue(UserAccount user, IReadOnlyCollection<string> roles)
    {
        var issuedAt = _clock.UtcNow;
        var expiresAt = issuedAt.Add(_options.AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtClaimNames.Subject, user.Id.ToString()),
            new(JwtClaimNames.PublicId, user.PublicId),
            new(JwtClaimNames.Name, user.DisplayName),
            new(JwtClaimNames.EmailVerified, user.EmailConfirmed ? "true" : "false"),
            new(JwtClaimNames.TokenId, Guid.CreateVersion7().ToString()),
        };

        claims.AddRange(roles.Select(role => new Claim(JwtClaimNames.Role, role)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
