using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Otools.Net.Sfe.Client.Http;

/// <summary>
/// Gives the generated client's requests the token and the library, as the facade's: Kiota's own bearer provider
/// refuses plain <c>http</c>, which servers at home use.
/// </summary>
internal sealed class SfeAuthenticationProvider(SfeClientOptions options) : IAuthenticationProvider
{
    public Task AuthenticateRequestAsync(
        RequestInformation request,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default
    )
    {
        if (options.Token is { Length: > 0 } token)
            request.Headers.TryAdd("Authorization", $"Bearer {token}");
        if (options.Library is { } library)
            request.Headers.TryAdd("X-SFE-Tenant", library.ToString(CultureInfo.InvariantCulture));
        return Task.CompletedTask;
    }
}
