using System.Security.Cryptography;
using Otools.Net.Sfe.Client.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary>
/// Uploads to the real server: in chunks with progress, name clashes, a large file with one chunk in memory, going on
/// after a failure, cancelling, whole folders.
/// </summary>
[TestClass]
public sealed class UploadTests
{
    private const int SmallChunk = 64 * 1024;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task AFileIsUploadedInChunksWithItsDateAndProgress()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient(options => options.UploadChunkBytes = SmallChunk);
        var bytes = RandomNumberGenerator.GetBytes(3 * SmallChunk + 5);
        var modified = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);
        var reported = new List<long>();
        var options = new SfeUploadOptions { Modified = modified, Progress = new SyncProgress<long>(reported) };

        var item = await sfe.Files.UploadAsync("ChildDir/new.bin", new MemoryStream(bytes), options, Token);

        var full = server.Library.FullPath("ChildDir/new.bin");
        Assert.AreEqual("ChildDir/new.bin", item?.Path);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(full, Token));
        Assert.AreEqual(modified.UtcDateTime, File.GetLastWriteTimeUtc(full));
        CollectionAssert.AreEqual(new long[] { 0, SmallChunk, 2 * SmallChunk, 3 * SmallChunk, bytes.Length }, reported);
    }

    [TestMethod]
    public async Task ATakenNameIsRefusedBeforeSendingWhenAsking()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();

        var refused = await Assert.ThrowsExactlyAsync<SfeConflictException>(() =>
            sfe.Files.UploadAsync("ChildDir/AFile1.txt", new MemoryStream([1, 2, 3]), cancellationToken: Token)
        );

        Assert.AreEqual("file-exists", refused.ProblemType);
        Assert.AreEqual(10, new FileInfo(server.Library.FullPath("ChildDir/AFile1.txt")).Length);
    }

    [TestMethod]
    [DataRow(SfeConflictChoice.Skip, "ChildDir/AFile1.txt", 10)]
    [DataRow(SfeConflictChoice.Replace, "ChildDir/AFile1.txt", 3)]
    [DataRow(SfeConflictChoice.KeepBoth, "ChildDir/AFile1 (2).txt", 3)]
    public async Task ATakenNameIsHandledAsChosen(SfeConflictChoice choice, string kept, int keptLength)
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var options = new SfeUploadOptions { Conflicts = choice };

        var item = await sfe.Files.UploadAsync("ChildDir/AFile1.txt", new MemoryStream([1, 2, 3]), options, Token);

        Assert.AreEqual(choice == SfeConflictChoice.Skip ? null : kept, item?.Path);
        Assert.AreEqual(keptLength, new FileInfo(server.Library.FullPath(kept)).Length);
    }

    [TestMethod]
    public async Task ALargeStreamIsUploadedWithOneChunkInMemory()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        const long Size = 3L * SfeClientOptions.DefaultChunkBytes + 12345;
        var source = new GeneratedStream(Size);
        long readAhead = 0;
        var progress = new SyncCallback(sent => readAhead = Math.Max(readAhead, source.BytesRead - sent));
        var options = new SfeUploadOptions { Size = Size, Progress = progress };

        await sfe.Files.UploadAsync("big.bin", source, options, Token);

        Assert.IsLessThanOrEqualTo(SfeClientOptions.DefaultChunkBytes, readAhead);
        var full = server.Library.FullPath("big.bin");
        Assert.AreEqual(Size, new FileInfo(full).Length);
        Assert.IsTrue(await HasGeneratedBytesAsync(full));
    }

    [TestMethod]
    public async Task AnUploadThatFailedGoesOnFromWhatTheServerHas()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient(options => options.UploadChunkBytes = SmallChunk);
        var bytes = RandomNumberGenerator.GetBytes(4 * SmallChunk);
        var modified = DateTimeOffset.UtcNow.AddDays(-1);
        await Assert.ThrowsExactlyAsync<IOException>(() =>
            sfe.Files.UploadAsync(
                "again.bin",
                new FailingStream(bytes, 2 * SmallChunk + 1),
                new SfeUploadOptions { Modified = modified },
                Token
            )
        );
        var reported = new List<long>();

        var options = new SfeUploadOptions { Modified = modified, Progress = new SyncProgress<long>(reported) };
        await sfe.Files.UploadAsync("again.bin", new MemoryStream(bytes), options, Token);

        Assert.AreEqual(2L * SmallChunk, reported[0]);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(server.Library.FullPath("again.bin"), Token));
    }

    [TestMethod]
    public async Task ACancelledUploadIsRemovedFromTheServer()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient(options => options.UploadChunkBytes = SmallChunk);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var progress = new SyncCallback(sent =>
        {
            if (sent > 0)
                cancel.Cancel();
        });
        var options = new SfeUploadOptions { Progress = progress };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sfe.Files.UploadAsync("stopped.bin", new MemoryStream(new byte[4 * SmallChunk]), options, cancel.Token)
        );

        var unfinished = await server.Bearer.GetStringAsync($"{V1}/files/uploads", Token);
        Assert.AreEqual("[]", unfinished);
        Assert.IsFalse(File.Exists(server.Library.FullPath("stopped.bin")));
    }

    [TestMethod]
    public async Task AFolderIsUploadedWithItsFolders()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var local = Directory.CreateTempSubdirectory("sfe-client-upload").FullName;
        var folder = Directory.CreateDirectory(Path.Combine(local, "Holiday")).FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, "a.txt"), "first", Token);
        Directory.CreateDirectory(Path.Combine(folder, "Day 2"));
        await File.WriteAllTextAsync(Path.Combine(folder, "Day 2", "b.txt"), "second", Token);
        var reported = new List<long>();

        try
        {
            var options = new SfeUploadOptions { Progress = new SyncProgress<long>(reported) };
            var items = await sfe.Files.UploadFolderAsync(folder, "ChildDir", options, Token);

            CollectionAssert.AreEquivalent(
                new[] { "ChildDir/Holiday/a.txt", "ChildDir/Holiday/Day 2/b.txt" },
                items.Select(item => item.Path).ToArray()
            );
            Assert.AreEqual(
                "second",
                await File.ReadAllTextAsync(server.Library.FullPath("ChildDir/Holiday/Day 2/b.txt"), Token)
            );
            Assert.AreEqual(11L, reported[^1]);
        }
        finally
        {
            Directory.Delete(local, recursive: true);
        }
    }

    private static async Task<bool> HasGeneratedBytesAsync(string path)
    {
        await using var file = File.OpenRead(path);
        var buffer = new byte[1 << 20];
        long index = 0;
        int read;
        while ((read = await file.ReadAsync(buffer)) > 0)
        {
            for (var i = 0; i < read; i++, index++)
            {
                if (buffer[i] != GeneratedStream.ByteAt(index))
                    return false;
            }
        }
        return true;
    }

    /// <summary>Calls back at once with each value reported.</summary>
    private sealed class SyncCallback(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
