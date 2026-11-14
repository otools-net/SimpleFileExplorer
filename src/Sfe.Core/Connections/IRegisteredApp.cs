using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// A provider reached through an app whoever runs the server registers at the provider (Dropbox): the app's secret
/// may be typed on the settings page (<see cref="ProviderSecrets"/>) or set in the configuration, which wins.
/// </summary>
public interface IRegisteredApp
{
    /// <summary>The configuration holds the secret: the one typed on the page is not used.</summary>
    bool SecretInConfiguration(ConnectionOptions options);
}
