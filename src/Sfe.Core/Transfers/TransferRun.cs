using Microsoft.Extensions.Logging;
using Sfe.Core.Connections;
using Sfe.Core.Files;
using Sfe.Core.Paths;

namespace Sfe.Core.Transfers;

/// <summary>
/// The walk of one transfer job (<see cref="TransferJobs"/>): each item asked, its folders depth first and page by
/// page as the source lists them (one page per level in memory), each file named at the target (the person's choice
/// on a clash), checked for room, written and verified (<see cref="TransferFileWriter"/>). A move takes each file
/// away from the source once verified (the files of a folder once its listing is read, so that no page shifts), then
/// the folders it emptied. A connection whose sign-in ended is marked and stops the job's remaining items.
/// </summary>
internal sealed class TransferRun(JobRun job, TransferStart start, ILogger logger)
{
    public const string SignInMessage = "The connection must be signed in to again: sign in, then try again.";

    private readonly TransferFileWriter _writer = new(job, start.Source, start.Target);
    private TransferTally _tally = new();
    private bool _signInEnded;

    private bool Move => start.Kind == FileJobKind.Move;
    private ITransferEndpoint Source => start.Source;
    private ITransferEndpoint Target => start.Target;

    /// <summary>The target folder, as the target names it; null when it is not there (or can't be reached).</summary>
    public async Task<string?> TargetFolderAsync(CancellationToken token)
    {
        if (Normalize(start.To.Path, start.To.ConnectionId) is not { } folder)
            return null;
        try
        {
            return await Target.GetAsync(folder, token) is { IsFolder: true } found ? found.Path : null;
        }
        catch (RemoteStorageException failure)
        {
            await FailedAsync(failure, token);
            return null;
        }
    }

    /// <summary>The result of an item when the target folder is missing.</summary>
    public FileJobItem NoTarget(string path) =>
        _signInEnded
            ? new FileJobItem(path, null, FileOperationStatus.Failed, SignInMessage)
            : new FileJobItem(path, null, FileOperationStatus.NotFound, "The folder they would go to was not found.");

    /// <summary>Transfers one asked item into <paramref name="folder"/>; its result.</summary>
    public async Task<FileJobItem> ItemAsync(string path, string folder, CancellationToken token)
    {
        if (_signInEnded)
            return new FileJobItem(path, null, FileOperationStatus.Failed, SignInMessage);
        _tally = new TransferTally();
        try
        {
            return await TransferAsync(path, folder, token);
        }
        catch (RemoteStorageException failure)
        {
            await FailedAsync(failure, token);
            var message = failure.Kind == RemoteErrorKind.CredentialsEnded ? SignInMessage : failure.Message;
            return new FileJobItem(path, null, TransferStatuses.Of(failure.Kind), message);
        }
    }

    private async Task<FileJobItem> TransferAsync(string path, string folder, CancellationToken token)
    {
        var entry = Normalize(path, start.FromConnection) is { Length: > 0 } asked
            ? await Source.GetAsync(asked, token)
            : null;
        if (entry is null)
            return new FileJobItem(path, null, FileOperationStatus.NotFound, "It was not found.");

        var (name, leftOut) = await NameAsync(path, entry, folder, token);
        if (leftOut is not null)
            return leftOut;
        var placed = entry.IsFolder
            ? await FolderAsync(entry, folder, name!, token)
            : await TopFileAsync(entry, folder, name!, token);
        var result = _tally.Result(path, placed);
        if (result.Status == FileOperationStatus.Done)
            Log(entry.Path, placed!);
        return result;
    }

    /// <summary>An asked file: written, then (a move) taken away from the source.</summary>
    private async Task<string?> TopFileAsync(TransferEntry file, string folder, string name, CancellationToken token)
    {
        if (await FileAsync(file, folder, name, token) is not { } written)
            return null;
        if (Move)
            await RemoveSourceAsync(file, token);
        return written.Path;
    }

    /// <summary>A folder made at the target (or the one there), then what it holds, page after page.</summary>
    private async Task<string> FolderAsync(TransferEntry folder, string into, string name, CancellationToken token)
    {
        var made = await Target.CreateFolderAsync(into, name, token);
        var moved = new List<TransferEntry>();
        await foreach (var child in Source.ListAsync(folder.Path, token))
            await ChildAsync(child, made.Path, moved, token);
        if (Move)
            await EmptyAsync(folder, moved, token);
        return made.Path;
    }

    /// <summary>A file or folder below an item; what fails is tallied and the walk goes on.</summary>
    private async Task ChildAsync(TransferEntry child, string into, List<TransferEntry> moved, CancellationToken token)
    {
        try
        {
            await PlaceChildAsync(child, into, moved, token);
        }
        catch (RemoteStorageException failure) when (failure.Kind != RemoteErrorKind.CredentialsEnded)
        {
            logger.LogWarning("{Account} could not transfer {Path}: {Problem}", Who, child.Path, failure.Message);
            _tally.Fail(failure);
        }
    }

    private async Task PlaceChildAsync(
        TransferEntry child,
        string into,
        List<TransferEntry> moved,
        CancellationToken token
    )
    {
        var (name, leftOut) = await NameAsync(child.Path, child, into, token);
        if (leftOut is not null)
            _tally.LeftOut(leftOut);
        else if (child.IsFolder)
            await FolderAsync(child, into, name!, token);
        else if (await FileAsync(child, into, name!, token) is not null && Move)
            moved.Add(child);
    }

    /// <summary>A file written at the target and verified; null when it was left out.</summary>
    private async Task<TransferEntry?> FileAsync(TransferEntry file, string into, string name, CancellationToken token)
    {
        if (!file.Downloadable)
        {
            _tally.Skip($"“{file.Name}” was left out: it has no content to transfer (an online document).");
            return null;
        }
        job.BytesTotal += file.Size ?? 0;
        await CheckRoomAsync(file, into, token);
        var written = await _writer.WriteAsync(file, into, name, token);
        await Target.AfterWriteAsync(written, token);
        return written;
    }

