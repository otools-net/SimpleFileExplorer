namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// What a watched folder's quick look at the provider's changes found (<see cref="ConnectionIndexer.FollowAsync"/>).
/// </summary>
/// <param name="Changes">The changes read (and written into the index).</param>
/// <param name="Folders">The folders (from the connection's root) whose items changed, and folders changed themselves.
/// </param>
/// <param name="TooManyFolders">More folders changed than <paramref name="Folders"/> names: take every one as changed.
/// </param>
public sealed record ConnectionFollowResult(long Changes, IReadOnlyCollection<string> Folders, bool TooManyFolders);
