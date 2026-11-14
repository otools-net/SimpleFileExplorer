using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>What the sign-in routes use (bound with <c>[AsParameters]</c>).</summary>
internal sealed record SignInServices(HttpRequest Request, CurrentAccess Current, ConnectionSignInFlow Flow);
