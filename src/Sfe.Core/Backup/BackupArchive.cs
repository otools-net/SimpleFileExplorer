namespace Sfe.Core.Backup;

/// <param name="Path">The zip file, in the temporary folder: whoever serves it deletes it.</param>
/// <param name="FileName">Name to download it as.</param>
public sealed record BackupArchive(string Path, string FileName, long Size);
