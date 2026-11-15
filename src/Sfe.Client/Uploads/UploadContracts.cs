using System;
using System.Collections.Generic;

namespace Otools.Net.Sfe.Client.Uploads;

// Requests and answers of the upload routes (files/uploads), the parts the facade uses.

/// <summary>An unfinished upload, as the server keeps it (<c>UploadInfo</c>).</summary>
internal sealed class UploadInfo
{
    public string Id { get; init; } = "";
    public string Target { get; init; } = "";
    public string Name { get; init; } = "";
    public long Size { get; init; }
    public long Received { get; init; }
    public DateTimeOffset? Modified { get; init; }
}

/// <summary>The body of <c>POST files/uploads</c>.</summary>
internal sealed record UploadStart(string Target, string Name, long Size, DateTimeOffset? Modified);

/// <summary>The body of <c>POST files/uploads/check</c>.</summary>
internal sealed record UploadCheckRequest(string Target, IReadOnlyList<string> Names);

/// <summary>The answer of <c>POST files/uploads/check</c>.</summary>
internal sealed class UploadCheck
{
    public List<string> Existing { get; init; } = [];
}

/// <summary>The body of <c>POST files/uploads/{id}/finish</c>.</summary>
internal sealed record UploadFinish(SfeConflictChoice Conflicts);
