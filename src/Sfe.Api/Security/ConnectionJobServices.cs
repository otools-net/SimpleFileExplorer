using Sfe.Core.Access;
using Sfe.Core.Connections;

namespace Sfe.Api.Security;

/// <summary>What <see cref="ConnectionJobAccess"/> reads the person's access with as a job runs.</summary>
public sealed record ConnectionJobServices(
    AccessService Accounts,
    RightsEvaluator Rights,
    ConnectionAccess Connections,
    AccessChanges Changes
);
