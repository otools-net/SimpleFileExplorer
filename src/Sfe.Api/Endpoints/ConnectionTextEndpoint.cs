using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// <c>GET api/connections/{id}/files/text</c>: a file of a connection read whole from its provider and decoded as the
/// library's text route does (<see cref="TextCodec"/>), for the text formats of the viewer. Read only: no parts, no
/// version to save over, no copy.
/// </summary>
internal static class ConnectionTextEndpoint
{
    public static readonly string Description = $"""
        `api/files/text` for a connection's file, read only: at most {TextEndpoints.MaxBytes / (1024 * 1024)} MB
        (413 `{ProblemTypes.TooLarge}`), 415 (`{ProblemTypes.NotText}`) for a file that isn't text; `encoding`
        reads it in another one. `access` never offers to save; `version` is the provider's content hash, else its
        size and date.
        """;

    public static async Task<IResult> Get(
        int id,
        string path,
        [AsParameters] ConnectionFileServices files,
        string? encoding = null,
        CancellationToken cancellationToken = default
    )
    {
        if (encoding is not null && !TextCodec.ReadEncodings.Contains(encoding))
        {
            return TypedResults.Problem(
                $"Read it as one of: {string.Join(", ", TextCodec.ReadEncodings)}.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }
        if (RemotePaths.Normalize(path) is not { Length: > 0 } normalized)
            return TypedResults.NotFound();
        var (open, answer) = await files.OpenAsync(id, Rights.ViewFiles, cancellationToken);
        if (open is null)
            return answer!;
        if (OpenConnection.AnswerOf(open.Check(normalized, Rights.ViewFiles)) is { } refused)
            return refused;
        var download = (await files.Current.GetAsync()).Has(Rights.Download);

        return await files.RunAsync(open, async storage =>
        {
            if (await storage.GetAsync(normalized, cancellationToken) is not { IsFolder: false } item)
                return TypedResults.NotFound();
            if (OpenConnection.AnswerOf(open.Check(normalized, item, Rights.ViewFiles)) is { } there)
                return there;
            if (item.Size > TextEndpoints.MaxBytes)
                return TooLarge();
            await using var content = await storage.OpenReadAsync(item.Path, null, cancellationToken);
            var bytes = await ReadAsync(content.Stream, cancellationToken);
            if (bytes is null)
                return TooLarge();
            return Answer(item, bytes, encoding, files.Context, download && open.At(normalized).Has(Rights.Download));
        });
    }

    private static IResult Answer(RemoteItem item, byte[] bytes, string? encoding, HttpContext context, bool download)
    {
        if (TextCodec.Decode(bytes, encoding) is not { } decoded)
        {
            return TypedResults.Problem(
                "It is not a text file.",
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                type: ProblemTypes.NotText
            );
        }
        var modified = item.Modified ?? default;
        var version = item.Hash ?? $"{bytes.LongLength}-{modified.Ticks}";
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(
            new TextFile(
                decoded.Text,
                decoded.Encoding,
                decoded.LineEnding,
                version,
                bytes.LongLength,
                DateTime.SpecifyKind(modified, DateTimeKind.Utc),
                new TextAccess(Overwrite: false, SaveCopy: false, Download: download, Permanent: false)
            )
        );
    }

    /// <summary>The whole content; null when it is over the limit (the provider's size can be wrong).</summary>
    private static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (bytes.Length + read > TextEndpoints.MaxBytes)
                return null;
            bytes.Write(buffer, 0, read);
        }
        return bytes.ToArray();
    }

    private static IResult TooLarge() =>
        TypedResults.Problem(
            $"It is too large to be shown as text (more than {TextEndpoints.MaxBytes / (1024 * 1024)} MB): "
                + "download it instead.",
            statusCode: StatusCodes.Status413PayloadTooLarge,
            type: ProblemTypes.TooLarge
        );
}
