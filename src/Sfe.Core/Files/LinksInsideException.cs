namespace Otools.Net.Sfe.Core.Files;

/// <summary>The trash (or a restore) would have to copy a folder holding links to another drive: never done.</summary>
public sealed class LinksInsideException(string message) : IOException(message);
