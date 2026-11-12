using System.Text.Json.Serialization;

namespace Sfe.Api.Security;

/// <summary>
/// What a link's token holds: signed and encrypted, it cannot be read, changed or widened to another file.
/// </summary>
/// <param name="User">The account that asked for the link; null for a visitor without one (checked against the guests'
/// rights).</param>
/// <param name="Stamp">The account's security stamp then: a new password or a disabled account ends every link it gave
/// out.</param>
public sealed record FileLinkClaims(
    [property: JsonPropertyName("t")] int Tenant,
    [property: JsonPropertyName("u")] int? User,
    [property: JsonPropertyName("s")] string? Stamp,
    [property: JsonPropertyName("p")] string Path
);
