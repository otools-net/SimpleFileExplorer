namespace Sfe.Api.Endpoints;

/// <param name="Paths">Folders to describe, relative to the library root.</param>
public sealed record FolderCoversRequest(IReadOnlyList<string>? Paths);

/// <param name="Path">File, relative to the library root.</param>
public sealed record FileLinkRequest(string? Path);

/// <param name="Animated">The picture has more than one frame.</param>
public sealed record ImageAnimationInfo(bool Animated);
