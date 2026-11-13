using Microsoft.AspNetCore.Mvc;

namespace Sfe.Api.Endpoints;

/// <param name="Paths">Folders to describe, relative to the library root.</param>
public sealed record FolderCoversRequest(IReadOnlyList<string>? Paths);

/// <param name="Path">File, relative to the library root.</param>
public sealed record FileLinkRequest(string? Path);

/// <param name="Animated">The picture has more than one frame.</param>
public sealed record ImageAnimationInfo(bool Animated);

/// <summary>A page of a folder (<c>GET api/browse</c>).</summary>
public sealed record BrowseQuery(
    [FromQuery(Name = "path")] string? Path = null,
    [FromQuery(Name = "offset")] int Offset = 0,
    [FromQuery(Name = "limit")] int? Limit = null,
    [FromQuery(Name = "sort")] string? Sort = null,
    [FromQuery(Name = "desc")] bool Desc = false
);

/// <summary>A page of a search of the library (<c>GET api/search</c>).</summary>
public sealed record LibrarySearchQuery(
    [FromQuery(Name = "q")] string? Q = null,
    [FromQuery(Name = "path")] string? Path = null,
    [FromQuery(Name = "after")] string? After = null,
    [FromQuery(Name = "limit")] int? Limit = null,
    [FromQuery(Name = "sort")] string? Sort = null,
    [FromQuery(Name = "desc")] bool Desc = false
);
