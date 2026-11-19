using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Dropbox;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Dropbox's thumbnails against recorded answers (<see cref="DropboxThumbnails"/>): one through
/// <c>get_thumbnail</c> with its <c>media_info</c>, several through <c>get_thumbnail_batch</c> (25 a call), files
/// Dropbox doesn't thumbnail as none. No test calls Dropbox.
/// </summary>
[TestClass]
public sealed class DropboxThumbnailTests
{
    private static readonly string Photo = """
        {".tag":"file","name":"a.jpg","path_lower":"/photos/a.jpg","path_display":"/Photos/a.jpg","id":"id:a",
         "client_modified":"2026-01-01T10:00:00Z","server_modified":"2026-01-02T10:00:00Z","rev":"015f01",
         "size":12,"is_downloadable":true,"content_hash":"hash1",
         "media_info":{".tag":"metadata","metadata":{".tag":"video","dimensions":{"height":1080,"width":1920},
         "duration":61500}}}
        """;

    private readonly RecordedDropbox _dropbox = new();
    private string _secretsFolder = null!;
    private IRemoteStorage _storage = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize()
    {
        _secretsFolder = Path.Combine(Path.GetTempPath(), $"sfe-secrets-{Guid.NewGuid():N}");
        var provider = new DropboxProvider(new OutboundPolicy(() => _dropbox), new ProviderSecrets(_secretsFolder));
        _storage = provider.Open("refresh-1", "Photos", new ConnectionOptions { Dropbox = new() { AppKey = "key1" } });
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _storage.DisposeAsync();
        _dropbox.Dispose();
    }

    [TestMethod]
    public async Task OneThumbnailTellsTheFilesSizeAndLength()
    {
        _dropbox.Handlers["/2/files/get_thumbnail"] = _ =>
        {
            var answer = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8]) };
            answer.Headers.Add("Dropbox-API-Result", Photo.Replace("\n", "").Replace("\r", ""));
            return answer;
        };

        var thumbnail = (await _storage.ThumbnailsAsync(["a.jpg"], 256, Token)).Single()!;

        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xD8 }, thumbnail.Bytes);
        Assert.AreEqual(new RemoteMedia(1920, 1080, 61.5), thumbnail.Media);
        var argument = _dropbox.Last("/2/files/get_thumbnail").Request.Headers.GetValues("Dropbox-API-Arg").Single();
        Assert.AreEqual("/Photos/a.jpg", JsonNode.Parse(argument)!["path"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task SeveralGoInBatchesOf25AndThoseDropboxDoesNotMakeAreNone()
    {
        var paths = Enumerable.Range(0, 30).Select(i => $"p{i}.jpg").ToList();
        _dropbox.Handlers["/2/files/get_thumbnail_batch"] = request =>
        {
            var asked = JsonNode.Parse(request.Content!.ReadAsStringAsync().Result)!["entries"]!.AsArray();
            var entries = asked.Select(
                (_, i) =>
                    i == 1
                        ? """{".tag":"failure","failure":{".tag":"unsupported_image"}}"""
                        : $$"""{".tag":"success","metadata":{{Photo}},"thumbnail":"{{Encoded("t")}}"}"""
            );
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"entries":[{{string.Join(",", entries)}}]}""", Encoding.UTF8),
            };
        };

        var thumbnails = await _storage.ThumbnailsAsync(paths, 256, Token);

        Assert.HasCount(30, thumbnails);
        Assert.AreEqual(2, _dropbox.Requests.Count(r => r.Request.RequestUri!.AbsolutePath.EndsWith("_batch")));
        Assert.IsNull(thumbnails[1], "Dropbox made none");
        Assert.AreEqual("t", Encoding.ASCII.GetString(thumbnails[0]!.Bytes));
        Assert.IsNull(thumbnails[26], "The second batch's second one");
    }

    [TestMethod]
    public async Task AFileDropboxDoesNotThumbnailHasNone()
    {
        _dropbox.Answers["/2/files/get_thumbnail"] = (
            HttpStatusCode.Conflict,
            """{"error_summary":"unsupported_extension/","error":{".tag":"unsupported_extension"}}"""
        );

        var thumbnails = await _storage.ThumbnailsAsync(["notes.txt"], 256, Token);

        Assert.IsNull(thumbnails.Single());
    }

    private static string Encoded(string text) => Convert.ToBase64String(Encoding.ASCII.GetBytes(text));
}
