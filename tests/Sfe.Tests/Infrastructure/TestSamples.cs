using System.Runtime.CompilerServices;

namespace Sfe.Tests.Infrastructure;

/// <summary>
/// The test media kept in the repository (<c>tests/samples/media</c>, listed with how ffmpeg makes them in
/// <c>tests/samples/media.json</c>): copied into a test's library instead of being rendered by each run. A new one is
/// added to <c>media.json</c> and made by <c>agents/tools/samples/update-samples.mjs</c> (tests/samples/README.md).
/// </summary>
public static class TestSamples
{
    public static string Folder { get; } = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", "..", "samples", "media"));

    /// <summary>The sample's path in the repository (to read it where it is).</summary>
    public static string PathOf(string name)
    {
        var path = Path.Combine(Folder, name);
        if (!File.Exists(path))
            Assert.Fail($"No sample {name} in {Folder}: add it to tests/samples/media.json and run agents/tools/samples/update-samples.mjs.");
        return path;
    }

    /// <summary>The tests' archives that need another program (7z), made by <c>agents/tools/samples/make-archives.mjs</c>.</summary>
    public static string ArchivesFolder { get; } = Path.GetFullPath(Path.Combine(Folder, "..", "archives"));

    /// <summary>An archive sample's path in the repository (read in place: nothing writes to it).</summary>
    public static string ArchiveOf(string name)
    {
        var path = Path.Combine(ArchivesFolder, name);
        if (!File.Exists(path))
            Assert.Fail($"No archive {name} in {ArchivesFolder}: make it in agents/tools/samples/make-archives.mjs and run update-samples.mjs.");
        return path;
    }

    /// <summary>
    /// A file taken from another project's tests (<c>tests/samples/third-party/</c>, its README: source, commit,
    /// licence), as <c>project/file</c>; read in place.
    /// </summary>
    public static string ThirdPartyOf(string name)
    {
        var path = Path.GetFullPath(Path.Combine(Folder, "..", "third-party", name));
        if (!File.Exists(path))
            Assert.Fail($"No third-party sample {name}: see tests/samples/third-party/README.md.");
        return path;
    }

    /// <summary>
    /// Copies the sample to <paramref name="destination"/> (folders made, a file there replaced), dated now, as a file
    /// just written; answers <paramref name="destination"/>.
    /// </summary>
    public static string CopyTo(string name, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(PathOf(name), destination, overwrite: true);
        File.SetLastWriteTimeUtc(destination, DateTime.UtcNow);
        return destination;
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;
}
