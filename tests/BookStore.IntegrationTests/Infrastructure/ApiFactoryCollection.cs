namespace BookStore.IntegrationTests.Infrastructure;

/// <summary>
/// Shares one booted API across every integration test class, so the host is
/// started once per run rather than once per class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiFactoryCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
