using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Otools.Net.Sfe.Client.Tests;

/// <summary>
/// The generated layer is committed (<c>src/Sfe.Client/Generated</c>): it must be what Kiota makes of the committed v1
/// document, so a change of the document (or of the generator's options) comes with its regenerated code.
/// </summary>
[TestClass]
public sealed class GenerationTests
{
    private static readonly TimeSpan GenerationTimeout = TimeSpan.FromMinutes(3);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RegeneratingTheClientChangesNothing()
    {
        var script = Path.Combine(RepositoryRoot(), "agents", "tools", "client-generation", "generate.mjs");

        var (exitCode, output) = await RunNodeAsync(script, "--check");

        Assert.AreEqual(
            0,
            exitCode,
            $"""
            The committed client differs from what the v1 document gives: run
            `node agents/tools/client-generation/generate.mjs`.
            {output}
            """
        );
    }

    private async Task<(int ExitCode, string Output)> RunNodeAsync(string script, string argument)
    {
        var start = new ProcessStartInfo("node")
        {
            ArgumentList = { script, argument },
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        timeout.CancelAfter(GenerationTimeout);
        var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await standardOutput + await standardError);
    }

    /// <summary>The repository's root: two folders above this file's.</summary>
    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
