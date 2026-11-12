namespace Sfe.Core.Access;

/// <param name="Path">Relative to the library root ('/' separated, empty for the root).</param>
/// <param name="IsFolder">A folder, else a file.</param>
public sealed record QuickLinkEntry(string Path, bool IsFolder);
