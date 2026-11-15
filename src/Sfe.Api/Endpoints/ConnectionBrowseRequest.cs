using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>A page of a connection's folder as the browse route asks it (<see cref="ConnectionBrowsing"/>).</summary>
/// <param name="Here">What the session may do in the folder (it sees it, or only passes through it).</param>
/// <param name="Filter">The children folder rules leave out (<see cref="ConnectionFolders.ListingFilterOf"/>).</param>
internal sealed record ConnectionBrowseRequest(
    OpenConnection Open,
    FolderAccess Here,
    RemoteListingQuery Asked,
    ListingFilter? Filter
);
