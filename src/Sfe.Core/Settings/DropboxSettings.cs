using Sfe.Core.Options;

namespace Sfe.Core.Settings;

/// <summary>
/// The editable part of <see cref="DropboxOptions"/>: the app key. The secret is never a setting (it would be sent
/// back with them): it has its own routes and file (<c>ProviderSecrets</c>).
/// </summary>
public sealed class DropboxSettings
{
    /// <inheritdoc cref="DropboxOptions.AppKey"/>
    public string? AppKey { get; set; }
}
