using Otools.Net.Sfe.Core.Indexing;
using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What <c>GET api/browse</c> takes from the index (bound with <c>[AsParameters]</c>; none of it is in the OpenAPI
/// document): the files' tags and marks, the folder's default tags, and scan as you go.
/// </summary>
internal sealed record BrowseIndex(TagService Tags, FolderDefaultTags DefaultTags, ListingIndexer Indexing);
