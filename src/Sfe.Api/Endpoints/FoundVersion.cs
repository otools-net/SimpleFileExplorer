using Otools.Net.Sfe.Api.Security;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>A previous version the caller may see, and its file.</summary>
/// <param name="State">The caller's access.</param>
/// <param name="Full">The file's full path.</param>
/// <param name="Canonical">The file's path in the library.</param>
internal sealed record FoundVersion(AccessState State, string Full, string Canonical);
