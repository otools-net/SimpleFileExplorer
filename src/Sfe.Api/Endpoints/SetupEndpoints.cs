using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Options;

namespace Sfe.Api.Endpoints;

/// <summary>How the server runs, so that the setup page shows how to give it a setup code there first.</summary>
public enum HostingKind
{
    WindowsService,
    Systemd,
    Iis,
    Docker,
    AppImage,
    Portable,
    Other,
}

/// <param name="Hosting">How the server runs (the setup page explains how to set the code for it).</param>
public sealed record SetupInfo(bool Required, bool Available, bool CodeConfigured, bool CodeTooShort, bool Recover, int MinimumCodeLength,
    int MinimumPasswordLength, string LibraryName, HostingKind Hosting);

public static class SetupEndpoints
{
    public static RouteGroupBuilder MapSetupEndpoints(this RouteGroupBuilder api)
    {
        var setup = api.MapGroup("/setup").WithTags("Session");

        setup.MapGet("/", GetSetup)
            .AllowAnonymous()
            .WithName("GetSetup")
            .WithSummary("Whether the server still needs its first administrator, and whether its environment gives the setup code.");

        setup.MapPost("/", CompleteSetup)
            .AllowAnonymous()
            .RequireRateLimiting(SessionEndpoints.SignInRateLimit)
            .WithName("CompleteSetup")
            .WithSummary("Creates the first administrator (or, with Sfe:Setup:Recover, restores one) and signs it in.")
            .WithDescription("""
                Needs the setup code of the server's environment (`Sfe:Setup:Code`, at least 12 characters): without it the
                server cannot be set up from the web. Refused once an administrator exists, unless `Sfe:Setup:Recover` is on.
                """);

        return api;
    }

    private static async Task<Ok<SetupInfo>> GetSetup(SetupService setup, IOptionsMonitor<SecurityOptions> security,
        CancellationToken cancellationToken)
    {
        var state = await setup.GetStateAsync(cancellationToken);
        return TypedResults.Ok(new SetupInfo(state.Required, state.Available, state.CodeConfigured, state.CodeTooShort, state.Recover, state.MinimumCodeLength,
            Math.Max(1, security.CurrentValue.MinimumPasswordLength), state.LibraryName, DetectHosting()));
    }

    private static async Task<Results<Ok<SessionInfo>, ValidationProblem, ProblemHttpResult>> CompleteSetup(
        HttpContext context, SetupService setup, AccessService accounts, SetupInput input, CancellationToken cancellationToken)
    {
        var result = await setup.CompleteAsync(input, cancellationToken);
        switch (result.Status)
        {
            case AccessStatus.Invalid:
                return TypedResults.ValidationProblem(result.Errors!);
            case not AccessStatus.Ok:
                return TypedResults.Problem(result.Problem, statusCode: StatusCodes.Status409Conflict);
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
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APP_POOL_ID")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH")))
            return HostingKind.Iis;
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            return HostingKind.Docker;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE")))
            return HostingKind.AppImage;
        return PortableBuild.IsPortable ? HostingKind.Portable : HostingKind.Other;
    }
}
