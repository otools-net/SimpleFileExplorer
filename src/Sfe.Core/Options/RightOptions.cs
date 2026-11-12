namespace Sfe.Core.Options;

/// <summary>Rights switched off for the whole server.</summary>
public sealed class RightOptions
{
    /// <summary>
    /// Rights nobody gets, not even administrators (e.g. <c>tracking.duplicatesRemove</c>, <c>files.*</c>;
    /// <c>folder:write</c> for every right that changes the library's files, <c>kind:write</c> for every right that
    /// changes anything): the functions they cover are gone from the server. See <see cref="Access.RightCatalog"/>.
    /// </summary>
    public List<string> Disabled { get; set; } = [];
}
