using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Core.Thumbnails;

/// <summary>
/// JPEG previews for videos and images, rendered with ffmpeg and cached on disk.
/// The cache layout is <c>{k[0..2]}/{k[2..4]}/{k[4..]}.bin</c> with <c>k</c> = <see cref="CacheKey"/>: two levels of
/// 256 folders keep every folder small whatever the library's shape. An empty file means "no thumbnail".
/// Keys are lower-cased only on case-insensitive file systems (Windows): a path finds its thumbnail whatever its case.
/// </summary>
public sealed class ThumbnailService
{
    private readonly LibraryPaths _paths;
    private readonly MediaClassifier _classifier;
    private readonly IOptionsMonitor<SfeOptions> _options;
    private readonly ILogger<ThumbnailService> _logger;
    private readonly FfmpegLimits _limits;
    private readonly Lock _jobsGate = new();
    private readonly Dictionary<string, Job> _jobs = new(LibraryPaths.PathComparer);
    private readonly Ffmpeg _ffmpeg;
    private CancellationTokenSource _cancel = new();
    private int _rendering;

    public ThumbnailService(LibraryPaths paths, MediaClassifier classifier, Ffmpeg ffmpeg, IOptionsMonitor<SfeOptions> options, FfmpegLimits limits,
        ILogger<ThumbnailService> logger)
    {
        _paths = paths;
        _classifier = classifier;
        _options = options;
        _logger = logger;
        _limits = limits;
        _ffmpeg = ffmpeg;
    }

    private ThumbnailOptions Options => _options.CurrentValue.Thumbnails;

    /// <summary>Limits concurrent ffmpeg runs, for every library together (<see cref="FfmpegLimits"/>).</summary>
    private SemaphoreSlim Concurrency() => _limits.Thumbnails(Options.MaxConcurrency);

    public bool IsEnabled => Options.Enabled;

    /// <summary>True when thumbnails can be generated (enabled and ffmpeg found).</summary>
    public bool IsAvailable => Options.Enabled && _ffmpeg.IsAvailable;

    /// <returns>Path of the cached JPEG, or null when no thumbnail can be provided.</returns>
    public async Task<string?> GetAsync(string? relativePath, CancellationToken cancellationToken = default)
    {
        if (!Options.Enabled || !_paths.TryResolve(relativePath, out var source))
            return null;

        var sourceInfo = new FileInfo(source);
        var kind = _classifier.Classify(sourceInfo.Extension);
        if (!sourceInfo.Exists || kind is not (ItemKind.Video or ItemKind.Image))
            return null;

        var cacheFile = GetCachePath(source);
        if (IsFresh(cacheFile, sourceInfo))
            return new FileInfo(cacheFile).Length > 0 ? cacheFile : null;

        if (!IsAvailable)
            return null;

        // Concurrent requests for the same file share a single ffmpeg run.
        Job job;
        var start = false;
        lock (_jobsGate)
        {
            if (!_jobs.TryGetValue(cacheFile, out job!))
            {
                _jobs[cacheFile] = job = new Job();
                start = true;
            }

            job.Waiters++;
        }

        if (start)
            _ = RunAsync(job, source, kind, cacheFile);

        try
        {
            return await job.Result.Task.WaitAsync(cancellationToken) ? cacheFile : null;
        }
        finally
        {
            lock (_jobsGate)
                job.Waiters--;
        }
    }

    /// <summary>A thumbnail being rendered, or waiting for its turn, and the requests waiting for it.</summary>
    private sealed class Job
    {
        /// <summary>Guarded by <c>_jobsGate</c>.</summary>
        public int Waiters;
        public TaskCompletionSource<bool> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Thumbnails being rendered, and those waiting for their turn.</summary>
    public (int Rendering, int Waiting) Activity
    {
        get
        {
            lock (_jobsGate)
            {
                var rendering = Volatile.Read(ref _rendering);
                return (rendering, Math.Max(0, _jobs.Count - rendering));
            }
        }
    }

    /// <summary>
    /// Stops the thumbnails being rendered and drops those waiting (from the "Background tasks" dialog). Nothing is
    /// cached for them: the listings ask again for the ones they still show.
    /// </summary>
    /// <returns>false when there was nothing to stop.</returns>
    public bool CancelAll()
    {
        int jobs;
        lock (_jobsGate)
            jobs = _jobs.Count;
        Interlocked.Exchange(ref _cancel, new CancellationTokenSource()).Cancel();
        if (jobs > 0)
            _logger.LogInformation("{Count} thumbnails cancelled", jobs);
        return jobs > 0;
    }

