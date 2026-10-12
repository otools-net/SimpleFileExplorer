using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Sfe.Api.Endpoints;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Api.Security;

public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Sign-in cookie, rights, and the keys signing both the cookie and the external players' links. Media are loaded
    /// by <c>&lt;img&gt;</c> and <c>&lt;video&gt;</c>, which send cookies but no other credentials: hence a cookie.
    /// </summary>
    public static IServiceCollection AddSfeSecurity(this IServiceCollection services, IConfiguration configuration, string contentRootPath)
    {
        var security = configuration.GetSection($"{SfeOptions.SectionName}:{nameof(SfeOptions.Security)}").Get<SecurityOptions>() ?? new SecurityOptions();

        // Kept on the disk: otherwise every restart (and every service account without a profile) signs everyone out.
        var keys = Path.GetFullPath(LibraryPaths.ExpandHome(security.KeysDirectory), contentRootPath);
        services.AddDataProtection()
            .SetApplicationName("SFE")
            .PersistKeysToFileSystem(new DirectoryInfo(keys));

        services.AddHttpContextAccessor();
        services.AddScoped<CurrentAccess>();
        services.AddSingleton<FileLinks>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = SfeSession.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                // Plain http on a home network has to work; behind https the cookie is marked secure.
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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
            });

        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, RightPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, RightHandler>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(SessionEndpoints.SignInRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, security.SignInsPerMinute), Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }
}
