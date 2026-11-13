using System.Collections.Concurrent;
using Sfe.Core.Access;

namespace Sfe.Core.Connections;

/// <summary>
/// Secrets of the providers' apps typed on the settings page (Dropbox's app secret), one file per kind in a folder
/// of the server's data (beside the setup code) readable by the server's account only (<see cref="SecretFiles"/>).
/// Never in <c>settings.json</c> nor sent back: the page only learns whether one is set. A backup does not carry them:
/// after a restore on another computer they are typed again.
/// </summary>
public sealed class ProviderSecrets(string directory)
{
    /// <summary>A secret longer than this is not an app secret.</summary>
    public const int MaxLength = 512;

    private readonly ConcurrentDictionary<string, string?> _read = new(StringComparer.OrdinalIgnoreCase);

    public string Directory { get; } = directory;

    /// <summary>The secret of <paramref name="kind"/>; null when none was typed.</summary>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public string? Get(string kind) =>
        _read.GetOrAdd(kind, k => File.Exists(PathOf(k)) ? File.ReadAllText(PathOf(k)).Trim() : null)
            is { Length: > 0 } secret
            ? secret
            : null;

    /// <exception cref="IOException">The folder is not writable.</exception>
    public void Set(string kind, string secret)
    {
        SecretFiles.CreatePrivateDirectory(Directory);
        SecretFiles.WriteAllText(PathOf(kind), secret.Trim());
        _read[kind] = secret.Trim();
    }

    /// <exception cref="IOException">The file cannot be deleted.</exception>
    public void Remove(string kind)
    {
        File.Delete(PathOf(kind));
        _read[kind] = null;
    }

    /// <summary>
    /// The kind's file: its id is a short word (<see cref="Settings.ConnectionSettings"/>), never a path.
    /// </summary>
    private string PathOf(string kind) =>
        kind.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            ? Path.Combine(Directory, $"{kind.ToLowerInvariant()}-app-secret.txt")
            : throw new ArgumentException("Not a kind of connection.", nameof(kind));
}
