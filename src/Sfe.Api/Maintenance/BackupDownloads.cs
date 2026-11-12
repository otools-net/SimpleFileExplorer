using System.Collections.Concurrent;
using System.Security.Cryptography;
using Sfe.Core.Backup;

namespace Sfe.Api.Maintenance;

/// <summary>
/// Backups made on the update page, kept until downloaded once (or for <see cref="Lifetime"/>): the page asks for one
/// with the setup code, then the browser downloads it by its token like any file, without holding it in memory.
/// </summary>
public sealed class BackupDownloads(ILogger<BackupDownloads> logger)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (BackupArchive Archive, DateTime Expires)> _archives = new();

    public string Add(BackupArchive archive)
    {
        Purge();
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        _archives[token] = (archive, DateTime.UtcNow + Lifetime);
        return token;
    }

    /// <summary>The backup of <paramref name="token"/>, forgotten here: the caller serves it and deletes it.</summary>
    public BackupArchive? Take(string token)
    {
        Purge();
        return _archives.TryRemove(token, out var entry) ? entry.Archive : null;
    }

    private void Purge()
    {
        foreach (var (token, entry) in _archives)
        {
            if (entry.Expires > DateTime.UtcNow || !_archives.TryRemove(token, out _))
                continue;
            try
            {
                File.Delete(entry.Archive.Path);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not delete the expired backup {File}", entry.Archive.Path);
            }
        }
    }
}
