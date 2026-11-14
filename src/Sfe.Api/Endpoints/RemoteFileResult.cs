using Microsoft.Net.Http.Headers;
using Sfe.Api.Security;
using Sfe.Core.Connections;

namespace Sfe.Api.Endpoints;

/// <summary>
/// A connection's file, streamed from the provider as it reads (nothing kept on the server's disk), under the
/// sandbox rules of the library's files (<see cref="LibraryEndpoints.ServeFile"/>): its own content policy, and a type
/// a page could run only ever downloaded. Answers 206 with the part the provider served, or 200 with the whole file
/// when it could not serve the part (no <c>Accept-Ranges</c> then).
/// </summary>
/// <param name="Ranged">The provider reads parts (<see cref="ProviderCapabilities.RangedReads"/>).</param>
public sealed class RemoteFileResult(RemoteContent content, RemoteItem item, bool download, bool ranged) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        await using var _ = content;
        var response = httpContext.Response;
        var contentType = LibraryEndpoints.ContentTypeOf(item.Name);
        response.ContentType = contentType;
        response.Headers[HeaderNames.ContentSecurityPolicy] = SecurityHeaders.FilePolicy;
        response.Headers[HeaderNames.CacheControl] = "no-store";
        if (download || !LibraryEndpoints.OpensInline(contentType))
        {
            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(item.Name);
            response.Headers[HeaderNames.ContentDisposition] = disposition.ToString();
        }
        if (item.Modified is { } modified)
            response.GetTypedHeaders().LastModified = new DateTimeOffset(modified, TimeSpan.Zero);

        var total = content.TotalLength ?? item.Size;
        if (content.Range is { } served && total is { } length)
        {
            var to = served.To ?? length - 1;
            response.StatusCode = StatusCodes.Status206PartialContent;
            response.Headers[HeaderNames.ContentRange] = $"bytes {served.From}-{to}/{length}";
            response.ContentLength = to - served.From + 1;
            response.Headers[HeaderNames.AcceptRanges] = "bytes";
        }
        else
        {
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentLength = content.Length ?? total;
            if (ranged && total is not null)
                response.Headers[HeaderNames.AcceptRanges] = "bytes";
        }

        if (HttpMethods.IsHead(httpContext.Request.Method))
            return;
        await content.Stream.CopyToAsync(response.Body, httpContext.RequestAborted);
    }
}