    private async Task RunAsync(Job job, string source, ItemKind kind, string cacheFile)
    {
        var rendered = false;
        var concurrency = Concurrency();
        var cancellationToken = _cancel.Token;
        var entered = false;
        try
        {
            await concurrency.WaitAsync(cancellationToken);
            entered = true;
            // Scrolling quickly through a large folder requests many thumbnails, and the browser cancels the ones
            // that leave the screen: don't run ffmpeg for a picture nobody waits for any more. The job is dropped
            // under the lock, so a request arriving later starts a new one.
            lock (_jobsGate)
            {
                if (job.Waiters == 0)
                {
                    _jobs.Remove(cacheFile);
                    return;
                }
            }

            Interlocked.Increment(ref _rendering);
            try
            {
                rendered = await GenerateAsync(source, kind, cacheFile, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _rendering);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled from the "Background tasks" dialog: nothing is cached, the next request tries again.
        }
        finally
        {
            if (entered)
                concurrency.Release();
            // Removed before completing: a request arriving now starts a new job, which finds the fresh cache file.
            lock (_jobsGate)
            {
                if (_jobs.TryGetValue(cacheFile, out var current) && current == job)
                    _jobs.Remove(cacheFile);
            }

            job.Result.TrySetResult(rendered);
        }
    }

    public string GetCachePath(string sourceFullPath) => Path.Combine(Options.CacheDirectory, ShardedPath(CacheKey(sourceFullPath)) + ".bin");

    private static bool IsFresh(string cacheFile, FileInfo source)
    {
        var cache = new FileInfo(cacheFile);
        return cache.Exists && cache.LastWriteTimeUtc >= source.LastWriteTimeUtc;
    }

    private async Task<bool> GenerateAsync(string source, ItemKind kind, string cacheFile, CancellationToken cancellationToken)
    {
        try
        {
            var image = await RenderAsync(source, kind == ItemKind.Video ? Options.VideoSeekSeconds : null, cancellationToken);
            // Clips shorter than the seek position produce no frame: fall back to the first one.
            if (image.Length == 0 && kind == ItemKind.Video && Options.VideoSeekSeconds > 0)
                image = await RenderAsync(source, 0, cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            var temp = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temp, image);
            File.Move(temp, cacheFile, overwrite: true);

            if (image.Length == 0)
                _logger.LogInformation("No thumbnail could be rendered for {File}", source);
            return image.Length > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nothing is cached, so the next request tries again.
            _logger.LogWarning(ex, "Thumbnail generation failed for {File}", source);
            return false;
        }
    }

    private async Task<byte[]> RenderAsync(string source, int? seekSeconds, CancellationToken cancellationToken)
    {
        List<string> arguments = ["-hide_banner", "-loglevel", "error", "-nostdin"];
        if (seekSeconds is > 0)
            arguments.AddRange(["-ss", seekSeconds.Value.ToString()]);
        arguments.AddRange([
            "-i", source,
            "-frames:v", "1",
            "-vf", $"scale='min({Options.Width},iw)':-2",
            "-q:v", "5",
            "-f", "image2", "-c:v", "mjpeg", "pipe:1",
        ]);

        using var output = new MemoryStream();
        var (exitCode, errors) = await _ffmpeg.RunAsync(arguments, TimeSpan.FromSeconds(Math.Max(1, Options.TimeoutSeconds)), output, cancellationToken);
        if (exitCode != 0)
        {
            _logger.LogDebug("ffmpeg exited with {Code} on {File}: {Error}", exitCode, source, errors);
            return [];
        }

        return output.ToArray();
    }

    /// <summary>
    /// Cache key of a file (thumbnail and trickplay): lower-case hex SHA1 of <c>{Hash(folder)}/{Hash(file name)}</c>.
    /// Built from the two hashes of the layout before 1.40.0 (one folder per hashed folder), so that the update could
    /// rename those entries without knowing their files (<see cref="Upgrades.Steps.V1_40_0.ShardCacheLayout"/>).
    /// </summary>
    public static string CacheKey(string sourceFullPath)
    {
        var directory = Path.GetDirectoryName(sourceFullPath) ?? string.Empty;
        return CacheKey(Hash(directory), Hash(Path.GetFileName(sourceFullPath)));
    }

    /// <inheritdoc cref="CacheKey(string)"/>
    internal static string CacheKey(string folderHash, string fileHash) =>
        Convert.ToHexStringLower(SHA1.HashData(Encoding.ASCII.GetBytes(folderHash + "/" + fileHash)));

    /// <summary>Relative cache path of a key: <c>ab/cd/rest</c> (65 536 folders over two levels).</summary>
    public static string ShardedPath(string key) => Path.Combine(key[..2], key[2..4], key[4..]);

    /// <summary>Hash of a folder or file name: upper-case hex SHA1 of the (lower-cased) name without trailing separators.</summary>
    internal static string Hash(string key)
    {
        if (LibraryPaths.IsCaseInsensitiveFileSystem)
            key = key.ToLowerInvariant();
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key.TrimEnd('\\', '/'))));
    }
}
