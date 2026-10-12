using Microsoft.Extensions.Logging.Abstractions;
using Sfe.Core.Options;
using Sfe.Core.SystemCommands;

namespace Sfe.Tests;

[TestClass]
public sealed class SystemCommandTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RunsTheDismountCommandThroughTheShell()
    {
        // Quotes, backslashes and shell expansions reach the shell untouched.
        var (command, expected) = OperatingSystem.IsWindows()
            ? ("echo dismounted & echo second", "dismounted \r\nsecond")
            : ("""printf '%s|' "a\\b" 'c d' $((1+1))""", @"a\b|c d|2|");

        var output = await Create(new SystemCommandOptions { DismountCommand = command }).DismountAsync(TestContext.CancellationToken);

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    public async Task ReportsFailures()
    {
        var output = await Create(new SystemCommandOptions { DismountCommand = "exit 3" }).DismountAsync(TestContext.CancellationToken);

        StringAssert.Contains(output, "exit code 3");
    }

    [TestMethod]
    public void ShutdownIsOptInAndHasADefaultCommandOnEveryPlatform()
    {
        Assert.IsFalse(Create(new SystemCommandOptions()).CanShutdown);
        Assert.IsTrue(Create(new SystemCommandOptions { EnableShutdown = true }).CanShutdown);
        Assert.IsFalse(Create(new SystemCommandOptions()).CanDismount);
    }

    private static SystemCommandService Create(SystemCommandOptions system) =>
        new(Microsoft.Extensions.Options.Options.Create(new SfeOptions { Root = Path.GetTempPath(), System = system }),
            NullLogger<SystemCommandService>.Instance);
}
