using Otools.Net.Sfe.Client.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests;

/// <summary>
/// The sample program (<c>samples/Sfe.Client.Sample</c>) run in process against the real server, as a smoke test:
/// what the docs show keeps working.
/// </summary>
[TestClass]
public sealed class SampleTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task TheSampleListsUploadsDownloadsAndMovesTwiceInARow()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var folder = Sample.SampleRun.DefaultFolder;

        using var first = new StringWriter();
        await Sample.SampleRun.RunAsync(sfe, folder, first, Token);
        using var second = new StringWriter();
        await Sample.SampleRun.RunAsync(sfe, folder, second, Token);

        StringAssert.Contains(first.ToString(), "Read back: Hello from Sfe.Client");
        StringAssert.Contains(first.ToString(), "  RFile1.txt (10 bytes)");
        StringAssert.Contains(second.ToString(), "  hello.txt (");
        Assert.IsTrue(File.Exists(server.Library.FullPath($"{folder}/Archive/hello.txt")));
        Assert.IsFalse(File.Exists(server.Library.FullPath($"{folder}/hello.txt")));
    }

    [TestMethod]
    public async Task WithoutAServerTheSampleTellsHowToRunIt()
    {
        if (Environment.GetEnvironmentVariable("SFE_URL") is not null)
            Assert.Inconclusive("SFE_URL is set: the sample would call that server.");

        var exitCode = await Sample.Program.Main([]);

        Assert.AreEqual(2, exitCode);
    }
}
