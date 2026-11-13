using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Logs;
using Sfe.Core.Settings;

namespace Sfe.Api.Endpoints;

/// <summary>
/// How each kind of connection is set up on this server, for its administrators: whether it is offered, the
/// address to register at the provider, the app secret (write-only), and a test. The app key and the other
/// connection settings are ordinary server settings (<c>connections.*</c>).
/// </summary>
public static class ConnectionProviderEndpoints
{
    /// <summary>Where a provider sends the browser back after an OAuth sign-in, under the API's base.</summary>
    public const string CallbackPath = "/connections/oauth/callback";

    public static RouteGroupBuilder MapConnectionProviderEndpoints(this RouteGroupBuilder api)
    {
        var providers = api.MapGroup("/connections/providers").WithTags("Connections");

        providers
            .MapGet("/{kind}", Get)
            .RequireRight(Rights.ServerSettings)
            .WithName("GetConnectionProvider")
            .WithSummary(
                "How a kind of connection is set up: offered or not, the return address to register at the "
                    + "provider, and where its app secret comes from (never the secret itself)."
            );

        providers
            .MapPut("/{kind}/secret", SetSecret)
            .RequireRight(Rights.ServerSettings)
            .WithName("SetConnectionProviderSecret")
            .WithSummary(
                "Sets the secret of the provider's app; kept in a file readable by the server only, never sent back. "
                    + "A secret in the configuration wins over it."
            );

        providers
            .MapDelete("/{kind}/secret", RemoveSecret)
            .RequireRight(Rights.ServerSettings)
            .WithName("RemoveConnectionProviderSecret")
            .WithSummary("Removes the secret typed for the provider's app (sign-ins go on with PKCE alone).");

        providers
            .MapPost("/{kind}/test", Test)
            .RequireRight(Rights.ServerSettings)
            .WithName("TestConnectionProvider")
            .WithSummary("Checks the kind's settings with the provider when it can (app key and secret).");

        return api;
    }

    /// <summary>
    /// The address of <see cref="CallbackPath"/> as the browser reaches this server (forwarded headers and virtual
    /// directory applied): what the administrator registers at the provider, and what a sign-in sends.
    /// </summary>
    public static string ReturnAddress(HttpRequest request) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}{ApiRoutes.Base}{CallbackPath}";

    private static Results<Ok<ProviderSetupView>, NotFound> Get(
        string kind,
        HttpRequest request,
        ConnectionProviders providers,
        ProviderSecrets secrets,
        SettingLayers settings
    )
    {
        if (providers.Find(kind) is not { } provider)
            return TypedResults.NotFound();

        var options = settings.ServerLevel().Connections;
        string? secret = provider switch
        {
            IRegisteredApp app when app.SecretInConfiguration(options) => ProviderSetupView.SecretInConfiguration,
            IRegisteredApp when secrets.Get(provider.Kind) is not null => ProviderSetupView.SecretOnPage,
            IRegisteredApp => ProviderSetupView.NoSecret,
            _ => null,
        };
        return TypedResults.Ok(
            new ProviderSetupView(
                provider.Kind,
                provider.Name,
                ConnectionProviders.IsAllowed(provider.Kind, options) && provider.SignInMethods(options).Count > 0,
                ReturnAddress(request),
                secret
            )
        );
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> SetSecret(
        string kind,
        AppSecretInput input,
        [AsParameters] ProviderSecretServices services
    )
    {
        var (providers, secrets, current, loggers) = services;
        if (providers.Find(kind) is not IRegisteredApp || providers.Find(kind) is not { } provider)
            return TypedResults.NotFound();
        var secret = input.Secret?.Trim() ?? string.Empty;
        if (secret.Length is 0 or > ProviderSecrets.MaxLength || secret.Any(char.IsControl))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["secret"] = [$"The app secret as the provider shows it (at most {ProviderSecrets.MaxLength})."],
                }
            );
        }

        secrets.Set(provider.Kind, secret);
        var state = await current.GetAsync();
        loggers
            .CreateLogger(typeof(ConnectionProviderEndpoints))
            .LogSecurityWarning("App secret of {Kind} set by {User}", provider.Kind, state.User?.UserName);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> RemoveSecret(
        string kind,
        [AsParameters] ProviderSecretServices services
    )
    {
        var (providers, secrets, current, loggers) = services;
        if (providers.Find(kind) is not IRegisteredApp || providers.Find(kind) is not { } provider)
            return TypedResults.NotFound();

        secrets.Remove(provider.Kind);
        var state = await current.GetAsync();
        loggers
            .CreateLogger(typeof(ConnectionProviderEndpoints))
            .LogInformation("App secret of {Kind} removed by {User}", provider.Kind, state.User?.UserName);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ProviderTest>, NotFound>> Test(
        string kind,
        ConnectionProviders providers,
        SettingLayers settings,
        CancellationToken cancellationToken
    ) =>
        providers.Find(kind) is { } provider
            ? TypedResults.Ok(await provider.TestAsync(settings.ServerLevel().Connections, cancellationToken))
            : TypedResults.NotFound();
}
