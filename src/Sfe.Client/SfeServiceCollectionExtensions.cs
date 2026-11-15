using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Otools.Net.Sfe.Client;

/// <summary>Registers <see cref="SfeClient"/> in a program's services.</summary>
public static class SfeServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SfeClient"/> (transient: each gets a client of <c>IHttpClientFactory</c>, whose handlers
    /// the factory pools) with the options set by <paramref name="configure"/>. Answers the builder of its
    /// <see cref="HttpClient"/>, for handlers, a proxy or certificates of the program's.
    /// </summary>
    /// <param name="services">The program's services.</param>
    /// <param name="configure">Sets the server's address, the token, the library…</param>
    public static IHttpClientBuilder AddSfeClient(this IServiceCollection services, Action<SfeClientOptions> configure)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        services.AddOptions<SfeClientOptions>().Configure(configure);
        var builder = services.AddHttpClient(SfeClient.HttpClientName, http =>
            http.Timeout = System.Threading.Timeout.InfiniteTimeSpan
        );
        services.TryAddTransient(provider =>
        {
            var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(SfeClient.HttpClientName);
            return new SfeClient(http, provider.GetRequiredService<IOptions<SfeClientOptions>>().Value);
        });
        return builder;
    }
}
