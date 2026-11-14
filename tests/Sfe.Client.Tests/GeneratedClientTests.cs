using Otools.Net.Sfe.Client.Generated.Models;
using Otools.Net.Sfe.Client.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests;

/// <summary>
/// The generated layer (<c>SfeRawClient</c>) against the real server: v1 routes called with a personal access token
/// only, answers read into the generated models.
/// </summary>
[TestClass]
public sealed class GeneratedClientTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task ATokenListsAFolderThroughTheGeneratedClient()
    {
        using var server = await TokenServer.StartAsync(Token);
        var raw = server.CreateRawClient();

        var listing = await raw.Api.V1.Browse.GetAsync(request => request.QueryParameters.Path = "ChildDir", Token);

        Assert.IsNotNull(listing);
        Assert.AreEqual("ChildDir", listing.Path);
        CollectionAssert.AreEquivalent(
            new[] { "AFile1.txt", "AFile2" },
            listing.Items!.Select(item => item.Name).ToArray()
        );
        Assert.IsTrue(listing.Items!.All(item => item.Kind is { } kind && kind != ItemKind.Directory));
    }

    [TestMethod]
    public async Task ATokenChangesSomethingWithoutTheRequestHeaderOfBrowsers()
    {
        using var server = await TokenServer.StartAsync(Token);
        var raw = server.CreateRawClient();

        var created = await raw.Api.V1.Tags.PostAsync(new TagInput { Name = "Holiday" }, cancellationToken: Token);
        var found = await raw.Api.V1.Tags.GetAsync(request => request.QueryParameters.Q = "Holiday", Token);

        Assert.AreEqual("Holiday", created?.Name);
        Assert.IsTrue(found?.Matches?.Any(tag => tag.Id == created!.Id));
    }
}
