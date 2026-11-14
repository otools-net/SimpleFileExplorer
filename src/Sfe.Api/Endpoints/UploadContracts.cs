using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="Target">The folder the files would go into.</param>
/// <param name="Names">Their names, or paths below it for a folder upload.</param>
public sealed record UploadCheckRequest(string? Target, IReadOnlyList<string>? Names);

/// <param name="Existing">The names (of <see cref="UploadCheckRequest.Names"/>) already taken there, seen by the caller
/// or not.</param>
/// <param name="CanReplace">The caller may replace what is there (it would go to the trash).</param>
public sealed record UploadCheck(IReadOnlyList<string> Existing, bool CanReplace);

public sealed record UploadFinish(ConflictChoice Conflicts = ConflictChoice.Ask);
