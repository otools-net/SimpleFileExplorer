using Sfe.Api.Security;
using Sfe.Core.Connections;

namespace Sfe.Api.Endpoints;

/// <summary>What the sign-in routes use (bound with <c>[AsParameters]</c>).</summary>
internal sealed record SignInServices(HttpRequest Request, CurrentAccess Current, ConnectionSignInFlow Flow);
