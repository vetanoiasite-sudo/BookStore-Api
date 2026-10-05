using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Domain.Selling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookStore.Application.Features.Authentication;

/// <summary>
/// Sign-up, sign-in, token refresh and sign-out. Everything that decides who a
/// caller is; changing a password or confirming an address lives in
/// <see cref="AccountService"/>.
/// </summary>
public sealed class AuthenticationService
{
    private readonly IIdentityService _identity;
    private readonly AccountService _accounts;
    private readonly IAccessTokenService _accessTokens;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IAppDbContext _context;
    private readonly IPublicIdProvider _publicIds;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        IIdentityService identity,
        AccountService accounts,
        IAccessTokenService accessTokens,
        IRefreshTokenService refreshTokens,
        IAppDbContext context,
        IPublicIdProvider publicIds,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<AuthenticationService> logger)
    {
        _identity = identity;
        _accounts = accounts;
        _accessTokens = accessTokens;
        _refreshTokens = refreshTokens;
        _context = context;
        _publicIds = publicIds;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Creates an account and signs it in. Every account is a member with a seller
    /// profile and its own wallet, so it can buy and sell straight away.
    /// </summary>
    public async Task<AuthenticationResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = Normalise(request.Email);

        var creation = await _identity.CreateAsync(
            email,
            request.DisplayName.Trim(),
            request.Password,
            request.PreferredLanguage,
            cancellationToken);

        if (!creation.Succeeded || creation.Value is null)
        {
            throw new AppValidationException(creation.Errors);
        }

        var account = creation.Value;

        var roles = Roles.Marketplace;

        await _identity.AddToRolesAsync(account.Id, roles, cancellationToken);
        await CreateSellerProfileAsync(account, cancellationToken);

        // Sent, not awaited on by the caller: a mail failure must not undo a signup.
        await _accounts.SendVerificationAsync(account, cancellationToken);

        _logger.LogInformation(
            "Registered account {PublicId} with roles {Roles}.",
            account.PublicId,
            string.Join('/', roles));

        return await IssueTokensAsync(account, roles, cancellationToken);
    }

    /// <summary>
    /// Verifies credentials and issues tokens. Every failure returns the same message,
    /// so the response cannot be used to discover which addresses have accounts.
    /// </summary>
    public async Task<AuthenticationResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = await _identity.FindByEmailAsync(Normalise(request.Email), cancellationToken);

        if (account is null)
        {
            _logger.LogInformation("Sign-in attempt for an address with no account.");
            throw InvalidCredentials();
        }

        var check = await _identity.CheckPasswordAsync(account.Id, request.Password, cancellationToken);

        switch (check)
        {
            case PasswordCheck.LockedOut:
                _logger.LogWarning("Sign-in refused for {PublicId}: locked out.", account.PublicId);
                throw new ForbiddenException(
                    "This account is temporarily locked after too many failed attempts. Please try again later.");

            case PasswordCheck.Deactivated:
                _logger.LogWarning("Sign-in refused for {PublicId}: deactivated.", account.PublicId);
                throw new ForbiddenException("This account has been deactivated. Please contact support.");

            case PasswordCheck.Invalid:
                _logger.LogInformation("Sign-in failed for {PublicId}: wrong password.", account.PublicId);
                throw InvalidCredentials();
        }

        await _identity.RecordSignInAsync(account.Id, cancellationToken);

        var roles = await EnsureMemberReadyAsync(
            account,
            await _identity.GetRolesAsync(account.Id, cancellationToken),
            cancellationToken);
        _logger.LogInformation("Signed in {PublicId}.", account.PublicId);

        return await IssueTokensAsync(account, roles, cancellationToken);
    }

    /// <summary>
    /// Exchanges a refresh token for a new pair. The old token stops working
    /// immediately, so a token that turns up twice is treated as stolen.
    /// </summary>
    public async Task<AuthenticationResponse> RefreshAsync(
        RefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        var exchange = await _refreshTokens.ExchangeAsync(
            request.RefreshToken,
            _currentUser.IpAddress,
            cancellationToken);

        if (exchange is null)
        {
            throw new UnauthorizedAccessException("This session has expired. Please sign in again.");
        }

        var account = await _identity.FindByIdAsync(exchange.UserId, cancellationToken);

        if (account is null || !account.IsActive)
        {
            await _refreshTokens.RevokeAllAsync(exchange.UserId, "Account is unavailable.", cancellationToken);
            throw new UnauthorizedAccessException("This session has expired. Please sign in again.");
        }

        var roles = await EnsureMemberReadyAsync(
            account,
            await _identity.GetRolesAsync(account.Id, cancellationToken),
            cancellationToken);
        var accessToken = _accessTokens.Issue(account, roles);

        return new AuthenticationResponse(
            accessToken.Value,
            accessToken.ExpiresAt,
            exchange.Replacement.Value,
            exchange.Replacement.ExpiresAt,
            await DescribeAsync(account, roles, cancellationToken));
    }

    /// <summary>
    /// Ends the session. Signing out is always reported as successful, even with an
    /// unknown token, because there is nothing useful the caller could do differently.
    /// </summary>
    public async Task LogoutAsync(
        LogoutRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.AllDevices && _currentUser.UserId is { } userId)
        {
            await _refreshTokens.RevokeAllAsync(userId, "Signed out of all devices.", cancellationToken);
            _logger.LogInformation("Signed {PublicId} out of every device.", _currentUser.PublicId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await _refreshTokens.RevokeAsync(request.RefreshToken, "Signed out.", cancellationToken);
        }
    }

    /// <summary>Describes the caller behind the current access token.</summary>
    public async Task<CurrentUserResponse> GetCurrentUserAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();

        var account = await _identity.FindByIdAsync(userId, cancellationToken)
                      ?? throw new NotFoundException("The signed-in account no longer exists.");

        var roles = await _identity.GetRolesAsync(userId, cancellationToken);
        return await DescribeAsync(account, roles, cancellationToken);
    }

    private async Task<AuthenticationResponse> IssueTokensAsync(
        UserAccount account,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var accessToken = _accessTokens.Issue(account, roles);
        var refreshToken = await _refreshTokens.IssueAsync(
            account.Id,
            _currentUser.IpAddress,
            cancellationToken);

        return new AuthenticationResponse(
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Value,
            refreshToken.ExpiresAt,
            await DescribeAsync(account, roles, cancellationToken));
    }

    private async Task<CurrentUserResponse> DescribeAsync(
        UserAccount account,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var sellerPublicId = roles.Contains(Roles.Member)
            ? await _context.Sellers
                .Where(seller => seller.UserId == account.Id)
                .Select(seller => seller.PublicId)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new CurrentUserResponse(
            account.PublicId,
            account.Email,
            account.DisplayName,
            account.EmailConfirmed,
            account.PreferredLanguage,
            roles,
            sellerPublicId);
    }

    /// <summary>
    /// Makes sure a member has the seller profile that selling needs. Accounts that
    /// only bought before the buyer and seller roles were merged get one the next
    /// time they sign in. Back-office accounts are left as they are.
    /// </summary>
    private async Task<IReadOnlyCollection<string>> EnsureMemberReadyAsync(
        UserAccount account,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        if (roles.Contains(Roles.Member))
        {
            await CreateSellerProfileAsync(account, cancellationToken);
        }

        return roles;
    }

    private async Task CreateSellerProfileAsync(UserAccount account, CancellationToken cancellationToken)
    {
        var alreadyExists = await _context.Sellers
            .AnyAsync(seller => seller.UserId == account.Id, cancellationToken);

        if (alreadyExists)
        {
            return;
        }

        var seller = Seller.Create(
            account.Id,
            account.DisplayName,
            _clock.UtcNow,
            _publicIds.NewSellerPublicId());

        _context.Sellers.Add(seller);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// One message for every credential failure. Saying "no such account" would let
    /// anyone test whether an address is registered here.
    /// </summary>
    private static UnauthorizedAccessException InvalidCredentials() =>
        new("The email address or password is incorrect.");

    private static string Normalise(string email) => email.Trim().ToLowerInvariant();
}
