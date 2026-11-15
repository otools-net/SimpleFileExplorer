using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Otools.Net.Sfe.Client;

/// <summary>Where a job stands, as the server last told it.</summary>
public sealed class SfeJobStatus
{
    /// <summary>The job's id on the server.</summary>
    public int Id { get; init; }

    /// <summary>What it does.</summary>
    public SfeJobKind Kind { get; init; }

    /// <summary>Running, waiting for an answer, or finished.</summary>
    public SfeJobState State { get; init; }

    /// <summary>The folder it moves or copies to.</summary>
    public string Target { get; init; } = "";

    /// <summary>How many items it has.</summary>
    public int Total { get; init; }

    /// <summary>How many it has done.</summary>
    public int Done { get; init; }

    /// <summary>How many bytes it has to write, as far as known.</summary>
    public long BytesTotal { get; init; }

    /// <summary>How many it has written.</summary>
    public long BytesDone { get; init; }

    /// <summary>The item it works on.</summary>
    public string? Current { get; init; }

    /// <summary>The name clash it waits on (<see cref="SfeJobState.Waiting"/>); null otherwise.</summary>
    public SfeJobConflict? Conflict { get; init; }

    /// <summary>What became of each item done.</summary>
    public IReadOnlyList<SfeJobItem> Results { get; init; } = [];

    /// <summary>When it started.</summary>
    public DateTimeOffset Started { get; init; }

    /// <summary>When it finished; null while it runs.</summary>
    public DateTimeOffset? Finished { get; init; }

    /// <summary>Whether it has finished, done or cancelled.</summary>
    [JsonIgnore]
    public bool IsFinished => Finished is not null || State is SfeJobState.Completed or SfeJobState.Cancelled;
}
