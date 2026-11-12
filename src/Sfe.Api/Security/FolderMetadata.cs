namespace Sfe.Api.Security;

/// <summary>
/// How a route takes folder rules into account (agents/docs/features/folder-access.md), read by the test checking
/// that every route about library paths says it; null <paramref name="How"/> for routes whose paths are not the
/// library's (server folders).
/// </summary>
public sealed record FolderMetadata(string? How);
