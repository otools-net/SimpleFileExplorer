using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// A stopped server lets go of the folders its configuration files watched: on Linux each watcher is an inotify
/// instance, 128 per user on the CI runners, and a test run starts hundreds of servers in one process.
/// </summary>
[TestClass]
[DoNotParallelize] // Counts the watchers of the whole process: no other test's server while it runs.
public sealed class ConfigurationWatcherTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task StoppedServersLeaveNoWatcherBehind()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Inconclusive("Counts inotify instances: Linux only.");

        using var library = new TestLibrary();
        var before = InotifyInstances();
        for (var i = 0; i < 5; i++)
        {
            await using var factory = ApiClients.CreateFactory(library);
            using var client = factory.CreateClient();
            using var _ = await client.GetAsync("/api/config", TestContext.CancellationToken);
        }

        Assert.IsLessThanOrEqualTo(before, InotifyInstances());
    }

    private static int InotifyInstances() =>
        new DirectoryInfo("/proc/self/fd").EnumerateFileSystemInfos().Count(fd =>
        {
            try
            {
                return fd.LinkTarget == "anon_inode:inotify";
            }
            catch (IOException)
            {
                return false; // Closed since it was listed.
            }
        });
}
