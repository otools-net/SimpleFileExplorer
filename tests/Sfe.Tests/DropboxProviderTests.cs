using System.Net;
using System.Web;
using Sfe.Core.Connections;
using Sfe.Core.Connections.Dropbox;
using Sfe.Core.Options;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// <see cref="DropboxProvider"/>'s sign-in and test against recorded answers (<see cref="RecordedDropbox"/>): what
/// it sends Dropbox, what it keeps, how Dropbox's refusals come out. No test calls Dropbox.
/// </summary>
[TestClass]
public sealed class DropboxProviderTests
{
    private const string Key = "appkey123";

    private readonly RecordedDropbox _dropbox = new();
    private string _secretsFolder = null!;
    private ProviderSecrets _secrets = null!;
    private DropboxProvider _provider = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize()
    {
        _secretsFolder = Path.Combine(Path.GetTempPath(), $"sfe-secrets-{Guid.NewGuid():N}");
        _secrets = new ProviderSecrets(_secretsFolder);
        _provider = new DropboxProvider(new OutboundPolicy(() => _dropbox), _secrets);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dropbox.Dispose();
        if (Directory.Exists(_secretsFolder))
            Directory.Delete(_secretsFolder, true);
    }

    [TestMethod]
    public void WithoutAnAppKeyDropboxIsNotOffered()
    {
        Assert.IsEmpty(_provider.SignInMethods(new ConnectionOptions()));
        CollectionAssert.AreEqual(
            new[] { SignInKind.OAuthRedirect, SignInKind.OAuthCode },
            _provider.SignInMethods(Options()).Select(m => m.Kind).ToArray()
        );
    }

    [TestMethod]
    public async Task TheRedirectSignInSendsPkceAndKeepsTheVerifierOnTheServer()
    {
        const string back = "https://sfe.example/app/api/connections/oauth/callback";
        var step = await _provider.BeginSignInAsync(Start(SignInKind.OAuthRedirect, back), Token);

        var address = new Uri(step.Address!);
        var query = HttpUtility.ParseQueryString(address.Query);
        Assert.AreEqual("www.dropbox.com", address.Host);
        Assert.AreEqual(Key, query["client_id"]);
        Assert.AreEqual("code", query["response_type"]);
        Assert.AreEqual(back, query["redirect_uri"]);
        Assert.AreEqual("the-state", query["state"]);
        Assert.AreEqual("offline", query["token_access_type"]);
        Assert.AreEqual("S256", query["code_challenge_method"]);
        Assert.IsFalse(string.IsNullOrEmpty(query["code_challenge"]));
        Assert.IsNotNull(step.Pending);
        Assert.DoesNotContain(step.Pending, step.Address!);
        Assert.IsEmpty(step.Fields!);

        var grant = await _provider.CompleteSignInAsync(
            Completion(SignInKind.OAuthRedirect, step.Pending, back),
            Token
        );

        Assert.AreEqual(new ConnectionGrant("refresh-1", "alice@example.org", "dbid:alice"), grant);
        var token = HttpUtility.ParseQueryString(_dropbox.Last("/oauth2/token").Body);
        Assert.AreEqual("the-code", token["code"]);
        Assert.AreEqual(step.Pending, token["code_verifier"]);
        Assert.AreEqual(back, token["redirect_uri"]);
        Assert.IsNull(token["client_secret"]);
        Assert.AreEqual("Bearer", _dropbox.Last("/2/users/get_current_account").Request.Headers.Authorization!.Scheme);
    }

