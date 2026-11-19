using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Http;
using Otools.Net.Sfe.Client.Tags;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// The library's tags on a connection's files, as <see cref="SfeTags"/> has them on the library's: read, given and
/// taken, on one file, many, or every file of a folder or search (<see cref="SfeTagScope"/>); paths relative to the
/// connection's root. The tags are kept in the library's index of the connection, so a file is tagged once the index
/// holds it: <see cref="SfeConnectionProblem.NotIndexed"/> until the connection's first sync in the library has run.
/// The tags themselves are made and listed with <see cref="SfeTags"/>.
/// </summary>
public sealed class SfeConnectionTags
{
    private readonly FileTagCalls _files;

    internal SfeConnectionTags(SfeTransport transport, FilePlace place)
    {
        _files = new FileTagCalls(transport, place);
    }

    /// <summary>The tags of a file, from the library's index (none for a file it doesn't hold yet).</summary>
    /// <param name="path">The file's path.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">
    /// The caller may not use the connection, or may not see the file's tags.
    /// </exception>
    public Task<SfeFileTags> GetFileTagsAsync(string path, CancellationToken cancellationToken = default) =>
        _files.GetAsync(path, cancellationToken);

    /// <summary>
    /// Gives tags to files (right <c>tags.assign</c>); a few files not indexed yet are asked of the provider and
    /// indexed on the way. Many files go in batches: a refused batch stops there, the earlier ones stay done.
    /// </summary>
    /// <param name="paths">The files' paths (not folders).</param>
    /// <param name="tagIds">The tags' ids.</param>
    /// <param name="cancellationToken">Stops between batches.</param>
    /// <exception cref="SfeApiException">
    /// A path is not a file the caller may tag, or a tag doesn't exist (400, <see cref="SfeApiException.Errors"/>).
    /// </exception>
    /// <exception cref="SfeConflictException">
    /// <see cref="SfeConnectionProblem.NotIndexed"/>: the connection is not indexed in the library yet, or too many
    /// of the files are missing from its index; its sync has started, try again once it ends.
    /// </exception>
    public Task<SfeTagChangeResult> AddAsync(
        IEnumerable<string> paths,
        IEnumerable<int> tagIds,
        CancellationToken cancellationToken = default
    ) => ChangeAsync(paths, tagIds, [], cancellationToken);

    /// <summary>
    /// Takes tags from files (right <c>tags.assign</c>). Many files go in batches: a refused batch stops there, the
    /// earlier ones stay done.
    /// </summary>
    /// <param name="paths">The files' paths (not folders).</param>
    /// <param name="tagIds">The tags' ids.</param>
    /// <param name="cancellationToken">Stops between batches.</param>
    /// <exception cref="SfeApiException">
    /// A path is not a file the caller may tag, or a tag doesn't exist (400, <see cref="SfeApiException.Errors"/>).
    /// </exception>
    /// <exception cref="SfeConflictException">
    /// <see cref="SfeConnectionProblem.NotIndexed"/>: the connection is not indexed in the library yet.
    /// </exception>
    public Task<SfeTagChangeResult> RemoveAsync(
        IEnumerable<string> paths,
        IEnumerable<int> tagIds,
        CancellationToken cancellationToken = default
    ) => ChangeAsync(paths, [], tagIds, cancellationToken);

    /// <summary>
    /// Gives some tags to files and takes others in one change (right <c>tags.assign</c>); a tag in both is added.
    /// </summary>
    /// <param name="paths">The files' paths (not folders).</param>
    /// <param name="add">The ids of the tags given.</param>
    /// <param name="remove">The ids of the tags taken.</param>
    /// <param name="cancellationToken">Stops between batches.</param>
    /// <exception cref="SfeApiException">
    /// A path is not a file the caller may tag, or a tag doesn't exist (400, <see cref="SfeApiException.Errors"/>).
    /// </exception>
    /// <exception cref="SfeConflictException">
    /// <see cref="SfeConnectionProblem.NotIndexed"/>: the connection is not indexed in the library yet, or too many
    /// of the files are missing from its index; its sync has started, try again once it ends.
    /// </exception>
    public Task<SfeTagChangeResult> ChangeAsync(
        IEnumerable<string> paths,
        IEnumerable<int> add,
        IEnumerable<int> remove,
        CancellationToken cancellationToken = default
    ) => _files.ChangeAsync(paths, add, remove, cancellationToken);

    /// <summary>
    /// The tags of the files of a folder (or folder tree), or of a search's matches, with the number of files having
    /// each. Only the files the index holds and the caller sees count.
    /// </summary>
    /// <param name="scope">The folder, and the search.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">
    /// The caller may not use the connection, or doesn't see the folder.
    /// </exception>
    public Task<SfeScopeTags> GetScopeTagsAsync(SfeTagScope scope, CancellationToken cancellationToken = default) =>
        _files.GetScopeAsync(scope, cancellationToken);

    /// <summary>
    /// Gives tags to and takes tags from every file of a folder (or folder tree), or every match of a search, in one
    /// call: only the files the index holds and the caller may tag change (a file not indexed yet is left out).
    /// </summary>
    /// <param name="scope">The folder, and the search.</param>
    /// <param name="add">The ids of the tags given.</param>
    /// <param name="remove">The ids of the tags taken.</param>
    /// <param name="cancellationToken">
    /// Stops waiting for the server; the files the server had already changed stay changed.
    /// </param>
    /// <exception cref="SfeApiException">A tag doesn't exist (400, <see cref="SfeApiException.Errors"/>).</exception>
    /// <exception cref="SfeNotFoundException">
    /// The caller may not use the connection, or doesn't see the folder.
    /// </exception>
    public Task<SfeScopeTagChangeResult> ChangeScopeAsync(
        SfeTagScope scope,
        IEnumerable<int> add,
        IEnumerable<int> remove,
        CancellationToken cancellationToken = default
    ) => _files.ChangeScopeAsync(scope, add, remove, cancellationToken);
}
