namespace Otools.Net.Sfe.Core.Library;

/// <summary>
/// What a listing leaves out (folder rules): the entries named in <paramref name="Hidden"/>, or, when
/// <paramref name="Only"/> is set, every entry but those (a folder shown only to lead to what is below it). Names
/// compare as the host file system does.
/// </summary>
public sealed record ListingFilter(IReadOnlySet<string> Hidden, IReadOnlySet<string>? Only = null)
{
    public bool Shows(string name) => !Hidden.Contains(name) && (Only is null || Only.Contains(name));
}
