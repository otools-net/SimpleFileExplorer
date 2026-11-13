namespace Sfe.Core.Options;

/// <summary>
/// The Dropbox app this server signs in through (<c>Sfe:Connections:Dropbox</c>), registered by whoever runs it at
/// Dropbox's App Console. Without an app key, Dropbox is not offered.
/// </summary>
public sealed class DropboxOptions
{
    /// <summary>The app key (public: it is in the address the browser is sent to).</summary>
    public string? AppKey { get; set; }

    /// <summary>
    /// The app secret, optional (PKCE signs in without it). Here in the configuration it wins over the one typed on
    /// the settings page, which is kept apart from the settings (<c>ProviderSecrets</c>) and never sent back.
    /// </summary>
    public string? AppSecret { get; set; }
}
