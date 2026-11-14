using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.Settings;

/// <summary>
/// The editable part of <see cref="SecurityOptions"/>: only the access tokens. The rest (sign-in limits, keys,
/// proxies, links) stays in the configuration.
/// </summary>
public sealed class SecuritySettings
{
    public AccessTokenSettings AccessTokens { get; set; } = new();

    public static SecuritySettings From(SecurityOptions options) =>
        new() { AccessTokens = AccessTokenSettings.From(options.AccessTokens) };

    public void ApplyTo(SecurityOptions options) => AccessTokens.ApplyTo(options.AccessTokens);

    /// <inheritdoc cref="AccessTokenSettings.Errors"/>
    public IEnumerable<(string Key, string Message)> Errors() => AccessTokens.Errors();
}
