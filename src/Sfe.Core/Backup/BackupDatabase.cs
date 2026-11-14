using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.Backup;

/// <param name="Entry">Path in the zip file, without extension for SQL Server (<c>.bak</c> is added).</param>
/// <param name="Description">What it holds, for the manifest (e.g. "Accounts, spaces and the index of
/// Library").</param>
public sealed record BackupDatabase(
    string Entry,
    string Description,
    DatabaseProvider Provider,
    string ConnectionString
);
