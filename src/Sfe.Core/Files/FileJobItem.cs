namespace Otools.Net.Sfe.Core.Files;

/// <param name="NewPath">Where it is now (moved, or the copy); null when not done.</param>
public sealed record FileJobItem(string Path, string? NewPath, FileOperationStatus Status, string? Message = null);
