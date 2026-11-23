using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Scan as you go through <c>api/browse</c>: the first page of a folder has it indexed in the background (that folder
/// only), a later page doesn't.
/// </summary>
[TestClass]
public sealed class ListingIndexerRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients.CreateFactory(_library, settings => settings["Sfe:Scan:IndexWhenListed"] = "true");
        _client = await ApiClients.SetUpAsync(_factory, Token);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task TheFirstPageOfAFolderHasItIndexed()
    {
        var listing = await GetAsync<DirectoryListing>($"{V1}/browse?path=RepeatedDir");
        // Listed at once, as the index was: the indexing comes after.
        Assert.IsTrue(listing.Items.Where(i => i.Kind != ItemKind.Directory).All(i => i.Indexed == false));

        var folder = await WaitUntilIndexedAsync("RepeatedDir");
        Assert.AreEqual(2, folder.Files);
        Assert.AreEqual(1, folder.Directories); // the folder itself, not those inside it

        var again = await GetAsync<DirectoryListing>($"{V1}/browse?path=RepeatedDir");
        Assert.IsTrue(again.Items.Where(i => i.Kind != ItemKind.Directory).All(i => i.Indexed == true));
    }

    [TestMethod]
    public async Task ALaterPageDoesNot()
    {
        await GetAsync<DirectoryListing>($"{V1}/browse?path=RepeatedDir&offset=1&limit=2");
        await Task.Delay(TimeSpan.FromSeconds(1), Token);

        Assert.IsFalse((await GetAsync<IndexedFolder>($"{V1}/index/folder?path=RepeatedDir")).Indexed);
    }

    private async Task<IndexedFolder> WaitUntilIndexedAsync(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var folder = await GetAsync<IndexedFolder>($"{V1}/index/folder?path={path}");
            if ((folder.Indexed && folder.Files > 0) || DateTime.UtcNow > deadline)
                return folder;
            await Task.Delay(100, Token);
        }
    }

    private async Task<T> GetAsync<T>(string url) => (await _client.GetFromJsonAsync<T>(url, Json, Token))!;
}
