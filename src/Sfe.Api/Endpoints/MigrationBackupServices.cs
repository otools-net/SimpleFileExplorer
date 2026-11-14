using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Api.Maintenance;
using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the route that makes the backup before an update takes from the container (bound with
/// <c>[AsParameters]</c>; none of it is in the OpenAPI document). A record of its own: the route runs without a
/// session.
/// </summary>
internal sealed record MigrationBackupServices(
    MigrationCoordinator Migration,
    IOptionsMonitor<SetupOptions> Setup,
    ILoggerFactory Loggers,
    BackupSources Backups,
    BackupDownloads Downloads
);
