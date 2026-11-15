using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary>
/// Every v1 route of the facade's areas has its method, or is named as left to <see cref="SfeClient.Raw"/>: a route
/// added in these areas fails here until the facade covers it (or says why not) in the same commit.
/// </summary>
[TestClass]
public sealed class FacadeCoverageTests
{
    /// <summary>
    /// The routes (below <c>/api/v1</c>) of the areas the facade covers: the route itself, or a prefix/.
    /// </summary>
    private static readonly string[] Areas =
    [
        "/config",
        "/browse",
        "/files/raw/",
        "/files/uploads",
        "/files/folders",
        "/files/rename",
        "/files/delete",
        "/files/jobs",
        "/files/trash",
        "/files/properties",
        "/files/archive",
        "/search",
        "/tags",
        "/file-tags",
        "/folder-default-tags",
        "/connections",
    ];

    /// <summary>
    /// Each route of those areas and the facade's member calling it, or why it is left to the raw client.
    /// </summary>
    private static readonly Dictionary<string, string> Covered = new()
    {
        ["GET /config"] = "Server.GetInfoAsync, Libraries.ListAsync, Libraries.GetCurrentAsync",
        ["GET /browse"] = "Files.ListAsync",
        ["GET /browse/siblings"] = "Raw: the web page's previous/next folder",
        ["POST /browse/covers"] = "Raw: pictures of folder cards",
        ["GET /files/raw/{path}"] = "Files.DownloadAsync, Files.OpenReadAsync",
        ["POST /files/uploads/check"] = "Files.UploadAsync (asking)",
        ["POST /files/uploads"] = "Files.UploadAsync",
        ["GET /files/uploads"] = "Files.UploadAsync (resume)",
        ["GET /files/uploads/{id}"] = "Files.UploadAsync (after a cut-off)",
        ["PUT /files/uploads/{id}"] = "Files.UploadAsync",
        ["POST /files/uploads/{id}/finish"] = "Files.UploadAsync",
        ["DELETE /files/uploads/{id}"] = "Files.UploadAsync (cancelled)",
        ["POST /files/folders"] = "Files.CreateFolderAsync",
        ["POST /files/rename"] = "Files.RenameAsync",
        ["POST /files/delete"] = "Files.DeleteAsync, Files.DeletePermanentlyAsync",
        ["POST /files/jobs/check"] = "Raw: who would gain access by a move (the web page's confirmation)",
        ["POST /files/jobs"] = "Files.MoveAsync, Files.CopyAsync",
        ["POST /files/jobs/compress"] = "Raw: zips made inside the library (not in phase 3)",
        ["POST /files/jobs/extract"] = "Raw: extraction (not in phase 3)",
        ["POST /files/jobs/transfer"] =
            "SfeConnection.CopyFromLibraryAsync, MoveFromLibraryAsync, CopyToLibraryAsync, MoveToLibraryAsync, "
            + "CopyToAsync, MoveToAsync",
        ["POST /files/jobs/{id}/retry"] = "SfeJob.RetryAsync",
        ["GET /files/jobs"] = "Raw: the caller's jobs (a job is followed through its SfeJob)",
        ["GET /files/jobs/{id}"] = "SfeJob.RefreshAsync, SfeJob.WaitAsync",
        ["POST /files/jobs/{id}/answer"] = "SfeJob.AnswerAsync",
        ["POST /files/jobs/{id}/cancel"] = "SfeJob.CancelAsync",
        ["GET /files/trash"] = "Trash.ListAsync",
        ["DELETE /files/trash"] = "Trash.EmptyAsync",
        ["GET /files/trash/count"] = "Trash.CountAsync",
        ["POST /files/trash/{id}/restore"] = "Trash.RestoreAsync",
        ["DELETE /files/trash/{id}"] = "Trash.DeleteAsync",
        ["GET /files/properties"] = "Properties.GetAsync, Files.GetAsync, Files.ExistsAsync",
        ["GET /files/properties/totals"] = "Properties.GetTotalsAsync",
        ["POST /files/properties/count"] = "Properties.CountAsync",
        ["POST /files/properties/checksum"] = "Properties.ChecksumAsync",
        ["GET /files/properties/jobs/{id}"] = "Properties.CountAsync, Properties.ChecksumAsync",
        ["DELETE /files/properties/jobs/{id}"] = "Properties.CountAsync, Properties.ChecksumAsync (cancelled)",
        ["POST /files/archive"] = "Archives.DownloadZipAsync",
        ["GET /files/archive/{token}"] = "Archives.DownloadZipAsync",
        ["GET /search"] = "Search.QueryAsync, Search.GetPageAsync, Search.CountAsync",
        ["GET /tags"] = "Tags.ListAsync, Tags.FindAsync",
        ["POST /tags"] = "Tags.CreateAsync",
        ["GET /tags/sidebar"] = "Raw: the web page's tag sidebar",
        ["DELETE /tags/history"] = "Raw: the sidebar's history of searched and viewed tags",
        ["GET /tags/{id}"] = "Tags.GetAsync",
        ["PUT /tags/{id}"] = "Tags.UpdateAsync",
        ["DELETE /tags/{id}"] = "Tags.DeleteAsync",
        ["GET /file-tags"] = "Tags.GetFileTagsAsync",
        ["POST /file-tags"] = "Tags.AddAsync, Tags.RemoveAsync, Tags.ChangeAsync",
        ["GET /file-tags/scope"] = "Raw: tags of a whole folder or search (the web page's scope dialog)",
        ["POST /file-tags/scope"] = "Raw: tagging a whole folder or search",
        ["POST /file-tags/viewed"] = "Raw: the web page's last viewed files",
        ["GET /folder-default-tags"] = "Raw: default tags of folders (folder administration)",
        ["PUT /folder-default-tags"] = "Raw: default tags of folders (folder administration)",
        ["GET /connections"] = "Connections.ListAsync, SfeConnection.GetInfoAsync, SfeConnection.GetCapabilitiesAsync",
        ["PATCH /connections/{id}"] = "Raw: managing a connection (administration)",
        ["DELETE /connections/{id}"] = "Raw: removing a connection (administration)",
        ["PUT /connections/{id}/libraries"] = "Raw: libraries offered a server connection (administration)",
        ["PUT /connections/{id}/groups"] = "Raw: groups using a connection (administration)",
        ["GET /connections/providers/{kind}"] = "Raw: a provider's setup (administration)",
        ["PUT /connections/providers/{kind}/secret"] = "Raw: a provider app's secret (administration)",
        ["DELETE /connections/providers/{kind}/secret"] = "Raw: a provider app's secret (administration)",
        ["POST /connections/providers/{kind}/test"] = "Raw: a provider's setup tested (administration)",
        ["POST /connections/authorize"] = "Raw: signing in to a provider goes through a browser (the web page)",
        ["POST /connections/authorize/complete"] = "Raw: signing in to a provider (the web page)",
        ["POST /connections/{id}/reauthorize"] = "Raw: signing in again (the web page)",
        ["GET /connections/oauth/callback"] = "Raw: the provider's redirect back to the browser",
        ["GET /connections/{id}/browse"] = "SfeConnectionFiles.ListAsync, SfeConnection.GetCapabilitiesAsync",
        ["GET /connections/{id}/files/raw/{path}"] = "SfeConnectionFiles.DownloadAsync, OpenReadAsync",
        ["GET /connections/{id}/thumbnails/{path}"] = "Raw: previews (the library's thumbnails are Raw too)",
        ["GET /connections/{id}/files/properties"] = "SfeConnectionFiles.GetPropertiesAsync, GetAsync, ExistsAsync",
        ["POST /connections/{id}/files/folders"] = "SfeConnectionFiles.CreateFolderAsync",
        ["POST /connections/{id}/files/rename"] = "SfeConnectionFiles.RenameAsync",
        ["POST /connections/{id}/files/delete"] = "SfeConnectionFiles.DeleteAsync",
        ["POST /connections/{id}/files/transfer"] = "SfeConnectionFiles.MoveAsync, CopyAsync",
        ["POST /connections/{id}/files/uploads/check"] = "SfeConnectionFiles.UploadAsync (asking)",
        ["POST /connections/{id}/files/uploads"] = "SfeConnectionFiles.UploadAsync, UploadFolderAsync",
        ["GET /connections/{id}/files/uploads"] = "SfeConnectionFiles.UploadAsync (resume)",
        ["GET /connections/{id}/files/uploads/{uploadId}"] = "SfeConnectionFiles.UploadAsync (after a cut-off)",
        ["PUT /connections/{id}/files/uploads/{uploadId}"] = "SfeConnectionFiles.UploadAsync",
        ["DELETE /connections/{id}/files/uploads/{uploadId}"] = "SfeConnectionFiles.UploadAsync (cancelled)",
        ["POST /connections/{id}/files/uploads/{uploadId}/finish"] = "SfeConnectionFiles.UploadAsync",
        ["POST /connections/{id}/archive"] = "SfeConnectionFiles.DownloadZipAsync",
        ["GET /connections/{id}/archive/{token}"] = "SfeConnectionFiles.DownloadZipAsync",
        ["GET /connections/{id}/folder-rules"] = "Raw: folder rules (administration of a library)",
        ["PUT /connections/{id}/folder-rules"] = "Raw: folder rules (administration of a library)",
        ["GET /connections/{id}/index"] = "Raw: the state of a connection's index (administration)",
        ["POST /connections/{id}/index/sync"] = "Raw: a connection's index synced now (administration)",
        ["GET /connections/{id}/search"] = "Raw: a connection's index searched (facade: connections-index phase 6)",
        ["GET /connections/{id}/file-tags"] = "Raw: a connection file's tags (facade: connections-index phase 6)",
        ["POST /connections/{id}/file-tags"] = "Raw: a connection's files tagged (facade: connections-index phase 6)",
        ["GET /connections/{id}/file-tags/scope"] = "Raw: tags of a connection's folder or search (facade: phase 6)",
        ["POST /connections/{id}/file-tags/scope"] = "Raw: a connection's folder or search tagged (facade: phase 6)",
        ["GET /connections/{id}/folders"] = "Raw: folders to put a rule on (administration of a library)",
    };

