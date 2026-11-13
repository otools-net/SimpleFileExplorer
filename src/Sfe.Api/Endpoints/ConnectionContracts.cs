namespace Sfe.Api.Endpoints;

/// <summary>How a kind of connection is set up on this server (Administration › Connections).</summary>
/// <param name="Offered">Allowed by the settings and configured enough to sign in (Dropbox: an app key).</param>
/// <param name="ReturnAddress">What to register at the provider for the redirect sign-in, as this request reached
/// the server.</param>
/// <param name="AppSecret">For a provider with a registered app: <c>none</c>, <c>page</c> (typed on the page) or
/// <c>configuration</c> (which wins); null for other kinds.</param>
public sealed record ProviderSetupView(string Kind, string Name, bool Offered, string ReturnAddress, string? AppSecret)
{
    public const string NoSecret = "none";
    public const string SecretOnPage = "page";
    public const string SecretInConfiguration = "configuration";
}

/// <summary>A provider app's secret, as typed.</summary>
public sealed record AppSecretInput(string? Secret);
