using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Options;

namespace Sfe.Core.Thumbnails;

/// <summary>The ffmpeg executable shared by thumbnails and trickplay: found once, run with a time limit.</summary>
public sealed class Ffmpeg
{
    private readonly IOptionsMonitor<SfeOptions> _options;
    private readonly ILogger<Ffmpeg> _logger;
    private readonly Lazy<string?> _path;

    public Ffmpeg(IOptionsMonitor<SfeOptions> options, ILogger<Ffmpeg> logger)
    {
        _options = options;
        _logger = logger;
        _path = new Lazy<string?>(Locate);
        _version = new Lazy<Task<string?>>(ReadVersionAsync);
    }

    /// <summary>Full path of the executable, or null when it can't be found.</summary>
    public string? ExecutablePath => _path.Value;

    /// <summary>The executable as configured (<c>Thumbnails:FfmpegPath</c>): a path, or a name looked up on PATH.</summary>
    public string Configured => _options.CurrentValue.Thumbnails.FfmpegPath;

    public bool IsAvailable => ExecutablePath is not null;

    /// <summary>Runs ffmpeg with <paramref name="arguments"/>; standard output is copied to <paramref name="output"/>.</summary>
    /// <returns>The exit code and the error output.</returns>
    /// <exception cref="TimeoutException">ffmpeg ran longer than <paramref name="timeout"/> (it is killed).</exception>
    public async Task<(int ExitCode, string Errors)> RunAsync(IEnumerable<string> arguments, TimeSpan timeout, Stream? output = null, CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(ExecutablePath ?? throw new InvalidOperationException("ffmpeg is not available."))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start ffmpeg.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(output ?? Stream.Null, limit.Token);
        var stderr = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(limit.Token));
        }
        catch (Exception ex)
        {
            // Also when writing the output failed (a viewer gone from a stream): ffmpeg would keep running unseen.
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Exited in the meantime.
            }

            if (ex is not OperationCanceledException)
                throw;
            cancellationToken.ThrowIfCancellationRequested();
            // Not a failure of the file: a busy host or a slow network drive can make ffmpeg time out.
            throw new TimeoutException($"ffmpeg timed out after {timeout.TotalSeconds:0} s.");
        }

        return (process.ExitCode, stderr.Result);
    }

    /// <summary>The version ffmpeg reports (<c>7.1-full_build...</c>), read once; null when it is missing or says none.</summary>
    public Task<string?> GetVersionAsync() => _version.Value;

    private readonly Lazy<Task<string?>> _version;

    private async Task<string?> ReadVersionAsync()
    {
        if (!IsAvailable)
            return null;
        try
        {
            var output = new MemoryStream();
            await RunAsync(["-hide_banner", "-version"], TimeSpan.FromSeconds(10), output);
            return ParseVersion(System.Text.Encoding.UTF8.GetString(output.ToArray()));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Cannot read the version of ffmpeg");
            return null;
        }
    }

    /// <summary>The word after "ffmpeg version" on the first line of <c>ffmpeg -version</c>.</summary>
    internal static string? ParseVersion(string output)
    {
        const string prefix = "ffmpeg version ";
        var line = output.Split('\n', 2)[0].Trim();
        return line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && line[prefix.Length..].Split(' ', 2)[0] is { Length: > 0 } version
            ? version
            : null;
    }

    private string? Locate()
    {
        var configured = _options.CurrentValue.Thumbnails.FfmpegPath;
        if (string.IsNullOrWhiteSpace(configured))
            return null;
        var found = Find(configured);
        if (found is null)
            _logger.LogWarning("ffmpeg not found at '{Path}': thumbnails and trickplay are disabled", configured);
        return found;
    }

    /// <summary>The file is named as ffmpeg's program (<c>ffmpeg</c>, <c>ffmpeg.exe</c> on Windows), whatever the case.</summary>
    public static bool IsFfmpegName(string path) =>
        Path.GetFileName(path) is var name
        && (name.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase) || (OperatingSystem.IsWindows() && name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The executable <paramref name="configured"/> names: a file path, a folder holding it (or its <c>bin</c>), or a
    /// name looked up on PATH. Null when none exists.
    /// </summary>
    public static string? Find(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return null;
        configured = configured.Trim();
        var names = OperatingSystem.IsWindows() ? ["ffmpeg.exe", "ffmpeg"] : new[] { "ffmpeg" };

        if (Path.IsPathRooted(configured) || configured.Contains(Path.DirectorySeparatorChar) || configured.Contains(Path.AltDirectorySeparatorChar))
        {
            if (File.Exists(configured))
                return configured;
            // A folder, as picked on the installation page: ffmpeg's own folder or the one of its download.
            return Directory.Exists(configured)
                ? new[] { configured, Path.Combine(configured, "bin") }.SelectMany(dir => names.Select(name => Path.Combine(dir, name))).FirstOrDefault(File.Exists)
                : null;
        }

        var candidates = OperatingSystem.IsWindows() && !Path.HasExtension(configured) ? [configured + ".exe", configured] : new[] { configured };
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(dir => candidates.Select(name => Path.Combine(dir, name)))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>The version an ffmpeg executable reports, for a path not yet configured (the installation page).</summary>
    public static async Task<string?> ProbeVersionAsync(string executable, CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-hide_banner");
        start.ArgumentList.Add("-version");
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return null;
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(10));
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            var errors = process.StandardError.ReadToEndAsync(limit.Token);
            try
            {
                await Task.WhenAll(output, errors, process.WaitForExitAsync(limit.Token));
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return ParseVersion(output.Result);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
