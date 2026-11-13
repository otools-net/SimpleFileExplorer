using Dropbox.Api;
using Sfe.Core.Options;

namespace Sfe.Core.Connections.Dropbox;

/// <summary>
/// Dropbox, through the app whoever runs the server registered (<see cref="DropboxOptions"/>): signs in with OAuth
/// and PKCE, by the redirect flow when the server's return address is registered at Dropbox, else by the code the
/// person copies. Every call leaves through <see cref="OutboundPolicy"/> with Dropbox's hosts only.
/// </summary>
public sealed class DropboxProvider(OutboundPolicy outbound, ProviderSecrets secrets)
    : IConnectionProvider,
        IRegisteredApp
{
    public const string DropboxKind = "dropbox";

    /// <summary>
    /// What the server calls: the token, the API, the content, the temporary links of ranged reads. The browser
    /// alone goes to the authorize page.
    /// </summary>
    public static readonly OutboundRule Rule = new([
        "api.dropbox.com",
        "api.dropboxapi.com",
        "content.dropboxapi.com",
        "*.dl.dropboxusercontent.com",
    ]);

    /// <summary>What SFE asks of an account: who it is, and its files.</summary>
    public static readonly string[] Scopes =
    [
        "account_info.read",
        "files.metadata.read",
        "files.content.read",
        "files.content.write",
    ];

    public string Kind => DropboxKind;
    public string Name => "Dropbox";
    public string Icon => "globe";

    public ProviderCapabilities Capabilities { get; } =
        new()
        {
            RangedReads = true,
            UploadsInParts = true,
            MaxUploadPart = 150L * 1024 * 1024,
            ServerSideCopy = true,
            ServerSideMove = true,
            Thumbnails = true,
            ChangeCursor = true,
            ContentHash = "dropbox",
            OwnTrash = true,
            CaseInsensitiveNames = true,
            FreeSpace = true,
        };

    public IReadOnlyList<SignInMethod> SignInMethods(ConnectionOptions options) =>
        string.IsNullOrWhiteSpace(options.Dropbox.AppKey)
            ? []
            : [new(SignInKind.OAuthRedirect, []), new(SignInKind.OAuthCode, [])];

    public Task<SignInStep> BeginSignInAsync(SignInStart start, CancellationToken cancellationToken)
    {
        var key = AppKey(start.Options);
        var redirect = start.Kind switch
        {
            SignInKind.OAuthRedirect => start.ReturnAddress
                ?? throw new RemoteStorageException(RemoteErrorKind.Refused, "No return address."),
            SignInKind.OAuthCode => null,
            _ => throw new RemoteStorageException(RemoteErrorKind.NotSupported, "Dropbox signs in with OAuth."),
        };
        var verifier = DropboxOAuth2Helper.GeneratePKCECodeVerifier();
        var address = DropboxOAuth2Helper.GetAuthorizeUri(
            OAuthResponseType.Code,
            key,
            redirect,
            state: start.State,
            tokenAccessType: TokenAccessType.Offline,
            scopeList: Scopes,
            includeGrantedScopes: IncludeGrantedScopes.None,
            codeChallenge: DropboxOAuth2Helper.GeneratePKCECodeChallenge(verifier)
        );
        IReadOnlyList<SignInField> fields = redirect is null ? [new SignInField("code", "Code shown by Dropbox")] : [];
        return Task.FromResult(new SignInStep(null, address.AbsoluteUri, verifier, fields));
    }

    public async Task<ConnectionGrant> CompleteSignInAsync(
        SignInCompletion completion,
        CancellationToken cancellationToken
    )
    {
        var code = completion.Fields.GetValueOrDefault("code")?.Trim();
        if (string.IsNullOrEmpty(code) || completion.Pending is null)
            throw new RemoteStorageException(RemoteErrorKind.Refused, "No code from Dropbox.");

        var key = AppKey(completion.Options);
        using var http = outbound.CreateClient(Rule);
        var token = await DropboxErrors.CallAsync(() =>
            DropboxOAuth2Helper.ProcessCodeFlowAsync(
                code,
                key,
                AppSecret(completion.Options),
                completion.Kind == SignInKind.OAuthRedirect ? completion.ReturnAddress : null,
                http,
                completion.Pending
            )
        );
        if (string.IsNullOrEmpty(token.RefreshToken))
            throw new RemoteStorageException(RemoteErrorKind.Refused, "Dropbox gave no lasting sign-in.");

        using var client = new DropboxClient(token.AccessToken, Config(http));
        var account = await DropboxErrors.CallAsync(() => client.Users.GetCurrentAccountAsync());
        return new ConnectionGrant(token.RefreshToken, account.Email, account.AccountId);
    }

    /// <summary>
    /// The files, through a client made from the refresh token (<paramref name="credentials"/>): the SDK fetches
    /// access tokens by itself; a refused refresh is <see cref="RemoteErrorKind.CredentialsEnded"/> at the first call.
    /// </summary>
    public IRemoteStorage Open(string credentials, string rootPath, ConnectionOptions options)
    {
        var http = outbound.CreateClient(Rule);
        var secret = AppSecret(options);
        var config = Config(http);
        var client = secret is null
            ? new DropboxClient(credentials, AppKey(options), config)
            : new DropboxClient(credentials, AppKey(options), secret, config);
        return new DropboxStorage(client, http, rootPath);
    }

    public async Task<ProviderTest> TestAsync(ConnectionOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Dropbox.AppKey))
            return new(false, "No app key: Dropbox is not offered.");
        if (AppSecret(options) is not { } secret)
        {
            return new(
                true,
                "An app key is set. Without the app secret it can only be checked by signing in to Dropbox."
            );
        }

        using var http = outbound.CreateClient(Rule);
        using var app = new DropboxAppClient(options.Dropbox.AppKey, secret, Config(http));
        try
        {
            var echo = await DropboxErrors.CallAsync(() => app.Check.AppAsync("sfe"));
            return echo.Result == "sfe"
                ? new(true, "Dropbox accepts the app key and secret.")
                : new(false, "Dropbox answered something unexpected.");
        }
        catch (RemoteStorageException error)
        {
            return new(
                false,
                error.Kind == RemoteErrorKind.CredentialsEnded
                    ? "Dropbox refused the app key or secret."
                    : error.Message
            );
        }
    }

    /// <summary>The configuration's secret, else the one typed on the settings page; null = PKCE alone.</summary>
    public string? AppSecret(ConnectionOptions options) =>
        string.IsNullOrWhiteSpace(options.Dropbox.AppSecret)
            ? secrets.Get(DropboxKind)
            : options.Dropbox.AppSecret.Trim();

    public bool SecretInConfiguration(ConnectionOptions options) =>
        !string.IsNullOrWhiteSpace(options.Dropbox.AppSecret);

    private static string AppKey(ConnectionOptions options) =>
        options.Dropbox.AppKey is { Length: > 0 } key
            ? key.Trim()
            : throw new RemoteStorageException(RemoteErrorKind.Refused, "Dropbox is not configured on this server.");

    private static DropboxClientConfig Config(HttpClient http) =>
        new("SFE") { HttpClient = http, MaxRetriesOnError = 0 };
}
