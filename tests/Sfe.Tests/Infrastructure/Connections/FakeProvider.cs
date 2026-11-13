using System.Collections.Concurrent;
using Sfe.Core.Connections;
using Sfe.Core.Options;

namespace Sfe.Tests.Infrastructure;

/// <summary>
/// A provider kept in memory, for the suites: no test calls a real one. An account is made by signing in to it:
/// the code <c>good:name</c> (OAuth kinds) or the password <c>secret</c> (Password) open the account <c>name</c>.
/// Its capabilities are Dropbox's by default, so the suites meet case-insensitive names and folders without dates.
/// </summary>
public sealed class FakeProvider : IConnectionProvider
{
    public const string FakeKind = "fake";

    /// <summary>Where the browser is sent for the OAuth kinds (a dotless host: nothing answers there).</summary>
    public const string AuthorizeAddress = "https://fakeprovider/authorize";

    public string Kind { get; init; } = FakeKind;
    public string Name => "Fake storage";
    public string Icon => "globe";

    public ProviderCapabilities Capabilities { get; set; } =
        new()
        {
            RangedReads = true,
            UploadsInParts = true,
            MaxUploadPart = 4 * 1024 * 1024,
            ServerSideCopy = true,
            ServerSideMove = true,
            ChangeCursor = true,
            ContentHash = "fake",
            OwnTrash = true,
            CaseInsensitiveNames = true,
            FreeSpace = true,
        };

    /// <summary>The ways to sign in; emptied by a test for a provider not configured yet.</summary>
    public List<SignInMethod> Methods { get; } =
    [
        new(SignInKind.OAuthRedirect, []),
        new(SignInKind.OAuthCode, []),
        new(SignInKind.Password, [new("name", "Name"), new("password", "Password", SignInFieldType.Secret)]),
    ];

    /// <summary>The accounts signed in to so far, by name: tests fill and read their files there.</summary>
    public ConcurrentDictionary<string, FakeAccount> Accounts { get; } = new(StringComparer.Ordinal);

    public FakeAccount Account(string name) => Accounts.GetOrAdd(name, _ => new FakeAccount(this));

    public IReadOnlyList<SignInMethod> SignInMethods(ConnectionOptions options) => Methods;

    public Task<SignInStep> BeginSignInAsync(SignInStart start, CancellationToken cancellationToken)
    {
        if (start.Kind == SignInKind.Password)
            return Task.FromResult(new SignInStep(PasswordGrant(start.Fields)));

        var address = $"{AuthorizeAddress}?state={Uri.EscapeDataString(start.State)}";
        if (start.ReturnAddress is { } back)
            address += $"&redirect_uri={Uri.EscapeDataString(back)}";
        return Task.FromResult(
            new SignInStep(null, address, $"verifier-{Guid.NewGuid():N}", [new SignInField("code", "Code")])
        );
    }

    public Task<ConnectionGrant> CompleteSignInAsync(SignInCompletion completion, CancellationToken cancellationToken)
    {
        var code = completion.Fields.GetValueOrDefault("code") ?? "";
        if (completion.Pending?.StartsWith("verifier-", StringComparison.Ordinal) != true || !code.StartsWith("good:"))
            throw new RemoteStorageException(RemoteErrorKind.CredentialsEnded, "The provider refused the code.");

        return Task.FromResult(Grant(code["good:".Length..]));
    }

    public IRemoteStorage Open(string credentials, string rootPath, ConnectionOptions options) =>
        new FakeStorage(Account(credentials), rootPath, Capabilities);

    public Task<ProviderTest> TestAsync(ConnectionOptions options, CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderTest(Methods.Count > 0, Methods.Count > 0 ? "Fine." : "Not configured."));

    private ConnectionGrant PasswordGrant(IReadOnlyDictionary<string, string> fields) =>
        fields.GetValueOrDefault("password") == "secret" && fields.GetValueOrDefault("name") is { Length: > 0 } name
            ? Grant(name)
            : throw new RemoteStorageException(RemoteErrorKind.CredentialsEnded, "Wrong name or password.");

    private ConnectionGrant Grant(string name)
    {
        Account(name);
        return new ConnectionGrant(name, $"{name}@fake", $"id:{name}");
    }
}
