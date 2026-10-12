using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sfe.Core.Data;

/// <summary>Used by <c>dotnet ef</c> to generate SQL Server migrations.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SfeDbContext>
{
    public const string ConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=sfe-design;Trusted_Connection=True;";

    public SfeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SfeDbContext>();
        SfeDatabase.Configure(options, Options.DatabaseProvider.SqlServer, ConnectionString);
        return new SfeDbContext(options.Options);
    }
}

/// <summary>
/// Used by <c>dotnet ef ... --context AccessDbContext --output-dir Data/AccessMigrations</c>.
/// </summary>
internal sealed class AccessDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AccessDbContext>
{
    public AccessDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AccessDbContext>();
        SfeDatabase.Configure(options, Options.DatabaseProvider.SqlServer, DesignTimeDbContextFactory.ConnectionString, AccessDbContext.MigrationsHistoryTable);
        return new AccessDbContext(options.Options);
    }
}
