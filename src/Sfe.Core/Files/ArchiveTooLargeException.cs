namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// A zip grew past <c>Files:MaxArchiveMegabytes</c> while it was written (the index was behind the disk).
/// </summary>
public sealed class ArchiveTooLargeException(string message) : IOException(message);
