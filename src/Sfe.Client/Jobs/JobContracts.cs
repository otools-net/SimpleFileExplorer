using System.Collections.Generic;

namespace Otools.Net.Sfe.Client.Jobs;

// Requests of the job routes (files/jobs); their answers are SfeJobStatus.

/// <summary>The body of <c>POST files/jobs</c> for a move or copy.</summary>
internal sealed record JobRequest(
    SfeJobKind Kind,
    IReadOnlyList<string> Paths,
    string Target,
    SfeConflictChoice Conflicts,
    bool KeepAccess,
    bool KeepTags
);

/// <summary>The body of <c>POST files/jobs/{id}/answer</c>.</summary>
internal sealed record JobAnswer(SfeConflictChoice Choice, bool ForAll);
