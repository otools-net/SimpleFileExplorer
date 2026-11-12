using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Sfe.Api.Security;
using Sfe.Core.Files;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>A file of the library is never a script or a style sheet of the app's pages (audit 2026-11, A3).</summary>
[TestClass]
public sealed class LibraryContentTests
{
    private const string Script = "fetch('api/access/users')";

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients.CreateFactory(_library);
        _client = await ApiClients.SetUpAsync(_factory, TestContext.CancellationToken);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    [DataRow("text/javascript")]
    [DataRow("application/javascript")]
    [DataRow("application/x-javascript")]
    [DataRow("application/ecmascript")]
    [DataRow("application/wasm")]
    [DataRow("text/css")]
    [DataRow("TEXT/JavaScript")]
    public void TypesAPageCouldRunBecomeADownloadType(string type)
    {
        Assert.AreEqual("application/octet-stream", LibraryContent.SafeType(type));
    }

    [TestMethod]
    [DataRow("image/jpeg")]
    [DataRow("video/mp4")]
    [DataRow("text/plain")]
    [DataRow("application/json")]
    [DataRow("application/pdf")]
    public void OtherTypesAreKept(string type)
    {
        Assert.AreEqual(type, LibraryContent.SafeType(type));
    }

    [TestMethod]
    [DataRow("evil.js")]
    [DataRow("evil.mjs")]
    [DataRow("evil.css")]
    [DataRow("evil.wasm")]
    public async Task ScriptsAndStylesAreServedAsDownloadsOfNoRunnableType(string name)
    {
        File.WriteAllText(_library.FullPath(name), Script);

        using var response = await _client.GetAsync($"/api/files/raw/{name}", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.AreEqual("nosniff", string.Join(',', response.Headers.GetValues("X-Content-Type-Options")));
        Assert.AreEqual(Script, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    [DataRow("script")]
    [DataRow("style")]
    [DataRow("worker")]
    [DataRow("sharedworker")]
    [DataRow("serviceworker")]
    [DataRow("embed")]
    [DataRow("object")]
    public async Task AFileLoadedAsAScriptOrAStyleIsNotFound(string destination)
    {
        File.WriteAllText(_library.FullPath("evil.js"), Script);

        using var response = await GetAsAsync("/api/files/raw/evil.js", destination);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    [DataRow("image")]
    [DataRow("video")]
    [DataRow("document")]
    [DataRow("empty")]
    public async Task PicturesPlayersAndDownloadsStillLoadFiles(string destination)
    {
        using var response = await GetAsAsync("/api/files/raw/Media/photo.jpg", destination);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task ASharedLinkIsNotAScriptEither()
    {
        File.WriteAllText(_library.FullPath("evil.js"), Script);
        using var created = await _client.PostAsJsonAsync(
            "/api/files/links", new Sfe.Api.Endpoints.FileLinkRequest("evil.js"), TestContext.CancellationToken);
        var link = (await created.Content.ReadFromJsonAsync<FileLink>(TestContext.CancellationToken))!;

        using var asScript = await GetAsAsync("/" + link.Url, "script");
        using var asDownload = await GetAsAsync("/" + link.Url, "empty");

        Assert.AreEqual(HttpStatusCode.NotFound, asScript.StatusCode);
        Assert.AreEqual("application/octet-stream", asDownload.Content.Headers.ContentType?.MediaType);
    }

    [TestMethod]
    public async Task ABooksStylesLoadButNothingOfItRuns()
    {
        using (var book = ZipFile.Open(_library.FullPath("book.epub"), ZipArchiveMode.Create))
        {
            WriteEntry(book, "style.css", "p { color: red }");
            WriteEntry(book, "evil.js", Script);
        }
        var token = EpubEntries.Token("book.epub");

        using var style = await GetAsAsync($"/api/files/epub/{token}/style.css", "style");
        using var script = await GetAsAsync($"/api/files/epub/{token}/evil.js", "script");
        using var worker = await GetAsAsync($"/api/files/epub/{token}/evil.js", "worker");

        Assert.AreEqual(HttpStatusCode.OK, style.StatusCode);
        Assert.AreEqual("text/css", style.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual(HttpStatusCode.NotFound, script.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, worker.StatusCode);
    }

    private static void WriteEntry(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(text);
    }

    /// <summary>A GET as a browser sends it when it loads <paramref name="url"/> for <paramref name="destination"/>.</summary>
    private async Task<HttpResponseMessage> GetAsAsync(string url, string destination)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Sec-Fetch-Dest", destination);
        return await _client.SendAsync(request, TestContext.CancellationToken);
    }
}
