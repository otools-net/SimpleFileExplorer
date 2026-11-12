namespace Sfe.Api.Security;

/// <param name="Url">Relative to the app (<c>api/files/shared/{token}/{name}</c>); it ends with the file's name, which
/// players read the format from.</param>
public sealed record FileLink(string Url, DateTimeOffset Expires);
