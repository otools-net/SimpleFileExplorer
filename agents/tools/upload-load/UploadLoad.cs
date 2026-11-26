#:project ../../../src/Sfe.Client/Sfe.Client.csproj
#:property PublishAot=false
#:property Nullable=enable
// Load test of uploads (agents/docs/features/uploads.md, release 2.0 step 4.3c): many small files sent a few at a
// time, then one large file, through Sfe.Client; each checked on the server (count, sizes, SHA-256 of the large one),
// no unfinished upload left; timings and, for the server it starts itself, its memory. See README.md.
//
//   dotnet run -c Release agents/tools/upload-load/UploadLoad.cs -- [--work <dir>] [--port 5097] [--skip-build]
//       [--url <server> --token <sfe_…>] [--folder Upload load] [--small 5000] [--small-bytes 4096] [--parallel 2]
//       [--large-mb 1024] [--keep]
//
// Never more than 8 GB in one file (owner's rule: agents/docs/development/testing.md).
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Otools.Net.Sfe.Client;

const long MaxLargeMegabytes = 8 * 1024;

var options = LoadOptions.Parse(args);
if (options.LargeMegabytes > MaxLargeMegabytes)
{
    Console.Error.WriteLine($"--large-mb {options.LargeMegabytes}: more than 8 GB in one file is refused (testing.md).");
    return 2;
}
Directory.CreateDirectory(options.Work);
Console.WriteLine($"work folder {options.Work}");

ScratchServer? server = null;
try
{
    var (url, token) = (options.Url, options.Token);
    if (url is null || token is null)
    {
        server = await ScratchServer.StartAsync(options);
        (url, token) = (server.Url, server.Token);
    }
    var timings = new RequestTimings { InnerHandler = new SocketsHttpHandler() };
    using var http = new HttpClient(timings) { Timeout = Timeout.InfiniteTimeSpan };
    using var sfe = new SfeClient(http, new SfeClientOptions { BaseAddress = new Uri(url), Token = token, MaxRetries = 5 });
    var report = new List<string>();
    var watch = server is null ? null : new MemoryWatch(server.Process);

    await LoadRuns.PrepareFolderAsync(sfe, options.Folder);
    if (options.Small > 0)
    {
        timings.Clear();
        report.Add(await LoadRuns.SmallFilesAsync(sfe, options, watch));
        report.AddRange(timings.Lines("small files"));
    }
    if (options.LargeMegabytes > 0)
    {
        timings.Clear();
        report.Add(await LoadRuns.LargeFileAsync(sfe, options, watch));
        report.AddRange(timings.Lines("large file"));
    }
    report.Add(await LoadRuns.LeftoversAsync(url, token));

    Console.WriteLine();
    Console.WriteLine("| Run | Result |");
    Console.WriteLine("|---|---|");
    foreach (var line in report)
        Console.WriteLine(line);
    return report.Any(line => line.Contains("FAILED", StringComparison.Ordinal)) ? 1 : 0;
}
finally
{
    server?.Dispose();
    if (!options.Keep)
        LoadOptions.Clean(options.Work);
}

