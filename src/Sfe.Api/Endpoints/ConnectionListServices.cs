using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>What the list of connections uses (bound with <c>[AsParameters]</c>).</summary>
internal sealed record ConnectionListServices(
    CurrentAccess Current,
    ConnectionService Connections,
    ConnectionAccess Access,
    Otools.Net.Sfe.Core.Access.AccessService Accounts
);
