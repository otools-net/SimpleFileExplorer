namespace Otools.Net.Sfe.Core.Files;

/// <param name="Level">Deflate level of the files (0: stored).</param>
/// <param name="SkipCompressed">Files already compressed (pictures, videos, archives…) stored whatever the
/// level.</param>
/// <param name="Exclude">The real path of the zip being written: never put into itself.</param>
public sealed record CompressOptions(int Level = 6, bool SkipCompressed = true, string? Exclude = null);
