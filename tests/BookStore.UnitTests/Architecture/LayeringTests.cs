using System.Reflection;
using BookStore.Domain.Common;
using BookStore.Domain.Identity;

namespace BookStore.UnitTests.Architecture;

/// <summary>
/// Guards the dependency rule: Domain must stay free of infrastructure and of
/// third-party packages, so business rules remain portable and fast to test.
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly Domain = typeof(DomainException).Assembly;
    private static readonly Assembly Application = typeof(
        Application.Common.Models.PageRequest).Assembly;

    [Fact]
    public void Domain_references_only_the_base_class_library()
    {
        var offenders = Domain.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !name.StartsWith("System.", StringComparison.Ordinal)
                           && name is not "System"
                           && name is not "netstandard"
                           && name is not "mscorlib")
            .ToArray();

        offenders.ShouldBeEmpty(
            $"Domain must not depend on packages. Found: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Application_does_not_reference_infrastructure()
    {
        var offenders = Application.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase)
                           || name.Contains("BookStore.Api", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        offenders.ShouldBeEmpty(
            $"Application must not depend on outer layers. Found: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Back_office_and_marketplace_roles_do_not_overlap()
    {
        Roles.BackOffice.Intersect(Roles.Marketplace).ShouldBeEmpty();
        Roles.All.Count.ShouldBe(Roles.BackOffice.Count + Roles.Marketplace.Count);
        Roles.All.Distinct().Count().ShouldBe(Roles.All.Count);
    }
}
