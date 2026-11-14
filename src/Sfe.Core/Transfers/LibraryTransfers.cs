using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Files;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Core.Transfers;

/// <summary>
/// Opens the library's side of transfers (agents/docs/features/transfers.md), one per job: the
/// library's services it works with, and the person's access, read again at every call.
/// </summary>
public sealed class LibraryTransfers(
    LibraryPaths paths,
    WorkFolder work,
    DirectoryIndexer indexer,
    LibraryBrowser browser,
    ScanCoordinator scans,
    Trash trash,
    IOptionsMonitor<SfeOptions> options,
    ILogger<LibraryTransferEndpoint> logger
)
{
    internal LibraryPaths Paths => paths;
    internal WorkFolder Work => work;
    internal DirectoryIndexer Indexer => indexer;
    internal LibraryBrowser Browser => browser;
    internal ScanCoordinator Scans => scans;
    internal Trash Trash => trash;
    internal FileManagementOptions Files => options.CurrentValue.Files;
    internal ILogger Logger => logger;

    /// <summary>
    /// The library's side for one job of <paramref name="actor"/>. <paramref name="writeRight"/>: what writing there
    /// and taking a source away needs (<see cref="Access.Rights.CopyFiles"/> or <see cref="Access.Rights.MoveFiles"/>).
    /// <paramref name="partials"/>: where the partial files it writes are told (the job's, so that an extraction in
    /// the same folder leaves them).
    /// </summary>
    public LibraryTransferEndpoint Open(
        FileActor actor,
        IFileJobAccess access,
        string writeRight,
        ConcurrentBag<string> partials
    ) => new(this, actor, access, writeRight, partials);
}
