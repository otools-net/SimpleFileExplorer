namespace Sfe.Core.Files.Compression;

/// <summary>
/// An archive that can't be read as asked: broken, split, protected by a password, past a limit, or using a method SFE
/// doesn't decompress. The message is for the person (no path, nothing of the server).
/// </summary>
/// <param name="needsPassword">The archive, or the entry, is protected by a password that wasn't given (or is wrong).</param>
public sealed class ArchiveException(string message, Exception? inner = null, bool needsPassword = false) : IOException(message, inner)
{
    public bool NeedsPassword { get; } = needsPassword;
}
