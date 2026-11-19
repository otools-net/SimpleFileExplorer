using Otools.Net.Sfe.Core.Files.Compression;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The archive a route of the explorer reads: its full path on disk, its canonical path (the key of its catalog) and
/// its format.
/// </summary>
internal sealed record ExploredArchive(string Full, string Canonical, ArchiveFormat Format);
