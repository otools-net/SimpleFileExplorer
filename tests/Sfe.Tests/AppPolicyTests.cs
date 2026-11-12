using Sfe.Api.Security;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Where the app's pages may load pictures and media from (audit 2026-11, A11).</summary>
[TestClass]
public sealed class AppPolicyTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void WithoutADataServerPicturesAndMediaComeFromTheAppOnly()
    {
        var policy = SecurityHeaders.AppPolicyFor(null);

        StringAssert.Contains(policy, "img-src 'self' data: blob:;");
        StringAssert.Contains(policy, "media-src 'self' blob:;");
        Assert.IsFalse(policy.Contains("http", StringComparison.Ordinal), policy);
        Assert.AreEqual(policy, SecurityHeaders.AppPolicy);
    }

    [TestMethod]
    [DataRow("http://nas:8080", "http://nas:8080")]
    [DataRow("https://files.example.com/media", "https://files.example.com")]
    [DataRow("http://nas/get?file={encodedUrl}&raw=1", "http://nas")]
    [DataRow("https://nas:8443/{url}", "https://nas:8443")]
    public void ADataServerAddsItsOriginAndNoOther(string dataServer, string origin)
    {
        var policy = SecurityHeaders.AppPolicyFor(dataServer);

        StringAssert.Contains(policy, $"img-src 'self' data: blob: {origin};");
        StringAssert.Contains(policy, $"media-src 'self' blob: {origin};");
        Assert.IsFalse(policy.Contains("http: https:", StringComparison.Ordinal), policy);
        StringAssert.Contains(policy, "script-src 'self';");
    }

    [TestMethod]
    [DataRow("{url}")]
    [DataRow("http://{url}")]
    public void ADataServerWithoutAFixedOriginKeepsEveryOrigin(string dataServer)
    {
        var policy = SecurityHeaders.AppPolicyFor(dataServer);

        StringAssert.Contains(policy, "img-src 'self' data: blob: http: https:;");
    }

    [TestMethod]
    public async Task TheAppsPagesCarryThePolicyOfTheConfiguredDataServer()
    {
        using var library = new TestLibrary();
        await using var factory = ApiClients.CreateFactory(library, settings => settings["Sfe:DataServer"] = "http://nas:8080/files");
        using var visitor = ApiClients.Create(factory);

        using var page = await visitor.GetAsync("/api/session", TestContext.CancellationToken);

        Assert.AreEqual(
            SecurityHeaders.AppPolicyFor("http://nas:8080/files"),
            string.Join(';', page.Headers.GetValues("Content-Security-Policy")));
    }
}
