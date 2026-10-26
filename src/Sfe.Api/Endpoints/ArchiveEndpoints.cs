using System.Buffers;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

public sealed record ArchiveRequest(IReadOnlyList<string?>? Paths);

/// <param name="Url">Where the browser downloads the zip, once, within <see cref="ArchiveLinks.Lifetime"/>.</param>
public sealed record ArchiveLink(string Url, string FileName);

/// <summary>
/// Several files, or folders with everything in them, downloaded as one zip (agents/docs/features/archives.md): the page
/// names them, then the browser downloads the zip, written while it is sent.
/// </summary>
public static class ArchiveEndpoints
{
    public static RouteGroupBuilder MapArchiveEndpoints(this RouteGroupBuilder api)
    {
        var archive = api.MapGroup("/files/archive").WithTags("Files");

        archive.MapPost("", Create)
            .RequireRight(Rights.Download)
            .FolderScoped("files.view and files.download at each item")
            .WithName("CreateArchive")
            .WithSummary("Names the files and folders of a zip; answers the link that downloads it.")
            .WithDescription($"""
                At most {Archives.MaxItems:N0} items. Each must be seen, openable and downloadable; a folder brings
                everything in it. The link works once, for the caller, within {ArchiveLinks.Lifetime.TotalMinutes:N0}
                minutes. 429 when the caller already downloads {ArchiveLinks.MaxPerCaller} zips; 400 when the space's
                `Files:MaxArchiveMegabytes` is smaller than what the index knows of them.
                """);
        archive.MapGet("/{token:regex(^[0-9a-f]{{48}}$)}", Download)
            .RequireRight(Rights.Download)
            .FolderScoped("files.view and files.download at each item again, and at each folder of the zip as it is written")
            .WithName("DownloadArchive")
            .WithSummary("Downloads a zip named by CreateArchive (once).")
            .WithDescription($"""
                Written while it is sent (no size known in advance). Links are not followed; folders the caller doesn't
                see are left out; links, unreadable files and folders the caller may not download are listed in
                “{Archives.LeftOutEntry}” inside the zip. A zip passing `Files:MaxArchiveMegabytes` (the index was behind)
                is cut off: the download fails.
                """);
        return api;
    }

    private static async Task<Results<Ok<ArchiveLink>, NotFound, ForbidHttpResult, ProblemHttpResult, ValidationProblem>> Create(
        Archives archives, ArchiveLinks links, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current, HttpContext context,
        ArchiveRequest request, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        var result = archives.Plan(request.Paths, new ArchiveAccess(state, paths, serverFiles, work));
        switch (result.Status)
        {
            case FileOperationStatus.Invalid:
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["paths"] = [result.Message!] });
            case FileOperationStatus.NotFound:
                return TypedResults.NotFound();
            case FileOperationStatus.Forbidden:
                return TypedResults.Forbid();
        }

        var caller = RequestLimits.Caller(context);
        if (!links.HasRoom(caller))
            return TypedResults.Problem($"You are already downloading {ArchiveLinks.MaxPerCaller} zips: wait for one to finish.", statusCode: StatusCodes.Status429TooManyRequests);
        var plan = result.Plan!;
        if (archives.MaxBytes > 0 && await archives.IndexedSizeAsync(plan, cancellationToken) is var size && size > archives.MaxBytes)
            return TypedResults.Problem(
                $"This is {size / 1024 / 1024:N0} MB: a zip may hold at most {archives.MaxBytes / 1024 / 1024:N0} MB here. Choose fewer files.",
                statusCode: StatusCodes.Status400BadRequest);

        var token = links.Add(state.TenantId, caller, [.. plan.Items.Select(item => item.Path)]);
        return TypedResults.Ok(new ArchiveLink($"api/files/archive/{token}", plan.FileName));
    }

    private static async Task<Results<EmptyHttpResult, NotFound, ForbidHttpResult, ProblemHttpResult>> Download(
        Archives archives, ArchiveLinks links, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current, HttpContext context,
        ILogger<Archives> logger, string token)
    {
        var state = await current.GetAsync();
        var caller = RequestLimits.Caller(context);
        if (links.Take(token, state.TenantId, caller) is not { } pending)
            return TypedResults.NotFound();
        // Checked again: rights and folders may have changed since the link was made.
        var access = new ArchiveAccess(state, paths, serverFiles, work);
        var result = archives.Plan(pending.Paths, access);
        if (result.Status != FileOperationStatus.Done)
            return result.Status == FileOperationStatus.Forbidden ? TypedResults.Forbid() : TypedResults.NotFound();
        using var running = links.TryBegin(caller);
        if (running is null)
            return TypedResults.Problem($"You are already downloading {ArchiveLinks.MaxPerCaller} zips: wait for one to finish.", statusCode: StatusCodes.Status429TooManyRequests);

        var plan = result.Plan!;
        var response = context.Response;
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        response.ContentType = "application/zip";
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(plan.FileName);
        response.Headers.ContentDisposition = disposition.ToString();
        response.Headers.CacheControl = "no-store";

        var who = state.User is { } user ? user.DisplayName ?? user.UserName : "A visitor";
        var cancellationToken = context.RequestAborted;
        try
        {
            await response.StartAsync(cancellationToken);
            var summary = await archives.WriteAsync(plan, new ResponseStream(response.BodyWriter, cancellationToken), access, cancellationToken);
            await response.BodyWriter.FlushAsync(cancellationToken);
            logger.LogInformation("{Account} downloaded {Name}: {Files} files, {Bytes:N0} bytes, {LeftOut} left out",
                who, plan.FileName, summary.Files, summary.Bytes, summary.LeftOut.Count);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("{Account} stopped downloading {Name}", who, plan.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The headers are gone: cut the connection, so that the browser says the download failed instead of keeping half a zip.
            logger.LogWarning(ex, "The zip {Name} of {Account} stopped", plan.FileName, who);
            context.Abort();
        }
        return TypedResults.Empty;
    }

    /// <summary>The caller's access in this request.</summary>
    /// <summary>
    /// The response as a stream for the zip, which writes small parts (headers, the directory) synchronously: those are
    /// kept in the response's buffer and sent with the next asynchronous write (the server refuses synchronous writes).
    /// </summary>
    private sealed class ResponseStream(PipeWriter writer, CancellationToken token) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => writer.Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer) => writer.Write(buffer);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            writer.Write(buffer.Span);
            var result = await writer.FlushAsync(cancellationToken.CanBeCanceled ? cancellationToken : token);
            if (result.IsCanceled || result.IsCompleted)
                throw new OperationCanceledException(token);
        }

        public override void Flush()
        {
        }

        public override async Task FlushAsync(CancellationToken cancellationToken) =>
            await writer.FlushAsync(cancellationToken.CanBeCanceled ? cancellationToken : token);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
