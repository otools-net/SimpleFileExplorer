using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Http;
using Otools.Net.Sfe.Client.Tags;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// The library's tags: listed, found, made, changed, deleted; read on files, added to and removed from them. A search
/// finds the files of a tag with <c>t:name</c> (<see cref="SfeSearch"/>).
/// </summary>
public sealed class SfeTags
{
    /// <summary>Files one tag change may name (the server's limit in every v1 version).</summary>
    private const int ChangeBatch = 1000;

    /// <summary>Tags one listing may answer (the server's limit).</summary>
    private const int MaxListed = 1000;

    private readonly SfeTransport _transport;

    internal SfeTags(SfeTransport transport)
    {
        _transport = transport;
    }

    /// <summary>The tags, by name (at most the server's limit, 1000).</summary>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public async Task<IReadOnlyList<SfeTag>> ListAsync(CancellationToken cancellationToken = default)
    {
        var route = Routes.V1($"tags?limit={MaxListed.ToString(CultureInfo.InvariantCulture)}");
        var answer = await _transport
            .ReadAsync<SfeTagSearch>(SfeRequest.Get(route), cancellationToken)
            .ConfigureAwait(false);
        return answer.Matches;
    }

    /// <summary>
    /// The tags whose name, shorthand or alias holds <paramref name="text"/> (case ignored), those starting with it
    /// first, and the tags inheriting from them.
    /// </summary>
    /// <param name="text">What the names hold.</param>
    /// <param name="limit">The most matches answered (1 to 1000).</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public Task<SfeTagSearch> FindAsync(string text, int limit = 100, CancellationToken cancellationToken = default)
    {
        var count = Math.Max(1, Math.Min(limit, MaxListed)).ToString(CultureInfo.InvariantCulture);
        var route = Routes.V1($"tags?q={Routes.Escape(text ?? "")}&limit={count}");
        return _transport.ReadAsync<SfeTagSearch>(SfeRequest.Get(route), cancellationToken);
    }

    /// <summary>A tag by its id.</summary>
    /// <param name="id">Its id.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">There is no such tag.</exception>
    public Task<SfeTag> GetAsync(int id, CancellationToken cancellationToken = default) =>
        _transport.ReadAsync<SfeTag>(SfeRequest.Get(Route(id)), cancellationToken);

    /// <summary>Makes a tag (right <c>tags.manage</c>) and answers it.</summary>
    /// <param name="draft">Its name and the rest.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeApiException">The draft is refused (400: a name taken, an unknown parent…).</exception>
    public Task<SfeTag> CreateAsync(SfeTagDraft draft, CancellationToken cancellationToken = default)
    {
        if (draft is null)
            throw new ArgumentNullException(nameof(draft));
        return _transport.ReadAsync<SfeTag>(SfeRequest.Post(Routes.V1("tags"), draft), cancellationToken);
    }

    /// <summary>Replaces everything of a tag by <paramref name="draft"/> (right <c>tags.manage</c>).</summary>
    /// <param name="id">The tag's id.</param>
    /// <param name="draft">Its new name and the rest; what it leaves empty is emptied.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">There is no such tag.</exception>
    public Task<SfeTag> UpdateAsync(int id, SfeTagDraft draft, CancellationToken cancellationToken = default)
    {
        if (draft is null)
            throw new ArgumentNullException(nameof(draft));
        var request = new SfeRequest(HttpMethod.Put, Route(id)) { Content = () => SfeJson.Content(draft) };
        return _transport.ReadAsync<SfeTag>(request, cancellationToken);
    }

    /// <summary>Deletes a tag and takes it from every file (right <c>tags.manage</c>).</summary>
    /// <param name="id">The tag's id.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">There is no such tag.</exception>
    /// <exception cref="SfeConflictException">It comes with the app.</exception>
    public Task DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        _transport.RunAsync(SfeRequest.Delete(Route(id)), cancellationToken);

    /// <summary>The tags of a file.</summary>
    /// <param name="path">The file's path.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">It isn't there, or the caller may not see its tags.</exception>
    public Task<SfeFileTags> GetFileTagsAsync(string path, CancellationToken cancellationToken = default) =>
        _transport.ReadAsync<SfeFileTags>(
            SfeRequest.Get(Routes.WithPath("file-tags", SfePath.Normalize(path))),
            cancellationToken
        );

    /// <summary>
    /// Gives tags to files (right <c>tags.assign</c>); files not indexed yet are indexed on the way. Many files go
    /// in batches: a refused batch stops there, the earlier ones stay done.
    /// </summary>
    /// <param name="paths">The files' paths (not folders).</param>
    /// <param name="tagIds">The tags' ids.</param>
    /// <param name="cancellationToken">Stops between batches.</param>
    /// <exception cref="SfeApiException">
    /// A path is not a file the caller may tag, or a tag doesn't exist (400, <see cref="SfeApiException.Errors"/>).
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
    public async Task<SfeTagChangeResult> ChangeAsync(
        IEnumerable<string> paths,
        IEnumerable<int> add,
        IEnumerable<int> remove,
        CancellationToken cancellationToken = default
    )
    {
        var files = (paths ?? throw new ArgumentNullException(nameof(paths))).Select(SfePath.Normalize).ToList();
        var added = (add ?? []).ToList();
        var removed = (remove ?? []).ToList();
        var answers = new List<FileTagChangeAnswer>();
        for (var start = 0; start < files.Count; start += ChangeBatch)
        {
            var batch = files.GetRange(start, Math.Min(ChangeBatch, files.Count - start));
            var body = new FileTagChange(batch, added, removed);
            answers.Add(
                await _transport
                    .ReadAsync<FileTagChangeAnswer>(SfeRequest.Post(Routes.V1("file-tags"), body), cancellationToken)
                    .ConfigureAwait(false)
            );
        }
        return new SfeTagChangeResult(
            answers.Sum(answer => answer.Added),
            answers.Sum(answer => answer.Removed),
            answers.SelectMany(answer => answer.Files).ToList()
        );
    }

    private static string Route(int id) => Routes.V1($"tags/{id.ToString(CultureInfo.InvariantCulture)}");
}
