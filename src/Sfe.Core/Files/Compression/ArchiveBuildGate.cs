using Sfe.Core.Options;

namespace Sfe.Core.Files.Compression;

/// <summary>
/// How many archives' lists the whole server reads at once (<see cref="ArchiveOptions.CatalogBuilds"/>): one instance
/// for every library (as <see cref="Thumbnails.FfmpegLimits"/>), so that several libraries don't multiply the load.
/// </summary>
public sealed class ArchiveBuildGate
{
    private Gate _gate = new(1);
    private Gate _compress = new(1);

    /// <summary>
    /// The gate, resized to <paramref name="size"/> when the setting changed (builds running release the old one).
    /// </summary>
    public SemaphoreSlim Builds(int size) => Resized(ref _gate, size);

    /// <summary>
    /// The gate of compressions (zips written into libraries, <c>CompressJobs</c>), as <see cref="Builds"/>.
    /// </summary>
    public SemaphoreSlim Compressions(int size) => Resized(ref _compress, size);

    private static SemaphoreSlim Resized(ref Gate field, int size)
    {
        var gate = Volatile.Read(ref field);
        if (gate.Size != Math.Max(1, size))
        {
            Interlocked.CompareExchange(ref field, new Gate(size), gate);
            gate = Volatile.Read(ref field);
        }
        return gate.Semaphore;
    }

    private sealed class Gate(int size)
    {
        public int Size { get; } = Math.Max(1, size);
        public SemaphoreSlim Semaphore { get; } = new(Math.Max(1, size));
    }
}
