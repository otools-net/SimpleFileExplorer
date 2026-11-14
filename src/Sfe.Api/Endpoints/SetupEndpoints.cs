using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Api.Endpoints;

public static class SetupEndpoints
{
    private const string CompleteSetupDescription = """
        Needs the setup code of the server's environment (`Sfe:Setup:Code`, at least 12 characters): without it
        the server cannot be set up from the web. Refused once an administrator exists, unless
        `Sfe:Setup:Recover` is on. A new install (no account yet) set up from the server's own computer, in the
        browser SFE opened or with the link its console shows (its token in the header `X-SFE-Setup-Link`),
        needs no code (`codeRequired` false in `GetSetup`, unless `Sfe:Setup:LocalWithoutCode` is off).
        `fastSignIn` turns on `Sfe:FastSignIn` for the browser SFE opens when it starts.
        """;

    public static RouteGroupBuilder MapSetupEndpoints(this RouteGroupBuilder api)
    {
        var setup = api.MapGroup("/setup").WithTags("Session").SessionOnly();

        setup
            .MapGet("/", GetSetup)
            .AllowAnonymous()
            .WithName("GetSetup")
            .WithSummary(
                "Whether the server still needs its first administrator, and whether its environment gives the setup "
                    + "code."
            );

        setup
            .MapPost("/", CompleteSetup)
            .AllowAnonymous()
            .RequireRateLimiting(SessionEndpoints.SignInRateLimit)
            .WithName("CompleteSetup")
            .WithSummary("Creates the first administrator (or, with Sfe:Setup:Recover, restores one) and signs it in.")
            .WithDescription(CompleteSetupDescription);

        return api;
    }

    private static async Task<Ok<SetupInfo>> GetSetup(
        HttpContext context,
        SetupService setup,
        IOptionsMonitor<SecurityOptions> security,
        CancellationToken cancellationToken
    )
    {
        var state = await setup.GetStateAsync(cancellationToken);
        return TypedResults.Ok(
            new SetupInfo(
                state.Required,
                state.Available,
                state.CodeConfigured,
                state.CodeTooShort,
                state.Recover,
                state.MinimumCodeLength,
                Math.Max(1, security.CurrentValue.MinimumPasswordLength),
                state.LibraryName,
                DetectHosting(),
                // Where the code is kept tells about the server: only while the setup page can be used.
                state.CodeGenerated,
                state.CodeGenerated && state.Available ? state.CodeFile : null,
                !await LocalVisitor.SetupCodeWaivedAsync(context)
            )
        );
    }

    private static async Task<Results<Ok<SessionInfo>, ValidationProblem, ProblemHttpResult>> CompleteSetup(
        HttpContext context,
        [AsParameters] SetupServices services,
        SetupInput input,
        CancellationToken cancellationToken
    )
    {
        var (setup, accounts, settings, loggers) = services;
        var result = await setup.CompleteAsync(
            input,
            await LocalVisitor.SetupCodeWaivedAsync(context),
            cancellationToken
        );
        switch (result.Status)
        {
            case AccessStatus.Invalid:
                return TypedResults.ValidationProblem(result.Errors!);
            case not AccessStatus.Ok:
                return TypedResults.Problem(result.Problem, statusCode: StatusCodes.Status409Conflict);
        }

        if (
            input.FastSignIn
            && !await settings.SaveServerValueAsync("fastSignIn", JsonValue.Create(true), cancellationToken)
        )
        {
            loggers
                .CreateLogger(typeof(SetupEndpoints))
                .LogWarning("Fast sign-in stays off: the server's configuration locks it (Sfe:Locked)");
        }

        await SfeSession.SignInAsync(context, result.Value!, null, persistent: false);
        await accounts.RecordSignInAsync(result.Value!.Id, cancellationToken);
        return TypedResults.Ok(await SessionEndpoints.DescribeAsync(context));
    }

    internal static HostingKind DetectHosting()
    {
        if (WindowsServiceHelpers.IsWindowsService())
            return HostingKind.WindowsService;
        if (SystemdHelpers.IsSystemdService())
            return HostingKind.Systemd;
        if (
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APP_POOL_ID"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH"))
        )
        {
            return HostingKind.Iis;
        }

        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            return HostingKind.Docker;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE")))
            return HostingKind.AppImage;
        return PortableBuild.IsPortable ? HostingKind.Portable : HostingKind.Other;
    }
}
