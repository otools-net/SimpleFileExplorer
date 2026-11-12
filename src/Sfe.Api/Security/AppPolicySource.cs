using Microsoft.Extensions.Primitives;
using Sfe.Core.Options;
using Sfe.Core.Settings;

namespace Sfe.Api.Security;

/// <summary>
/// The policy of the app's pages as the server is set now (<see cref="SecurityHeaders.AppPolicyFor"/>): pictures and
/// media load from the app, and from the data server when there is one (<c>Sfe:DataServer</c>, a server setting).
/// Built once and again when the configuration or the server's settings change.
/// </summary>
public sealed class AppPolicySource : IDisposable
{
    private readonly SettingLayers _settings;
    private readonly IDisposable[] _watches;
    private string? _policy;

    public AppPolicySource(SettingLayers settings, IConfiguration configuration)
    {
        _settings = settings;
        _watches =
        [
            ChangeToken.OnChange(settings.GetChangeToken, Forget),
            ChangeToken.OnChange(configuration.GetReloadToken, Forget),
        ];
    }

    /// <summary>The <c>Content-Security-Policy</c> of the app's pages.</summary>
    public string Current => _policy ??= SecurityHeaders.AppPolicyFor(DataServerUrl.Normalize(_settings.ServerLevel().DataServer));

    public void Dispose()
    {
        foreach (var watch in _watches)
            watch.Dispose();
    }

    private void Forget() => _policy = null;
}
