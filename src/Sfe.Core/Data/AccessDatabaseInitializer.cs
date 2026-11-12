using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Sfe.Core.Data;

/// <summary>
/// Creates or updates the database of the accounts, groups and libraries (<see cref="AccessDbContext"/>).
/// </summary>
public sealed class AccessDatabaseInitializer(
    IDbContextFactory<AccessDbContext> dbFactory,
    ILogger<AccessDatabaseInitializer> logger
)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await DatabaseSchema.InitializeAsync(db, logger, cancellationToken);
    }
}
