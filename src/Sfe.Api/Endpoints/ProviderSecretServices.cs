using Sfe.Api.Security;
using Sfe.Core.Connections;

namespace Sfe.Api.Endpoints;

/// <summary>What setting or removing a provider app's secret uses (bound with <c>[AsParameters]</c>).</summary>
internal sealed record ProviderSecretServices(
    ConnectionProviders Providers,
    ProviderSecrets Secrets,
    CurrentAccess Current,
    ILoggerFactory Loggers
);
