using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the routes of quick links take from the container (bound with <c>[AsParameters]</c>; none of it is in the
/// OpenAPI document).
/// </summary>
internal sealed record QuickLinkServices(
    QuickLinkService Service,
    CurrentAccess Current,
    LibraryPaths Paths,
    MediaClassifier Classifier
);
