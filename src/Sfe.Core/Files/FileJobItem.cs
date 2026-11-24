namespace Otools.Net.Sfe.Core.Files;

/// <param name="NewPath">Where it is now (moved, or the copy); null when not done.</param>
/// <param name="Merged">A folder merged into the folder of its name there (<paramref name="NewPath"/>, which held
/// other items before): moving it back would take them too.</param>
public sealed record FileJobItem(
    string Path,
    string? NewPath,
    FileOperationStatus Status,
    string? Message = null,
    bool Merged = false
);
