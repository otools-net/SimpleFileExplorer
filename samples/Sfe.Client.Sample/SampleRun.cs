using System.Text;

namespace Otools.Net.Sfe.Client.Sample;

/// <summary>
/// What the sample does with a client: the server's version, a folder listed, a file uploaded into a folder of its
/// own, read back, and moved to a sub-folder.
/// </summary>
public static class SampleRun
{
    /// <summary>The library's folder the sample works in when none is given.</summary>
    public const string DefaultFolder = "Sfe.Client.Sample";

    /// <summary>Runs the sample with <paramref name="sfe"/>, telling what it does.</summary>
    /// <param name="sfe">A client of the server, with a token that may upload and move.</param>
    /// <param name="folder">The folder of the library the sample works in (made when missing).</param>
    /// <param name="output">Where it tells what it does.</param>
    /// <param name="cancellationToken">Stops it.</param>
    public static async Task RunAsync(
        SfeClient sfe,
        string folder,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var server = await sfe.Server.GetInfoAsync(cancellationToken);
        await output.WriteLineAsync($"Server {server.Version}");

        await ListAsync(sfe, "", output, cancellationToken);
        await MakeFolderAsync(sfe, folder, cancellationToken);

        var path = SfePath.Combine(folder, "hello.txt");
        var text = $"Hello from Sfe.Client, {DateTimeOffset.Now:u}";
        await UploadAsync(sfe, path, text, output, cancellationToken);

        var read = await DownloadAsync(sfe, path, cancellationToken);
        await output.WriteLineAsync($"Read back: {read}");

        var archive = SfePath.Combine(folder, "Archive");
        await MakeFolderAsync(sfe, archive, cancellationToken);
        await MoveAsync(sfe, path, archive, output, cancellationToken);
        await ListAsync(sfe, archive, output, cancellationToken);
    }

    private static async Task ListAsync(SfeClient sfe, string folder, TextWriter output, CancellationToken token)
    {
        await output.WriteLineAsync($"In /{folder}:");
        await foreach (var item in sfe.Files.ListAsync(folder, token))
        {
            var size = item.IsFolder ? "folder" : $"{item.Size:N0} bytes";
            await output.WriteLineAsync($"  {item.Name} ({size})");
        }
    }

    private static async Task MakeFolderAsync(SfeClient sfe, string folder, CancellationToken token)
    {
        if (!await sfe.Files.ExistsAsync(folder, token))
            await sfe.Files.CreateFolderAsync(folder, token);
    }

    private static async Task UploadAsync(
        SfeClient sfe,
        string path,
        string text,
        TextWriter output,
        CancellationToken token
    )
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(text));
        // Replace: a file of that name left by an earlier run is replaced.
        var options = new SfeUploadOptions { Conflicts = SfeConflictChoice.Replace };
        var uploaded = await sfe.Files.UploadAsync(path, content, options, token);
        await output.WriteLineAsync($"Uploaded {uploaded!.Path} ({uploaded.Size} bytes)");
    }

    private static async Task<string> DownloadAsync(SfeClient sfe, string path, CancellationToken token)
    {
        using var content = new MemoryStream();
        await sfe.Files.DownloadAsync(path, content, cancellationToken: token);
        return Encoding.UTF8.GetString(content.ToArray());
    }

    private static async Task MoveAsync(
        SfeClient sfe,
        string path,
        string folder,
        TextWriter output,
        CancellationToken token
    )
    {
        var options = new SfeJobOptions { Conflicts = SfeConflictChoice.Replace };
        var job = await sfe.Files.MoveAsync([path], folder, options, token);
        var done = await job.WaitAsync(cancellationToken: token);
        await output.WriteLineAsync($"Moved {path} to {folder}: {done.State}");
    }
}
