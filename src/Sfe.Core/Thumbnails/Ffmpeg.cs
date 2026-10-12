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
    }

    /// <summary>Full path of the executable, or null when it can't be found.</summary>
    public string? ExecutablePath => _path.Value;

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
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Exited in the meantime.
            }

            cancellationToken.ThrowIfCancellationRequested();
            // Not a failure of the file: a busy host or a slow network drive can make ffmpeg time out.
            throw new TimeoutException($"ffmpeg timed out after {timeout.TotalSeconds:0} s.");
        }

        return (process.ExitCode, stderr.Result);
    }

    private string? Locate()
    {
        var configured = _options.CurrentValue.Thumbnails.FfmpegPath;
        if (string.IsNullOrWhiteSpace(configured))
            return null;

        if (Path.IsPathRooted(configured) || configured.Contains(Path.DirectorySeparatorChar) || configured.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(configured) ? configured : Warn(configured);

        var names = OperatingSystem.IsWindows() && !Path.HasExtension(configured) ? [configured + ".exe", configured] : new[] { configured };
        var found = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(dir => names.Select(name => Path.Combine(dir, name)))
            .FirstOrDefault(File.Exists);

        return found ?? Warn(configured);

        string? Warn(string path)
        {
            _logger.LogWarning("ffmpeg not found at '{Path}': thumbnails and trickplay are disabled", path);
            return null;
        }
    }
}
