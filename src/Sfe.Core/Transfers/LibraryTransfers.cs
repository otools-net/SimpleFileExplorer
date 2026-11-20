using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Indexing;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Core.Transfers;

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
    internal ItemsBeingWritten Writing => browser.Writing;
    internal FileManagementOptions Files => options.CurrentValue.Files;
    internal ILogger Logger => logger;

    /// <summary>
    /// The library's side for one job of <paramref name="actor"/>. <paramref name="writeRight"/>: what writing there
    /// and taking a source away needs (<see cref="Access.Rights.CopyFiles"/> or <see cref="Access.Rights.MoveFiles"/>).
    /// <paramref name="partials"/>: where the partial files it writes are told (the job's, so that an extraction in
    /// the same folder leaves them). <paramref name="tags"/>: the tags carried with the files (none without it).
    /// </summary>
    public LibraryTransferEndpoint Open(
        FileActor actor,
        IFileJobAccess access,
        string writeRight,
        ConcurrentBag<string> partials,
        LibraryTransferTags? tags = null
    ) => new(this, actor, access, writeRight, partials) { Tags = tags };
}
