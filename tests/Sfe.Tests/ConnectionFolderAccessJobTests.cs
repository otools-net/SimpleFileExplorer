using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The folder rules of a connection on what carries its files elsewhere (<see cref="RuledConnection"/>): uploads
/// into a hidden folder are not found and a hidden name is taken, zips never carry a hidden item nor name it,
/// transfers check the target folder at once and every item as the job goes, with the access read again when a
/// rule changes.
/// </summary>
[TestClass]
public sealed class ConnectionFolderAccessJobTests
{
    private RuledConnection _world = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Cancel => TestContext.CancellationToken;

    private HttpClient Bob => _world.Bob;

    [TestInitialize]
    public async Task InitializeAsync() => _world = await RuledConnection.CreateAsync(Cancel);

    [TestCleanup]
    public async Task CleanupAsync() => await _world.DisposeAsync();

    [TestMethod]
    public async Task AnUploadIntoAHiddenFolderIsNotFoundAndAHiddenNameIsTaken()
    {
        using var hidden = await StartUploadAsync("Private", "x.txt");
        using var below = await StartUploadAsync("", "PRIVATE/Inner/x.txt");
        using var readOnly = await StartUploadAsync("ReadOnly", "x.txt");
        using var name = await StartUploadAsync("", "private");
        using var check = await PostAsync("files/uploads/check", new UploadCheckRequest("", ["Private", "new.txt"]));
        using var fine = await StartUploadAsync("Lead/Open", "x.txt");

        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, below.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, readOnly.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, name.StatusCode);
        var taken = (await check.Content.ReadFromJsonAsync<UploadCheck>(RuledConnection.Json, Cancel))!;
        CollectionAssert.AreEqual(new[] { "Private" }, taken.Existing.ToArray());
        Assert.AreEqual(HttpStatusCode.Created, fine.StatusCode);
        Assert.AreEqual(1, _world.Account.Calls.Count(c => c.StartsWith("upload-begin", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AnUploadStopsWhenItsFolderIsHiddenMeanwhile()
    {
        using var started = await StartUploadAsync("Public", "x.txt");
        var upload = (await started.Content.ReadFromJsonAsync<UploadInfo>(RuledConnection.Json, Cancel))!;
        await _world.SetRulesAsync(Cancel, _world.Rule("Public", FolderLevel.Hidden));

        using var chunk = await Bob.PutAsync(
            $"{_world.Base}/files/uploads/{upload.Id}?offset=0",
            new ByteArrayContent("x"u8.ToArray()),
            Cancel
        );

        Assert.AreEqual(HttpStatusCode.NotFound, chunk.StatusCode);
        Assert.IsFalse(_world.Called("upload-append"));
    }

    [TestMethod]
    public async Task AZipNeverCarriesNorNamesAHiddenItem()
    {
        using var hidden = await PostAsync("archive", new ArchiveRequest(["Public/a.txt", "Private"]));
        using var inside = await PostAsync("archive", new ArchiveRequest(["mixed/HIDDEN/h.txt"]));

        var entries = await ZipEntriesAsync([""]);
        var mixed = await ZipEntriesAsync(["Mixed"]);

        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, inside.StatusCode);
        string[] expected =
        [
            "Shared/",
            "Shared/Lead/",
            "Shared/Lead/Open/",
            "Shared/Lead/Open/o.txt",
            "Shared/Mixed/",
            "Shared/Mixed/ok.txt",
            "Shared/Public/",
            "Shared/Public/a.txt",
            "Shared/Public/b.jpg",
            "Shared/ReadOnly/",
            "Shared/ReadOnly/r.txt",
        ];
        CollectionAssert.AreEquivalent(expected, entries);
        CollectionAssert.AreEquivalent(new[] { "Mixed/", "Mixed/ok.txt" }, mixed);
        Assert.IsFalse(_world.Called("secret") || _world.Called("h.txt") || _world.Called("l.txt"));
    }

    [TestMethod]
    public async Task AZipIsCheckedAgainWhenItIsDownloaded()
    {
        using var made = await PostAsync("archive", new ArchiveRequest(["Public"]));
        var link = (await made.Content.ReadFromJsonAsync<ArchiveLink>(RuledConnection.Json, Cancel))!;
        await _world.SetRulesAsync(Cancel, _world.Rule("Public", FolderLevel.Hidden));

        using var download = await Bob.GetAsync(link.Url, Cancel);

        Assert.AreEqual(HttpStatusCode.NotFound, download.StatusCode);
        Assert.IsFalse(_world.Called("read"));
    }

    [TestMethod]
    public async Task ATransferCarriesNoHiddenItemAndChecksItsTargetAtOnce()
    {
        using var intoHidden = await TransferAsync(null, ["RFile1.txt"], _world.Id, "Private");
        using var intoRead = await TransferAsync(null, ["RFile1.txt"], _world.Id, "readonly");
        using var outOfHidden = await TransferAsync(_world.Id, ["Private", "Mixed", "Lead/l.txt"], null, "ChildDir");
        var job = await FinishedAsync(outOfHidden);

        Assert.AreEqual(HttpStatusCode.NotFound, intoHidden.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, intoRead.StatusCode);
        CollectionAssert.AreEqual(
            new[] { FileOperationStatus.NotFound, FileOperationStatus.Done, FileOperationStatus.NotFound },
            job.Results.Select(r => r.Status).ToArray()
        );
        Assert.IsTrue(File.Exists(_world.Library.FullPath("ChildDir/Mixed/ok.txt")));
        Assert.IsFalse(Directory.Exists(_world.Library.FullPath("ChildDir/Mixed/Hidden")));
        Assert.IsFalse(Directory.Exists(_world.Library.FullPath("ChildDir/Private")));
        Assert.IsFalse(_world.Called("secret") || _world.Called("h.txt") || _world.Called("Lead"));
    }

    [TestMethod]
    public async Task AJobReadsTheAccessAgainOnceARuleChanged()
    {
        var subject = new ConnectionJobSubject(
            (await _world.Get<AccessService>().GetTenantsAsync(Cancel)).Single(),
            _world.BobId,
            _world.Id,
            StringComparison.OrdinalIgnoreCase
        );
        var services = new ConnectionJobServices(
            _world.Get<AccessService>(),
            _world.Get<RightsEvaluator>(),
            _world.Get<ConnectionAccess>(),
            _world.Get<AccessChanges>()
        );
        var access = new ConnectionJobAccess(services, subject);

        var before = await access.FoldersAsync(Cancel);
        await _world.SetRulesAsync(Cancel, _world.Rule("Public", FolderLevel.Hidden));
        var after = await access.FoldersAsync(Cancel);
        await _world.ShareAsync([], Cancel);
        var gone = await access.FoldersAsync(Cancel);

        Assert.IsTrue(before!.Sees("Public/a.txt"));
        Assert.IsFalse(after!.Sees("public/a.txt"));
        Assert.IsNull(gone, "no group of his uses the connection any more");
    }

    private Task<HttpResponseMessage> StartUploadAsync(string target, string name) =>
        PostAsync("files/uploads", new UploadStart(target, name, 1));

    private Task<HttpResponseMessage> PostAsync<T>(string route, T body) =>
        Bob.PostAsJsonAsync($"{_world.Base}/{route}", body, RuledConnection.Json, Cancel);

    private async Task<string[]> ZipEntriesAsync(IReadOnlyList<string?> paths)
    {
        using var made = await PostAsync("archive", new ArchiveRequest(paths));
        Assert.AreEqual(HttpStatusCode.OK, made.StatusCode, await made.Content.ReadAsStringAsync(Cancel));
        var link = (await made.Content.ReadFromJsonAsync<ArchiveLink>(RuledConnection.Json, Cancel))!;
        var bytes = await Bob.GetByteArrayAsync(link.Url, Cancel);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.IsNull(zip.GetEntry(Archives.LeftOutEntry), "nothing hidden is named");
        return [.. zip.Entries.Select(e => e.FullName)];
    }

    private Task<HttpResponseMessage> TransferAsync(int? from, IReadOnlyList<string> paths, int? to, string target)
    {
        var request = new TransferRequest("copy", new TransferSide(from, paths), new TransferTarget(to, target));
        return Bob.PostAsJsonAsync($"{V1}/files/jobs/transfer", request, RuledConnection.Json, Cancel);
    }

    private async Task<FileJobView> FinishedAsync(HttpResponseMessage started)
    {
        Assert.AreEqual(HttpStatusCode.Accepted, started.StatusCode, await started.Content.ReadAsStringAsync(Cancel));
        var id = (await started.Content.ReadFromJsonAsync<FileJobView>(RuledConnection.Json, Cancel))!.Id;
        var until = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var job = (await Bob.GetFromJsonAsync<FileJobView>($"{V1}/files/jobs/{id}", RuledConnection.Json, Cancel))!;
            if (job.State is FileJobState.Completed or FileJobState.Cancelled)
                return job;
            Assert.IsLessThan(until, DateTime.UtcNow, $"Job {id} is {job.State}");
            await Task.Delay(20, Cancel);
        }
    }
}
