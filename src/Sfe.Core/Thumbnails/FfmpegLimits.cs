namespace Sfe.Core.Thumbnails;

/// <summary>
/// How many ffmpeg runs the whole server allows at once: thumbnails (<c>Thumbnails:MaxConcurrency</c>) and trickplay
/// videos of the background passes (<c>Trickplay:Parallelism</c>). One instance for every library, so that several
/// libraries don't multiply the load: these are server settings.
/// </summary>
public sealed class FfmpegLimits
{
    private Gate _thumbnails = new(1);
    private Gate _trickplay = new(1);

    /// <summary>The gate of thumbnail runs, resized to <paramref name="size"/> when the setting changed.</summary>
    public SemaphoreSlim Thumbnails(int size) => Resize(ref _thumbnails, size);

    /// <summary>The gate of the videos the trickplay passes make, resized to <paramref name="size"/> when the setting changed.</summary>
    public SemaphoreSlim Trickplay(int size) => Resize(ref _trickplay, size);

    /// <summary>Replaced when the setting changes: runs in progress release the old one.</summary>
    private sealed class Gate(int size)
    {
        public int Size { get; } = Math.Max(1, size);
        public SemaphoreSlim Semaphore { get; } = new(Math.Max(1, size));
    }

    private static SemaphoreSlim Resize(ref Gate field, int size)
    {
        var gate = Volatile.Read(ref field);
        if (gate.Size != Math.Max(1, size))
        {
            Interlocked.CompareExchange(ref field, new Gate(size), gate);
            gate = Volatile.Read(ref field);
        }

        return gate.Semaphore;
    }
}
