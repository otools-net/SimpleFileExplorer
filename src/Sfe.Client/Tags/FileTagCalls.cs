using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Http;

namespace Otools.Net.Sfe.Client.Tags;

/// <summary>
/// The calls on files' tags (<c>file-tags…</c>) of a place, the library's or a connection's (the same routes and
/// bodies below <c>connections/{id}/</c>), so that <see cref="SfeTags"/> and <see cref="SfeConnectionTags"/> share
/// them.
/// </summary>
internal sealed class FileTagCalls
{
    /// <summary>Files one tag change may name (the server's limit in every v1 version).</summary>
    private const int ChangeBatch = 1000;

    private readonly SfeTransport _transport;
    private readonly FilePlace _place;

    public FileTagCalls(SfeTransport transport, FilePlace place)
    {
        _transport = transport;
        _place = place;
    }

    /// <summary>The tags of the file <paramref name="path"/>.</summary>
    public Task<SfeFileTags> GetAsync(string path, CancellationToken cancellationToken) =>
        _transport.ReadAsync<SfeFileTags>(
            SfeRequest.Get(_place.WithPath("file-tags", SfePath.Normalize(path))),
            cancellationToken
        );

    /// <summary>Tags given to and taken from files, in batches the server takes; the sum of the answers.</summary>
    public async Task<SfeTagChangeResult> ChangeAsync(
        IEnumerable<string> paths,
        IEnumerable<int> add,
        IEnumerable<int> remove,
        CancellationToken cancellationToken
    )
    {
        var files = (paths ?? throw new ArgumentNullException(nameof(paths))).Select(SfePath.Normalize).ToList();
        var added = (add ?? []).ToList();
        var removed = (remove ?? []).ToList();
        var answers = new List<FileTagChangeAnswer>();
        for (var start = 0; start < files.Count; start += ChangeBatch)
        {
            var batch = files.GetRange(start, Math.Min(ChangeBatch, files.Count - start));
            var request = SfeRequest.Post(_place.Route("file-tags"), new FileTagChange(batch, added, removed));
            answers.Add(
                await _transport.ReadAsync<FileTagChangeAnswer>(request, cancellationToken).ConfigureAwait(false)
            );
        }
        return new SfeTagChangeResult(
            answers.Sum(answer => answer.Added),
            answers.Sum(answer => answer.Removed),
            answers.SelectMany(answer => answer.Files).ToList()
        );
    }

    /// <summary>The tags of the files of <paramref name="scope"/>, with the number of files having each.</summary>
    public Task<SfeScopeTags> GetScopeAsync(SfeTagScope scope, CancellationToken cancellationToken)
    {
        var asked = scope ?? throw new ArgumentNullException(nameof(scope));
        var route = _place.WithPath("file-tags/scope", SfePath.Normalize(asked.Folder));
        route += asked.Recursive ? "&recursive=true" : "";
        route += asked.Query is { } query ? $"&q={Routes.Escape(query)}" : "";
        return _transport.ReadAsync<SfeScopeTags>(SfeRequest.Get(route), cancellationToken);
    }

    /// <summary>Tags given to and taken from every file of <paramref name="scope"/>, in one request.</summary>
    public Task<SfeScopeTagChangeResult> ChangeScopeAsync(
        SfeTagScope scope,
        IEnumerable<int> add,
        IEnumerable<int> remove,
        CancellationToken cancellationToken
    )
    {
        var asked = scope ?? throw new ArgumentNullException(nameof(scope));
        var folder = SfePath.Normalize(asked.Folder);
        var body = new ScopeTagChange(folder, asked.Recursive, asked.Query, [.. add ?? []], [.. remove ?? []]);
        var request = SfeRequest.Post(_place.Route("file-tags/scope"), body);
        return _transport.ReadAsync<SfeScopeTagChangeResult>(request, cancellationToken);
    }
}
