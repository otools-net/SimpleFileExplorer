using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Sfe.Core.Data;

/// <summary>Creates or updates a library's index database.</summary>
public sealed class DatabaseInitializer(IDbContextFactory<SfeDbContext> dbFactory, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await DatabaseSchema.InitializeAsync(db, logger, cancellationToken);
    }
}
