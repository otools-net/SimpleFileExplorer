namespace Otools.Net.Sfe.Core.Options;

/// <summary>Backups of the databases and configuration files (Administration › System, and the update page).</summary>
public sealed class BackupOptions
{
    /// <summary>
    /// SQL Server only: the folder, as the database server sees it, where it writes each database's backup (<c>BACKUP
    /// DATABASE</c>) before SFE adds it to the zip file; empty = the server's default backup folder. When SFE cannot
    /// read that folder (another computer, no access), the zip names where the backup was left instead. SQLite
    /// databases are copied by SFE itself.
    /// </summary>
    public string? SqlServerFolder { get; set; }
}
