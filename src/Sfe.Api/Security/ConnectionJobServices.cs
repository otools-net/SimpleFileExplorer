using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Api.Security;

/// <summary>What <see cref="ConnectionJobAccess"/> reads the person's access with as a job runs.</summary>
public sealed record ConnectionJobServices(
    AccessService Accounts,
    RightsEvaluator Rights,
    ConnectionAccess Connections,
    AccessChanges Changes
);
