using Sfe.Api.Security;
using Sfe.Core.Connections;

namespace Sfe.Api.Endpoints;

/// <summary>What the list of connections uses (bound with <c>[AsParameters]</c>).</summary>
internal sealed record ConnectionListServices(
    CurrentAccess Current,
    ConnectionService Connections,
    ConnectionAccess Access,
    Sfe.Core.Access.AccessService Accounts
);