    [TestMethod]
    public void EveryRouteOfTheFacadesAreasIsCoveredOrLeftToRawByName()
    {
        var routes = AreaRoutes();

        var missing = routes.Except(Covered.Keys).ToList();
        var gone = Covered.Keys.Except(routes).ToList();

        Assert.IsEmpty(
            missing,
            $"New routes in the facade's areas: give each a method, or a line here. {string.Join(", ", missing)}"
        );
        Assert.IsEmpty(gone, $"Routes named here but not in the v1 document: {string.Join(", ", gone)}");
    }

    private static List<string> AreaRoutes([CallerFilePath] string here = "")
    {
        var document = Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "samples", "openapi", "v1.json");
        using var json = JsonDocument.Parse(File.ReadAllText(document));
        var routes = new List<string>();
        foreach (var path in json.RootElement.GetProperty("paths").EnumerateObject())
        {
            var route = path.Name["/api/v1".Length..];
            if (
                !Areas.Any(area =>
                    route == area.TrimEnd('/') || route.StartsWith(area.TrimEnd('/') + "/", StringComparison.Ordinal)
                )
            )
                continue;
            foreach (
                var method in path
                    .Value.EnumerateObject()
                    .Where(m => m.Name is "get" or "post" or "put" or "delete" or "patch")
            )
                routes.Add($"{method.Name.ToUpperInvariant()} {route}");
        }
        return routes;
    }
}
