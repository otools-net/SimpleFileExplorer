namespace Sfe.Core.Options;

public sealed class DatabaseOptions
{
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.SqlServer;

    /// <summary>Apply migrations (SQL Server) or create the schema (SQLite) on startup.</summary>
    public bool MigrateOnStartup { get; set; } = true;
}
