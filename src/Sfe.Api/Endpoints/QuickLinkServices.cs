using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Library;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

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
