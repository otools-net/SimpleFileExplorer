namespace Otools.Net.Sfe.Core.Backup;

/// <param name="Entry">Path in the zip file.</param>
/// <param name="Path">Full path of the file on this computer.</param>
public sealed record BackupFile(string Entry, string Path);
