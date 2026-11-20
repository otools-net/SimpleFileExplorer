using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Security;

public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Sign-in cookie, rights, and the keys signing both the cookie and the external players' links. Media are loaded
    /// by <c>&lt;img&gt;</c> and <c>&lt;video&gt;</c>, which send cookies but no other credentials: hence a cookie.
    /// </summary>
    public static IServiceCollection AddSfeSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath
    )
    {
        var security = configuration
            .GetSection($"{SfeOptions.SectionName}:{nameof(SfeOptions.Security)}")
            .Get<SecurityOptions>()
            ?? new SecurityOptions();

        AddSigningKeys(services, security, contentRootPath);

        services.AddHttpContextAccessor();
        services.AddScoped<CurrentAccess>();
        services.AddSingleton<FileLinks>();
        services.AddSingleton<FlashLinks>();
        services.AddSingleton<ArchiveLinks>();
        services.AddSingleton<LibraryLinks>();
        services.AddSingleton<ServerFiles>();

        AddSignIn(services, security);
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, RightPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, RightHandler>();

        AddForwardedHeaders(services, security);
        services.AddSingleton<SignInThrottle>();
        services.AddSingleton<AppPolicySource>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<FastSignIn>();
        services.AddSingleton<LocalSetupLink>();
        services.AddSingleton<CatalogBuildLimits>();

        AddRateLimits(services, security);
        return services;
    }

    /// <summary>
    /// The keys signing the cookie and the links, kept on the disk: otherwise every restart (and every service account
    /// without a profile) signs everyone out. Also what protects the connections' credentials.
    /// </summary>
    private static void AddSigningKeys(IServiceCollection services, SecurityOptions security, string contentRootPath)
    {
        var keys = KeysDirectory(security, contentRootPath);
        // Made here, for the server's account only, rather than by Data Protection with the process's default mode:
        // whoever reads a key forges an administrator's cookie. One that exists is left alone (WarnIfKeysAreOpen).
        TryCreatePrivate(keys);
        services.AddDataProtection().SetApplicationName("SFE").PersistKeysToFileSystem(new DirectoryInfo(keys));
        services.AddSingleton<Otools.Net.Sfe.Core.Connections.ICredentialProtector, ConnectionCredentials>();
    }

    /// <summary>The sign-in cookie, or the access token of a request that carries a bearer.</summary>
    private static void AddSignIn(IServiceCollection services, SecurityOptions security) =>
        services
            .AddAuthentication(AccessTokenAuthentication.SelectingScheme)
            // A request with a bearer is the token's alone (its cookies are ignored); any other, the cookie's.
            .AddPolicyScheme(AccessTokenAuthentication.SelectingScheme, "Session or access token", options =>
                options.ForwardDefaultSelector = context =>
                    BearerTokens.IsBearer(context.Request)
                        ? AccessTokenAuthentication.SchemeName
                        : CookieAuthenticationDefaults.AuthenticationScheme
            )
            .AddScheme<AuthenticationSchemeOptions, AccessTokenAuthentication>(
                AccessTokenAuthentication.SchemeName,
                null
            )
            .AddCookie(options => ConfigureCookie(options, security));

    private static void ConfigureCookie(CookieAuthenticationOptions options, SecurityOptions security)
    {
        options.Cookie.Name = SfeSession.CookieName;
        // Per port: two instances on one host don't share it.
        options.CookieManager = new InstanceCookieManager();
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Plain http on a home network has to work; behind https (or a trusted proxy's) the cookie is marked secure.
        options.Cookie.SecurePolicy = security.RequireHttps
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(Math.Max(1, security.SessionDays));
        options.SlidingExpiration = true;
        // An API answers with a status, the client shows its own sign-in page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = SfeSession.ValidateAsync;
    }

    /// <summary>A reverse proxy's visitor address and scheme: this computer's proxies always, others named.</summary>
    private static void AddForwardedHeaders(IServiceCollection services, SecurityOptions security) =>
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            var proxies = security.TrustedProxies.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim());
            foreach (var proxy in proxies)
                Trust(options, proxy);
        });

    /// <summary>Trusts a proxy named by its address or its network (<c>10.0.0.0/8</c>).</summary>
    /// <exception cref="InvalidOperationException">It is neither: the server does not start.</exception>
    private static void Trust(ForwardedHeadersOptions options, string proxy)
    {
        if (System.Net.IPNetwork.TryParse(proxy, out var network))
        {
            options.KnownIPNetworks.Add(network);
        }
        else if (System.Net.IPAddress.TryParse(proxy, out var address))
        {
            options.KnownProxies.Add(address);
        }
        else
        {
            throw new InvalidOperationException(
                $"Sfe:Security:TrustedProxies: “{proxy}” is not an address or a network (like 10.0.0.0/8)."
            );
        }
    }

    /// <summary>The rate limit policies of the routes, and how many sign-ins an address may try per minute.</summary>
    private static void AddRateLimits(IServiceCollection services, SecurityOptions security) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            RequestLimits.AddPolicies(options);
            options.AddPolicy(SessionEndpoints.SignInRateLimit, context =>
                RateLimitPartition.GetFixedWindowLimiter(AddressKeys.Of(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, security.SignInsPerMinute),
                    Window = TimeSpan.FromMinutes(1),
                })
            );
        });

    /// <summary>
    /// Says in the log, at start, that other accounts of the computer may read the keys folder (an install made before
    /// the server created it private, or one opened on purpose): its mode is the administrator's to change.
    /// </summary>
    public static void WarnIfKeysAreOpen(this WebApplication app)
    {
        var security = app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value;
        var keys = KeysDirectory(security, app.Environment.ContentRootPath);
        if (!SecretFiles.IsOpenToOthers(keys))
            return;
        app.Logger.LogWarning(
            "Other accounts of this computer may read {Keys}, which holds the keys signing the sign-in cookies: "
                + "whoever reads them signs in as anyone. Unless that is meant, run: chmod -R go-rwx \"{Keys}\"",
            keys,
            keys
        );
    }

    private static string KeysDirectory(SecurityOptions security, string contentRootPath) =>
        Path.GetFullPath(LibraryPaths.ExpandHome(security.KeysDirectory), contentRootPath);

    /// <summary>Best effort: where the folder cannot be made now, Data Protection reports it as before.</summary>
    private static void TryCreatePrivate(string directory)
    {
        try
        {
            SecretFiles.CreatePrivateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
