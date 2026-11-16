using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Previews;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The file a preview route of a connection shows, checked: as the library's index holds it (no provider call; a
/// thumbnail), or as the provider tells it now (<c>fresh</c>: what is played or read must be its current version).
/// Its id and version make the cache's key, never the request. The right is checked at the path asked and where the
/// item is (its folder rules, by path and provider id).
/// </summary>
internal static class ConnectionPreviewFiles
{
    public static async Task<(PreviewAsk? Ask, IResult? Answer)> OpenAsync(
        PreviewRequest request,
        CancellationToken cancellationToken
    )
    {
        var (files, previews, open) = (request.Files, request.Previews, request.Open);
        var place = await ConnectionTagEndpoints.PlaceAsync(files, open, cancellationToken);
        RemoteItem? item = null;
        if (!request.Fresh && place is not null)
            item = await previews.Rows.FindAsync(place, request.Path, cancellationToken);
        if (item is null)
        {
            var answer = await files.RunAsync(open, async storage =>
            {
                item = await storage.GetAsync(request.Path, cancellationToken);
                return TypedResults.Ok();
            });
            if (item is null)
                return (null, answer is IStatusCodeHttpResult { StatusCode: 200 } ? TypedResults.NotFound() : answer);
        }
        if (item.IsFolder)
            return (null, TypedResults.NotFound());
        if (OpenConnection.AnswerOf(open.Check(request.Path, item, request.Right)) is { } refused)
            return (null, refused);

        var kind = previews.Classifier.Classify(RemoteSort.ExtensionOf(item));
        var caller = RequestLimits.Caller(files.Context);
        return (new PreviewAsk(open.Connection.Id, item, kind, open.Capabilities, place, caller), null);
    }
}
