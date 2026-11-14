using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="Item">As listings show it, with what the index knows (tags, resolution, duration).</param>
/// <param name="CanChecksum">The caller may read the file whole for a checksum (right `files.download`).</param>
public sealed record ItemProperties(LibraryItem Item, ItemDetails Details, bool CanChecksum);

public sealed record CountRequest(string? Path);

public sealed record ChecksumRequest(string? Path, ChecksumAlgorithm Algorithm = ChecksumAlgorithm.Sha256);
