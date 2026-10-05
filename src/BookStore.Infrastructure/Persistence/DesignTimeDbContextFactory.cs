using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BookStore.Infrastructure.Persistence;

/// <summary>
/// Used by the EF Core command line tools when they build a context outside the
/// running application. Reads the connection string from the environment so no
/// credentials live in source control.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string LocalDefault =
        "Server=.;Database=BookStore;Trusted_Connection=True;TrustServerCertificate=True";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? LocalDefault;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(
                typeof(DesignTimeDbContextFactory).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }
}
