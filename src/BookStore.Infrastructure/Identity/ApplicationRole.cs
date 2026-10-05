using Microsoft.AspNetCore.Identity;

namespace BookStore.Infrastructure.Identity;

/// <summary>
/// A platform role. The four roles are fixed and seeded; the entity exists only so
/// roles can carry a human description in the admin screens.
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole()
    {
    }

    public ApplicationRole(string name) : base(name)
    {
        NormalizedName = name.ToUpperInvariant();
    }

    /// <summary>What the role is for, shown on the admin users screen.</summary>
    public string? Description { get; set; }
}
