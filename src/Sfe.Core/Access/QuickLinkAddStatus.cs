namespace Otools.Net.Sfe.Core.Access;

public enum QuickLinkAddStatus
{
    Done,

    /// <summary>
    /// The list would hold more than <see cref="QuickLinkService.MaxLinks"/> links: nothing was changed.
    /// </summary>
    Full,
}
