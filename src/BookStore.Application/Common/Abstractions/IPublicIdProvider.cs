namespace BookStore.Application.Common.Abstractions;

/// <summary>
/// Issues the codes that appear in public URLs and on warehouse labels. Backed by
/// database sequences, so two simultaneous requests can never be handed the same
/// code even under load.
/// </summary>
public interface IPublicIdProvider
{
    /// <summary>Next book code, for example <c>BK-2026-000123</c>.</summary>
    Task<string> NextBookPublicIdAsync(CancellationToken cancellationToken = default);

    /// <summary>Next order number, for example <c>ORD-2026-000123</c>.</summary>
    Task<string> NextOrderNumberAsync(CancellationToken cancellationToken = default);

    /// <summary>A fresh opaque seller code. Random rather than sequential.</summary>
    string NewSellerPublicId();

    /// <summary>A fresh opaque user code, used wherever an account must be referenced.</summary>
    string NewUserPublicId();
}
