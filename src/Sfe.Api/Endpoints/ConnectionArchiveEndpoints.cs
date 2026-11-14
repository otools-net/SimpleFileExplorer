using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Files;

namespace Sfe.Api.Endpoints;

/// <summary>
/// Several files or folders of a connection downloaded as one zip: the library's two steps
/// (<see cref="ArchiveEndpoints"/>), the link bound to the connection, the zip written from the provider while it
/// is sent (<see cref="RemoteArchives"/>).
/// </summary>
public static class ConnectionArchiveEndpoints
{
    private static readonly string CreateDescription = $"""
        At most {Archives.MaxItems:N0} items, checked at the provider; a folder brings everything in it. The link
        works once, for the caller, within {ArchiveLinks.Lifetime.TotalMinutes:N0} minutes; 429 when the caller
        already downloads {ArchiveLinks.MaxPerCaller} zips.
        """;

    public static RouteGroupBuilder MapConnectionArchiveEndpoints(this RouteGroupBuilder api)
    {
        var archive = api.MapGroup("/connections/{id:int}/archive").WithTags("Connections");

        archive
            .MapPost("", Create)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.view and files.download; 404 for a connection the subject may not use")
            .WithName("CreateConnectionArchive")
            .WithSummary("Names the files and folders of a connection's zip; answers the link that downloads it.")
            .WithDescription(CreateDescription);
        archive
            .MapGet("/{token:regex(^[0-9a-f]{{48}}$)}", Download)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.view and files.download again; 404 for a connection the subject may not use")
            .WithName("DownloadConnectionArchive")
            .WithSummary(
                $"Downloads a zip named by CreateConnectionArchive (once); what has no content is listed in "
                    + $"“{Archives.LeftOutEntry}”."
            );
        return api;
    }

    private static async Task<IResult> Create(
        int id,
        ArchiveRequest request,
        [AsParameters] ConnectionFileServices files,
        ArchiveLinks links,
        CancellationToken cancellationToken
    )
    {
        var (open, answer) = await OpenAsync(files, id, cancellationToken);
        if (open is null)
            return answer!;
        var caller = RequestLimits.Caller(files.Context);
        if (!links.HasRoom(caller))
            return TooMany();

        return await files.RunAsync(open, async storage =>
        {
            var result = await RemoteArchives.PlanAsync(
                storage,
                request.Paths,
                open.Connection.Name,
                cancellationToken
            );
            if (result.Invalid is { } invalid)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["paths"] = [invalid] });
            if (result.Plan is not { } plan)
                return TypedResults.NotFound();

            var state = await files.Current.GetAsync();
            var token = links.Add(state.TenantId, caller, [.. plan.Items.Select(i => i.Path)], Scope(id));
            return TypedResults.Ok(new ArchiveLink(ApiRoutes.Link($"connections/{id}/archive/{token}"), plan.FileName));
        });
    }

    private static async Task<IResult> Download(
        int id,
        string token,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionArchiveServices archives
    )
    {
        var context = files.Context;
        var state = await files.Current.GetAsync();
        var caller = RequestLimits.Caller(context);
        if (archives.Links.Take(token, state.TenantId, caller, Scope(id)) is not { } pending)
            return TypedResults.NotFound();
        // Checked again: the connection, its sign-in and the rights may have changed since the link was made.
        var (open, answer) = await OpenAsync(files, id, context.RequestAborted);
        if (open is null)
            return answer!;
        using var running = archives.Links.TryBegin(caller);
        if (running is null)
            return TooMany();

        return await files.RunAsync(open, async storage =>
        {
            var result = await RemoteArchives.PlanAsync(
                storage,
                pending.Paths,
                open.Connection.Name,
                context.RequestAborted
            );
            if (result.Plan is not { } plan)
                return TypedResults.NotFound();
            await StreamAsync(context, storage, plan, archives);
            return TypedResults.Empty;
        });
    }

    private static async Task StreamAsync(
        HttpContext context,
        IRemoteStorage storage,
        RemoteArchivePlan plan,
        ConnectionArchiveServices archives
    )
    {
        var response = context.Response;
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        response.ContentType = "application/zip";
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(plan.FileName);
        response.Headers.ContentDisposition = disposition.ToString();
        response.Headers.CacheControl = "no-store";
        var cancellationToken = context.RequestAborted;
        try
        {
            await response.StartAsync(cancellationToken);
            var output = new ArchiveEndpoints.ResponseStream(response.BodyWriter, cancellationToken);
            var summary = await RemoteArchives.WriteAsync(
                storage,
                plan,
                output,
                archives.Archives.MaxBytes,
                cancellationToken
            );
            await response.BodyWriter.FlushAsync(cancellationToken);
            archives.Logger.LogInformation(
                "A zip of a connection's files was downloaded: {Files} files, {Bytes:N0} bytes, {LeftOut} left out",
                summary.Files,
                summary.Bytes,
                summary.LeftOut.Count
            );
        }
        catch (OperationCanceledException)
        {
            archives.Logger.LogInformation("A download of a zip of a connection's files was stopped");
        }
        catch (Exception ex) when (ex is IOException or RemoteStorageException)
        {
            // The headers are gone: cut the connection, so that the browser says the download failed.
            archives.Logger.LogWarning(ex, "A zip of a connection's files stopped");
            context.Abort();
        }
    }

    /// <summary>The connection, for whoever may view and download its files.</summary>
    private static async Task<(OpenConnection? Open, IResult? Answer)> OpenAsync(
        ConnectionFileServices files,
        int id,
        CancellationToken cancellationToken
    )
    {
        var opened = await files.OpenAsync(id, Rights.Download, cancellationToken);
        if (opened.Open is not null && !(await files.Current.GetAsync()).Has(Rights.ViewFiles))
            return (null, TypedResults.Forbid());
        return opened;
    }

    private static string Scope(int id) => $"connection:{id}";

    private static IResult TooMany() =>
        TypedResults.Problem(
            $"You are already downloading {ArchiveLinks.MaxPerCaller} zips: wait for one to finish.",
            statusCode: StatusCodes.Status429TooManyRequests
        );
}
