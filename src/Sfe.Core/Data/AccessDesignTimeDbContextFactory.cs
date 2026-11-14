using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Otools.Net.Sfe.Core.Data;

/// <summary>
/// Used by <c>dotnet ef ... --context AccessDbContext --output-dir Data/AccessMigrations</c>.
/// </summary>
internal sealed class AccessDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AccessDbContext>
{
    public AccessDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AccessDbContext>();
        SfeDatabase.Configure(
            options,
            Options.DatabaseProvider.SqlServer,
            DesignTimeDbContextFactory.ConnectionString,
            AccessDbContext.MigrationsHistoryTable
        );
        return new AccessDbContext(options.Options);
    }
}
