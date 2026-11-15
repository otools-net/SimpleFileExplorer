using System.Runtime.CompilerServices;

namespace Otools.Net.Sfe.Client.Tests.Compatibility;

/// <summary>
/// The package's public surface (the facade: types, members, signatures) is compared with the snapshot committed in
/// PublicApi.txt: a change a program built against the package would notice fails here until the snapshot is written
/// again in the same commit, on purpose. The generated client (<c>Raw</c>) is left out: it follows the v1 document,
/// kept by the OpenAPI baseline and the regeneration check.
/// </summary>
[TestClass]
public sealed class PublicApiTests
{
    /// <summary>The variable that writes the snapshot instead of comparing (<c>write</c>).</summary>
    public const string UpdateVariable = "SFE_PUBLIC_API";

    public const string UpdateCommand =
        $"{UpdateVariable}=write dotnet test tests/Sfe.Client.Tests --filter FullyQualifiedName~PublicApiTests";

    [TestMethod]
    public void ThePublicSurfaceIsTheSnapshots()
    {
        var current = PublicApiWriter.Write(typeof(SfeClient).Assembly, "Otools.Net.Sfe.Client.Generated");
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "write")
            File.WriteAllText(SnapshotPath(), current);

        var snapshot = File.ReadAllText(SnapshotPath()).ReplaceLineEndings("\n");

        var removed = Lines(snapshot).Except(Lines(current)).ToList();
        var added = Lines(current).Except(Lines(snapshot)).ToList();
        Assert.AreEqual(
            0,
            removed.Count + added.Count,
            "The public surface of Sfe.Client differs from tests/Sfe.Client.Tests/Compatibility/PublicApi.txt."
                + $"\nGone or changed (breaks programs built against the package):\n  {string.Join("\n  ", removed)}"
                + $"\nNew:\n  {string.Join("\n  ", added)}"
                + "\nKeep what programs use (add an overload, a property) unless the break is meant and noted in "
                + $"CHANGELOG.md; then write the snapshot again in the same commit: {UpdateCommand}"
        );
    }

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static string SnapshotPath([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "PublicApi.txt");
}
