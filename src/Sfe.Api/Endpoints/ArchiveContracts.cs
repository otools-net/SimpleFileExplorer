using Sfe.Api.Security;

namespace Sfe.Api.Endpoints;

public sealed record ArchiveRequest(IReadOnlyList<string?>? Paths);

/// <param name="Url">Where the browser downloads the zip, once, within <see cref="ArchiveLinks.Lifetime"/>.</param>
public sealed record ArchiveLink(string Url, string FileName);
