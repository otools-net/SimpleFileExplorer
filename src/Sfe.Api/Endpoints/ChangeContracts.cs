using Sfe.Core.KeepAlive;

namespace Sfe.Api.Endpoints;

/// <param name="Path">The folder changed (root-relative).</param>
/// <param name="Origin">The tab whose request changed it (`X-SFE-Tab`), which needs no notice; null from a background
/// job.</param>
public sealed record FolderChange(string Path, string? Origin);

/// <param name="State">How keep alive goes in the space: `none` (off, or nothing pinged), `ok`, `failing`.</param>
public sealed record KeepAliveSignal(KeepAliveState State);

/// <summary>Something came into or left the trash.</summary>
public sealed record TrashSignal;
