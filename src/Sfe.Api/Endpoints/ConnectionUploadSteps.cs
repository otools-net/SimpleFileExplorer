using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// A chunk and a finish of an upload into a connection, once its gate is held (<see cref="RemoteUpload.Gate"/>):
/// the provider called in a turn of the connection (<see cref="ConnectionFileServices.RunAsync"/>), the record moved
/// on only when the provider took the bytes.
/// </summary>
internal static class ConnectionUploadSteps
{
    /// <summary>Bytes read from the request at a time.</summary>
    private const int ReadBufferBytes = 81_920;

    /// <summary>
    /// Passes the request's body on at <paramref name="offset"/>: 409 with the upload at another offset (also the
    /// provider's, when it has more than SFE knew: a part whose answer was lost), 400 for a chunk too large.
    /// </summary>
    public static async Task<IResult> AppendAsync(ConnectionUploadStep step, long offset)
    {
        var (files, open, services, upload) = step;
        var cancellationToken = files.Context.RequestAborted;
        if (offset != upload.Received)
            return TypedResults.Conflict(upload.ToInfo());

        var limit = Math.Min(Uploads.MaxChunkBytes, open.Capabilities.MaxUploadPart ?? long.MaxValue);
        await using var part = await ReadPartAsync(files.Context.Request, limit, cancellationToken);
        if (part is null || offset + part.Length > upload.Size)
        {
            return TypedResults.Problem(
                $"A chunk is at most {limit / 1024 / 1024} MB, and ends at the size of the file.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        return await files.RunAsync(open, async storage =>
        {
            try
            {
                await storage.AppendUploadAsync(upload.Session, offset, part, cancellationToken);
            }
            catch (RemoteStorageException error) when (error.Kind == RemoteErrorKind.WrongOffset)
            {
                return Realign(services.Uploads, upload, error.Offset);
            }
            services.Uploads.Advance(upload, offset + part.Length);
            return TypedResults.Ok(upload.ToInfo());
        });
    }

    /// <summary>
    /// Makes the file: a name taken is answered (ask) or the upload dropped (skip) before the provider finishes,
    /// so its session stays for another try.
    /// </summary>
    public static async Task<IResult> FinishAsync(ConnectionUploadStep step, ConflictChoice choice)
    {
        var (files, open, services, upload) = step;
        var cancellationToken = files.Context.RequestAborted;
        if (upload.Received != upload.Size)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["name"] = [$"“{upload.Name}” is not fully uploaded yet."] }
            );
        }

        var path = RemotePaths.Combine(upload.Target, upload.Name);
        return await files.RunAsync(open, async storage =>
        {
            var asksFirst = choice is ConflictChoice.Ask or ConflictChoice.Skip;
            if (asksFirst && await storage.GetAsync(path, cancellationToken) is not null)
                return Taken(services.Uploads, upload, choice);

            var target = new RemoteUploadTarget(path, upload.Size, ConflictOf(choice), upload.Modified);
            var item = await storage.FinishUploadAsync(upload.Session, target, cancellationToken);
            services.Uploads.Remove(upload);
            services.Listings.Forget(open.Connection.Id);
            return TypedResults.Ok(ConnectionListings.ToItem(item, services.Classifier));
        });
    }

    /// <summary>The request's body, up to <paramref name="limit"/> bytes; null when it is longer.</summary>
    private static async Task<MemoryStream?> ReadPartAsync(
        HttpRequest request,
        long limit,
        CancellationToken cancellationToken
    )
    {
        if (request.ContentLength > limit)
            return null;
        var part = new MemoryStream();
        var buffer = new byte[ReadBufferBytes];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (part.Length + read > limit)
            {
                await part.DisposeAsync();
                return null;
            }
            part.Write(buffer, 0, read);
        }
        part.Position = 0;
        return part;
    }

    /// <summary>The provider has another offset: the record takes it when it is told and possible.</summary>
    private static IResult Realign(RemoteUploads uploads, RemoteUpload upload, long? offset)
    {
        if (offset is { } known && known >= 0 && known <= upload.Size)
            uploads.Advance(upload, known);
        return TypedResults.Conflict(upload.ToInfo());
    }

    private static IResult Taken(RemoteUploads uploads, RemoteUpload upload, ConflictChoice choice)
    {
        if (choice == ConflictChoice.Skip)
        {
            uploads.Remove(upload);
            return TypedResults.NoContent();
        }
        return TypedResults.Problem(
            $"“{RemotePaths.NameOf(upload.Name)}” is already there.",
            statusCode: StatusCodes.Status409Conflict,
            type: FileEndpoints.ExistsType
        );
    }

    private static RemoteConflict ConflictOf(ConflictChoice choice) =>
        choice switch
        {
            ConflictChoice.Replace => RemoteConflict.Replace,
            ConflictChoice.KeepBoth => RemoteConflict.KeepBoth,
            _ => RemoteConflict.Fail,
        };
}
