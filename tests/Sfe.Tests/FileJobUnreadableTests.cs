using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// A copy whose original the server's account may not read says so, not that it may not change files where the copy
/// goes (<see cref="UnreadableException"/>, agents/docs/features/move-and-copy.md).
/// </summary>
[TestClass]
public sealed class FileJobUnreadableTests
{
    private static readonly FileActor Me = new(7, "tester");

    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private UnreadableFile? _denied;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        _denied?.Dispose();
        await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AFileTheServerMayNotReadIsToldAsSuch()
    {
        Deny("Media/photo.jpg");

        var result = (await CopyAsync("Media/photo.jpg", "ChildDir")).Results.Single();

        Assert.AreEqual(FileOperationStatus.Failed, result.Status);
        Assert.AreEqual("“photo.jpg” could not be copied: the server's account may not read it.", result.Message);
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(_library.FullPath("ChildDir"), "photo.jpg*").Any());
    }

    [TestMethod]
    public async Task AFileInACopiedFolderIsNamed()
    {
        Deny("RepeatedDir/ChildDir/AFile1.txt");

        var result = (await CopyAsync("RepeatedDir", "Media")).Results.Single();

        Assert.AreEqual(FileOperationStatus.Failed, result.Status);
        Assert.AreEqual(
            "“RepeatedDir” could not be copied: the server's account may not read “RepeatedDir/ChildDir/AFile1.txt” "
                + "in it.",
            result.Message
        );
        Assert.IsFalse(Directory.Exists(_library.FullPath("Media/RepeatedDir")), "Nothing half copied stays");
    }

    private async Task<FileJobView> CopyAsync(string path, string target)
    {
        var board = _services.Get<JobBoard>();
        var view = _services
            .Get<FileJobs>()
            .Start(new FileJobRequest(FileJobKind.Copy, [path], target), Me, new AllowAll());
        await board.Completion(view.Id)!.WaitAsync(TimeSpan.FromSeconds(30), Token);
        return board.Find(view.Id)!;
    }

    private void Deny(string path) => _denied = UnreadableFile.Make(_library.FullPath(path));

    private sealed class AllowAll : IFileJobAccess
    {
        public Task<bool> MayAsync(string path, string right) => Task.FromResult(true);

        public Task<bool> MayThroughoutAsync(string path, string right) => Task.FromResult(true);

        public bool IsProtected(string fullPath) => false;

        public bool IsReadOnly(string fullPath) => false;
    }
}
