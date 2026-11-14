namespace Otools.Net.Sfe.Api.Security;

/// <summary>
/// The rights a route needs (any one of them), read by the test checking that every route names some.
/// </summary>
public sealed record RightMetadata(IReadOnlyList<string> AnyOf);