    [TestMethod]
    public async Task TheCodeSignInAsksForTheCodeAndSendsTheSecretWhenThereIsOne()
    {
        _secrets.Set(DropboxProvider.DropboxKind, "  page-secret ");
        var step = await _provider.BeginSignInAsync(Start(SignInKind.OAuthCode, null), Token);

        Assert.IsNull(HttpUtility.ParseQueryString(new Uri(step.Address!).Query)["redirect_uri"]);
        Assert.AreEqual("code", step.Fields!.Single().Id);

        await _provider.CompleteSignInAsync(Completion(SignInKind.OAuthCode, step.Pending, null), Token);
        var token = HttpUtility.ParseQueryString(_dropbox.Last("/oauth2/token").Body);
        Assert.AreEqual("page-secret", token["client_secret"]);
        Assert.IsNull(token["redirect_uri"]);

        // The configuration's secret wins over the page's.
        var options = Options();
        options.Dropbox.AppSecret = "config-secret";
        Assert.AreEqual("config-secret", _provider.AppSecret(options));
        Assert.IsTrue(_provider.SecretInConfiguration(options));
    }

    [TestMethod]
    public async Task ACodeDropboxRefusesEndsTheSignIn()
    {
        _dropbox.Answers["/oauth2/token"] = (
            HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"code doesn't exist or has expired"}"""
        );

        var error = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            _provider.CompleteSignInAsync(Completion(SignInKind.OAuthCode, "verifier", null), Token)
        );

        Assert.AreEqual(RemoteErrorKind.CredentialsEnded, error.Kind);
        Assert.DoesNotContain("the-code", error.Message);
    }

    [TestMethod]
    public async Task OnlyDropboxsHostsAreCalled()
    {
        _dropbox.Answers.Clear();
        _dropbox.Answers["/oauth2/token"] = (HttpStatusCode.OK, RecordedDropbox.TokenAnswer);

        await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            _provider.CompleteSignInAsync(Completion(SignInKind.OAuthCode, "verifier", null), Token)
        );
        Assert.IsTrue(_dropbox.Requests.All(r => DropboxProvider.Rule.Allows(r.Request.RequestUri!)));
    }

    [TestMethod]
    public async Task TheTestChecksKeyAndSecretWhenItCan()
    {
        Assert.IsFalse((await _provider.TestAsync(new ConnectionOptions(), Token)).Ok);

        var keyOnly = await _provider.TestAsync(Options(), Token);
        Assert.IsTrue(keyOnly.Ok);
        Assert.IsEmpty(_dropbox.Requests);

        _secrets.Set(DropboxProvider.DropboxKind, "page-secret");
        Assert.IsTrue((await _provider.TestAsync(Options(), Token)).Ok);
        Assert.AreEqual("Basic", _dropbox.Last("/2/check/app").Request.Headers.Authorization!.Scheme);

        _dropbox.Answers["/2/check/app"] = (HttpStatusCode.Unauthorized, RecordedDropbox.AuthErrorAnswer);
        var refused = await _provider.TestAsync(Options(), Token);
        Assert.IsFalse(refused.Ok);
        Assert.DoesNotContain("page-secret", refused.Message);
    }

    [TestMethod]
    public void TheSecretFileHoldsTheSecretAndIsRemoved()
    {
        Assert.IsNull(_secrets.Get(DropboxProvider.DropboxKind));
        _secrets.Set(DropboxProvider.DropboxKind, "s3cret");
        Assert.AreEqual("s3cret", new ProviderSecrets(_secretsFolder).Get(DropboxProvider.DropboxKind));

        _secrets.Remove(DropboxProvider.DropboxKind);
        Assert.IsNull(_secrets.Get(DropboxProvider.DropboxKind));
        Assert.IsNull(new ProviderSecrets(_secretsFolder).Get(DropboxProvider.DropboxKind));
        Assert.ThrowsExactly<ArgumentException>(() => _secrets.Set("../x", "s"));
    }

    private static ConnectionOptions Options() => new() { Dropbox = new() { AppKey = Key } };

    private static SignInStart Start(SignInKind kind, string? back) =>
        new(kind, Options(), back, "the-state", new Dictionary<string, string>());

    private static SignInCompletion Completion(SignInKind kind, string? pending, string? back) =>
        new(kind, Options(), pending, back, new Dictionary<string, string> { ["code"] = "the-code" });
}