/// <summary>The command line.</summary>
sealed record LoadOptions(
    string Work,
    int Port,
    bool SkipBuild,
    string? Url,
    string? Token,
    string Folder,
    int Small,
    int SmallBytes,
    int Parallel,
    long LargeMegabytes,
    bool Keep)
{
    public static LoadOptions Parse(string[] args)
    {
        string? Text(string name) => Array.IndexOf(args, name) is var i and >= 0 ? args[i + 1] : null;
        long Number(string name, long fallback) =>
            Text(name) is { } value ? long.Parse(value, CultureInfo.InvariantCulture) : fallback;
        return new LoadOptions(
            Path.GetFullPath(Text("--work") ?? Path.Combine(Path.GetTempPath(), "sfe-upload-load")),
            (int)Number("--port", 5097),
            args.Contains("--skip-build"),
            Text("--url"),
            Text("--token"),
            Text("--folder") ?? "Upload load",
            (int)Number("--small", 5000),
            (int)Number("--small-bytes", 4096),
            (int)Number("--parallel", 2),
            Number("--large-mb", 1024),
            args.Contains("--keep"));
    }

    /// <summary>What a run leaves in the work folder, the build kept (minutes to make again).</summary>
    public static void Clean(string work)
    {
        foreach (var name in new[] { "library", "data", "source" })
        {
            var folder = Path.Combine(work, name);
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}

/// <summary>The runs, each answering a line of the report.</summary>
static class LoadRuns
{
    public static async Task PrepareFolderAsync(SfeClient sfe, string folder)
    {
        if (await sfe.Files.ExistsAsync(folder))
            throw new InvalidOperationException($"\"{folder}\" is already in the library: choose another --folder.");
        await sfe.Files.CreateFolderAsync(folder);
    }

    /// <summary>
    /// Small files, <c>--parallel</c> at a time as the page sends them (two), into folders of 500 (as a folder upload
    /// makes them); then the folders listed back and counted.
    /// </summary>
    public static async Task<string> SmallFilesAsync(SfeClient sfe, LoadOptions options, MemoryWatch? watch)
    {
        var source = Path.Combine(options.Work, "source", "small");
        var files = SourceFiles.Small(source, options.Small, options.SmallBytes);
        var target = SfePath.Combine(options.Folder, "small");
        await sfe.Files.CreateFolderAsync(target);
        for (var folder = 0; folder < (files.Count + 499) / 500; folder++)
            await sfe.Files.CreateFolderAsync(SfePath.Combine(target, $"part {folder:D3}"));

        Console.WriteLine($"\n== {files.Count:N0} files of {options.SmallBytes:N0} bytes, {options.Parallel} at a time");
        watch?.Start();
        var times = new double[files.Count];
        var clock = Stopwatch.StartNew();
        var done = 0;
        await Parallel.ForAsync(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = options.Parallel },
            async (i, token) =>
            {
                var one = Stopwatch.StartNew();
                await using var stream = File.OpenRead(files[i]);
                var path = SfePath.Combine(target, $"part {i / 500:D3}", Path.GetFileName(files[i]));
                await sfe.Files.UploadAsync(path, stream, null, token);
                times[i] = one.Elapsed.TotalMilliseconds;
                if (Interlocked.Increment(ref done) % 500 == 0)
                    Console.WriteLine($"  {done:N0} in {clock.Elapsed.TotalSeconds:N1} s");
            });
        clock.Stop();
        var memory = watch?.Stop();

        var listed = 0L;
        await foreach (var folder in sfe.Files.ListAsync(target))
            await foreach (var item in sfe.Files.ListAsync(folder.Path))
                listed += item.Size == options.SmallBytes ? 1 : 0;
        var check = listed == files.Count ? $"{listed:N0} listed back" : $"FAILED: {listed:N0} listed back";
        Array.Sort(times);
        return $"| {files.Count:N0} small files ({options.Parallel} at a time) | {clock.Elapsed.TotalSeconds:N1} s, "
            + $"{files.Count / clock.Elapsed.TotalSeconds:N0} files/s; per file median {Percentile(times, 50):N0} ms, "
            + $"95th {Percentile(times, 95):N0} ms, slowest {times[^1]:N0} ms; {check}{memory} |";
    }

    /// <summary>One large file, its SHA-256 read on the server and compared with the one of the bytes made.</summary>
    public static async Task<string> LargeFileAsync(SfeClient sfe, LoadOptions options, MemoryWatch? watch)
    {
        var source = Path.Combine(options.Work, "source", "large.bin");
        Console.WriteLine($"\n== one file of {options.LargeMegabytes:N0} MB");
        var expected = SourceFiles.Large(source, options.LargeMegabytes);
        var size = new FileInfo(source).Length;
        var path = SfePath.Combine(options.Folder, "large.bin");

        watch?.Start();
        var clock = Stopwatch.StartNew();
        var shown = 0L;
        var progress = new Progress<long>(bytes =>
        {
            if (bytes - Interlocked.Read(ref shown) < size / 10 && bytes != size)
                return;
            Interlocked.Exchange(ref shown, bytes);
            Console.WriteLine($"  {bytes * 100 / size} % in {clock.Elapsed.TotalSeconds:N1} s");
        });
        await using (var stream = File.OpenRead(source))
            await sfe.Files.UploadAsync(path, stream, new SfeUploadOptions { Progress = progress });
        clock.Stop();
        var memory = watch?.Stop();

        var hashing = Stopwatch.StartNew();
        var actual = await sfe.Properties.ChecksumAsync(path);
        hashing.Stop();
        var check = actual == expected ? "SHA-256 equal" : $"FAILED: SHA-256 {actual}, expected {expected}";
        return $"| one file of {options.LargeMegabytes:N0} MB | {clock.Elapsed.TotalSeconds:N1} s, "
            + $"{size / 1048576.0 / clock.Elapsed.TotalSeconds:N0} MB/s; {check} (server read it in "
            + $"{hashing.Elapsed.TotalSeconds:N1} s){memory} |";
    }

    /// <summary>The caller's unfinished uploads once every file is in: there should be none.</summary>
    public static async Task<string> LeftoversAsync(string url, string token)
    {
        using var http = new HttpClient { BaseAddress = new Uri(url) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var left = await http.GetFromJsonAsync<JsonElement[]>("api/v1/files/uploads");
        return left is { Length: 0 }
            ? "| unfinished uploads left | none |"
            : $"| unfinished uploads left | FAILED: {left?.Length} |";
    }

    private static double Percentile(double[] sorted, int percent) =>
        sorted[Math.Min(sorted.Length - 1, sorted.Length * percent / 100)];
}

/// <summary>The files sent, made in the work folder (the large one on the disk it is on: a scratch disk).</summary>
static class SourceFiles
{
    public static List<string> Small(string folder, int count, int bytes)
    {
        Directory.CreateDirectory(folder);
        var random = new Random(4_3);
        var content = new byte[bytes];
        var files = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            random.NextBytes(content);
            var file = Path.Combine(folder, $"file {i:D5}.bin");
            File.WriteAllBytes(file, content);
            files.Add(file);
        }
        return files;
    }

    /// <summary>
    /// Writes <paramref name="megabytes"/> MB of bytes that don't repeat (a 1 MB random block, its first bytes changed
    /// per megabyte) and answers their SHA-256, in lower-case hexadecimal.
    /// </summary>
    public static string Large(string file, long megabytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var block = new byte[1 << 20];
        new Random(4_3).NextBytes(block);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var output = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            for (long mb = 0; mb < megabytes; mb++)
            {
                BitConverter.TryWriteBytes(block, mb);
                hash.AppendData(block);
                output.Write(block);
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

/// <summary>
/// The time of each request until its answer's headers, by kind (the upload routes' steps: check, start, chunk,
/// finish; anything else by method and first segments), for the report.
/// </summary>
sealed class RequestTimings : DelegatingHandler
{
    private readonly Dictionary<string, List<double>> _times = [];

    public void Clear()
    {
        lock (_times)
            _times.Clear();
    }

    public IEnumerable<string> Lines(string run)
    {
        lock (_times)
        {
            foreach (var (kind, times) in _times.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                times.Sort();
                yield return $"| {run}: {kind} | {times.Count:N0} requests; median {At(times, 50):N1} ms, "
                    + $"95th {At(times, 95):N1} ms, slowest {times[^1]:N1} ms |";
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var response = await base.SendAsync(request, token);
        var kind = Kind(request);
        lock (_times)
        {
            if (!_times.TryGetValue(kind, out var times))
                _times[kind] = times = [];
            times.Add(clock.Elapsed.TotalMilliseconds);
        }
        return response;
    }

    private static string Kind(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var method = request.Method.Method;
        if (!path.Contains("/files/uploads", StringComparison.Ordinal))
            return $"{method} {string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Take(4))}";
        return (method, path) switch
        {
            ("POST", _) when path.EndsWith("/check", StringComparison.Ordinal) => "upload check",
            ("POST", _) when path.EndsWith("/finish", StringComparison.Ordinal) => "upload finish",
            ("POST", _) => "upload start",
            ("PUT", _) => "upload chunk",
            _ => $"upload {method}",
        };
    }

    private static double At(List<double> sorted, int percent) =>
        sorted[Math.Min(sorted.Count - 1, sorted.Count * percent / 100)];
}

/// <summary>The server's working set, read twice a second while a run goes.</summary>
sealed class MemoryWatch(Process process)
{
    private CancellationTokenSource? _stop;
    private Task? _reading;
    private long _peak;
    private long _start;

    public void Start()
    {
        process.Refresh();
        _start = _peak = process.WorkingSet64;
        _stop = new CancellationTokenSource();
        _reading = ReadAsync(_stop.Token);
    }

    /// <summary>Stops and answers the report's words ("; server memory …").</summary>
    public string Stop()
    {
        _stop!.Cancel();
        _reading!.Wait();
        process.Refresh();
        return $"; server working set {_start / 1048576:N0} MB at start, peak {_peak / 1048576:N0} MB, "
            + $"{process.WorkingSet64 / 1048576:N0} MB at the end";
    }

    private async Task ReadAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            process.Refresh();
            _peak = Math.Max(_peak, process.WorkingSet64);
            try
            {
                await Task.Delay(500, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

/// <summary>
/// The app built into the work folder and started on an empty library there (External mode, SQLite, as the
/// end-to-end tests run it), its administrator made and an access token for this tool.
/// </summary>
sealed class ScratchServer : IDisposable
{
    private const string SetupCode = "upload-load-setup";

    private ScratchServer(Process process, string url, string token)
    {
        (Process, Url, Token) = (process, url, token);
    }

    public Process Process { get; }
    public string Url { get; }
    public string Token { get; }

    public static async Task<ScratchServer> StartAsync(LoadOptions options)
    {
        var repo = Repository();
        var build = Path.Combine(options.Work, "build");
        var dll = Path.Combine(build, "bin", "Sfe.Api", "release", "Sfe.Api.dll");
        if (!options.SkipBuild || !File.Exists(dll))
            Build(repo, build);
        LoadOptions.Clean(options.Work);
        var library = Directory.CreateDirectory(Path.Combine(options.Work, "library")).FullName;
        var data = Directory.CreateDirectory(Path.Combine(options.Work, "data")).FullName;
        var url = $"http://localhost:{options.Port}/";

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = options.Work,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { dll, "--contentRoot", Path.Combine(repo, "src", "Sfe.Api"), "--no-browser" })
            start.ArgumentList.Add(arg);
        start.ArgumentList.Add("--no-logo");
        var environment = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Urls"] = url.TrimEnd('/'),
            ["Sfe__Mode"] = "External",
            ["Sfe__Root"] = library,
            ["Sfe__InstallationFile"] = Path.Combine(data, "installation.json"),
            ["Sfe__Database__Provider"] = "Sqlite",
            ["ConnectionStrings__Sfe"] = $"Data Source={Path.Combine(data, "sfe.db")}",
            ["Sfe__SettingsFile"] = Path.Combine(data, "settings.json"),
            ["Sfe__Thumbnails__CacheDirectory"] = Path.Combine(data, "cache"),
            ["Sfe__Security__KeysDirectory"] = Path.Combine(data, "keys"),
            ["Sfe__Setup__Code"] = SetupCode,
        };
        foreach (var (key, value) in environment)
            start.Environment[key] = value;
        var process = Process.Start(start) ?? throw new InvalidOperationException("the app did not start");
        LogTo(process, Path.Combine(options.Work, "app.log"));
        Console.WriteLine($"app on {url} (log {Path.Combine(options.Work, "app.log")})");
        try
        {
            var token = await SetUpAsync(url);
            return new ScratchServer(process, url, token);
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }

    public void Dispose()
    {
        if (!Process.HasExited)
            Process.Kill(entireProcessTree: true);
        Process.WaitForExit();
        Process.Dispose();
    }

    private static string Repository([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));

    private static void Build(string repo, string build)
    {
        Console.WriteLine($"building Sfe.Api into {build}");
        // After a client change the first build can fail once on the previous build's chunk names (build.md).
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var info = new ProcessStartInfo("dotnet");
            foreach (var arg in new[] { "build", Path.Combine(repo, "src", "Sfe.Api"), "--artifacts-path", build })
                info.ArgumentList.Add(arg);
            // Release: what installs run (Debug answers each request a few milliseconds slower).
            foreach (var arg in new[] { "-c", "Release", "-v", "q", "-nologo" })
                info.ArgumentList.Add(arg);
            using var dotnet = Process.Start(info)!;
            dotnet.WaitForExit();
            if (dotnet.ExitCode == 0)
                return;
        }
        throw new InvalidOperationException("the build failed");
    }

    private static void LogTo(Process process, string file)
    {
        var log = new StreamWriter(file) { AutoFlush = true };
        process.OutputDataReceived += (_, e) => Write(log, e.Data);
        process.ErrorDataReceived += (_, e) => Write(log, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private static void Write(StreamWriter log, string? line)
    {
        if (line is null)
            return;
        lock (log)
            log.WriteLine(line);
    }

    /// <summary>Waits for the app, makes the administrator, then a token (tokens are made with a session).</summary>
    private static async Task<string> SetUpAsync(string url)
    {
        using var http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() })
        {
            BaseAddress = new Uri(url),
        };
        http.DefaultRequestHeaders.Add("X-SFE-Request", "1");
        await WaitForAsync(http);
        var setup = await http.PostAsJsonAsync("api/v1/setup", new
        {
            code = SetupCode,
            userName = "admin",
            password = "upload-load-password-1",
            displayName = "Upload load",
            libraryName = "Upload load",
        });
        await Ensure(setup, "setup");
        var made = await http.PostAsJsonAsync("api/v1/me/tokens", new { name = "upload load" });
        await Ensure(made, "token");
        var body = await made.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("secret").GetString()!;
    }

    private static async Task WaitForAsync(HttpClient http)
    {
        for (var i = 0; i < 240; i++)
        {
            try
            {
                if ((await http.GetAsync("api/v1/migration")).IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            await Task.Delay(500);
        }
        throw new TimeoutException("the app did not answer in two minutes: see app.log");
    }

    private static async Task Ensure(HttpResponseMessage response, string what)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"{what}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }
}
