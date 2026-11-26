namespace Otools.Net.Sfe.Core.Access;

/// <summary>What a subject may do in a library.</summary>
/// <param name="IsGuest">A visitor without an account or a guest account: the guest limit applies.</param>
/// <param name="IsMember">Belongs to the library (a group there, or a server administrator); visitors never do.</param>
/// <param name="Folders">Where it may use its rights (folder rules); null = everywhere.</param>
/// <param name="SpaceDisabled">The rights the library switches off for everyone (its <b>Files read-only</b> and
/// <b>Read-only</b> switches); null when unknown.</param>
public sealed record EffectiveAccess(
    IReadOnlySet<string> Rights,
    bool IsGuest,
    bool IsServerAdmin,
    bool IsMember,
    FolderPolicy? Folders = null,
    IReadOnlySet<string>? SpaceDisabled = null
)
{
    public static readonly EffectiveAccess None = new(new HashSet<string>(), true, false, false);

    /// <summary>Has the right somewhere in the library (what <c>RequireRight</c> checks).</summary>
    public bool Has(string right) => Rights.Contains(right);

    public FolderPolicy FolderPolicy => Folders ?? FolderPolicy.Unrestricted;

    /// <summary>What it may do at a root-relative, canonical path.</summary>
    public FolderAccess At(string path) => FolderPolicy.At(path, Rights);

    /// <summary>Where it may use <paramref name="right"/>, for the queries over the index.</summary>
    public PathFilter FilterFor(string right) => FolderPolicy.FilterFor(right, Rights);
}