    /// <summary>The target's free space, when it tells, takes the file.</summary>
    private async Task CheckRoomAsync(TransferEntry file, string into, CancellationToken token)
    {
        var space = await Target.FreeSpaceAsync(into, token);
        if (space.FreeBytes is { } free && file.Size is { } size && free < size)
        {
            throw new RemoteStorageException(
                RemoteErrorKind.TooLarge,
                $"There is not enough free space for “{file.Name}” ({size:N0} bytes, {free:N0} free)."
            );
        }
    }

    /// <summary>
    /// The name the item takes at the target: its own when free, else the person's choice (asked when needed): a
    /// free name, or what is there taken away; or the item's result when it is left out.
    /// </summary>
    private async Task<(string? Name, FileJobItem? LeftOut)> NameAsync(
        string path,
        TransferEntry entry,
        string into,
        CancellationToken token
    )
    {
        if (await Target.GetAsync(LibraryClashes.Join(into, entry.Name), token) is not { } existing)
            return (entry.Name, null);
        var conflict = new FileJobConflict(Side(entry), Side(existing), CanReplace: true);
        return await job.ChooseAsync(conflict, token) switch
        {
            ConflictChoice.Skip => (null, LeftOut(path, entry.Name)),
            ConflictChoice.KeepBoth => (await Target.FreeNameAsync(into, entry.Name, entry.IsFolder, token), null),
            _ => await ReplaceAsync(path, entry.Name, existing, token),
        };
    }

    private async Task<(string? Name, FileJobItem? LeftOut)> ReplaceAsync(
        string path,
        string name,
        TransferEntry existing,
        CancellationToken token
    )
    {
        try
        {
            await Target.RemoveForReplaceAsync(existing, token);
            return (name, null);
        }
        catch (RemoteStorageException failure) when (failure.Kind == RemoteErrorKind.Refused)
        {
            return (
                null,
                new FileJobItem(path, null, FileOperationStatus.Forbidden, $"You may not replace “{name}” there.")
            );
        }
    }

    /// <summary>The moved files of a folder taken away, then the folder when nothing is left in it.</summary>
    private async Task EmptyAsync(TransferEntry folder, List<TransferEntry> moved, CancellationToken token)
    {
        foreach (var file in moved)
            await RemoveSourceAsync(file, token);
        await foreach (var _ in Source.ListAsync(folder.Path, token))
            return;
        await RemoveSourceAsync(folder, token);
    }

    /// <summary>A moved item taken away from the source (its trash); a failure leaves it there, told.</summary>
    private async Task RemoveSourceAsync(TransferEntry entry, CancellationToken token)
    {
        try
        {
            await Source.DeleteSourceAsync(entry, token);
        }
        catch (RemoteStorageException failure) when (failure.Kind != RemoteErrorKind.CredentialsEnded)
        {
            _tally.Fail(
                new RemoteStorageException(
                    failure.Kind,
                    $"“{entry.Name}” was copied but stays where it was: {failure.Message}"
                )
            );
        }
    }

    /// <summary>A failure of either side; an ended sign-in marks its connection and stops the job's next items.</summary>
    private async Task FailedAsync(RemoteStorageException failure, CancellationToken token)
    {
        if (failure.Kind != RemoteErrorKind.CredentialsEnded)
            return;
        _signInEnded = true;
        foreach (var id in await EndedConnectionsAsync(token))
        {
            if (start.SignInEnded is { } mark)
                await mark(id);
        }
    }

    /// <summary>
    /// The connections whose sign-in ended: the one side that is a connection; with two, those that refuse a call.
    /// </summary>
    private async Task<List<int>> EndedConnectionsAsync(CancellationToken token)
    {
        var sides = new List<(int Id, ITransferEndpoint Side)>();
        if (start.FromConnection is { } from)
            sides.Add((from, Source));
        if (start.To.ConnectionId is { } to)
            sides.Add((to, Target));
        if (sides.Count < 2)
            return [.. sides.Select(s => s.Id)];
        var ended = new List<int>();
        foreach (var (id, side) in sides)
        {
            if (await HasEndedAsync(side, token))
                ended.Add(id);
        }
        return ended;
    }

    private static async Task<bool> HasEndedAsync(ITransferEndpoint side, CancellationToken token)
    {
        try
        {
            await side.GetAsync("", token);
            return false;
        }
        catch (RemoteStorageException failure)
        {
            return failure.Kind == RemoteErrorKind.CredentialsEnded;
        }
    }

    private void Log(string path, string placed) =>
        logger.LogInformation(
            "{Account} {Action} {Path} from {From} to {Place}",
            Who,
            Move ? "moved" : "copied",
            path,
            PlaceName(start.FromConnection),
            $"{PlaceName(start.To.ConnectionId)}: {placed}"
        );

    private string Who => start.Actor.Name;

    private static string PlaceName(int? connection) => connection is { } id ? $"connection {id}" : "the library";

    /// <summary>A path of the side: the library's rules or a connection's.</summary>
    private static string? Normalize(string path, int? connection) =>
        connection is null ? LibraryPaths.NormalizeRelative(path) : RemotePaths.Normalize(path);

    private static FileJobItem LeftOut(string path, string name) =>
        new(path, null, FileOperationStatus.Skipped, $"“{name}” was left out: there is one of that name there.");

    private static ClashSide Side(TransferEntry entry) =>
        new(
            entry.Name,
            entry.IsFolder,
            entry.Size,
            entry.Modified is { } date ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : null
        );
}
