using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Maintenance;
using Sfe.Api.Security;
using Sfe.Api.Tenancy;
using Sfe.Core.Library;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

public sealed record InstallationCheck(string? Code);

/// <param name="Token">Sent back in the <c>X-SFE-Installation</c> header by the other installation routes, for an hour.</param>
public sealed record InstallationGrant(string Token, InstallationView Settings);

/// <param name="RestartRequired">The new listening address applies once SFE is started again.</param>
public sealed record InstallationSaved(InstallationView Settings, bool RestartRequired);

/// <param name="Path">Full path of a folder on the server.</param>
public sealed record ServerFolder(string Name, string Path);

/// <param name="Path">The folder listed; null for the top (drives, or <c>/</c>).</param>
/// <param name="Parent">Its parent; null at the top of a drive.</param>
/// <param name="Places">At the top: the home folder, the current library folder and the usual media folders.</param>
/// <param name="Truncated">More sub-folders than <see cref="InstallationEndpoints.MaxFolders"/>: only the first ones are listed.</param>
public sealed record ServerFolders(string? Path, string? Parent, IReadOnlyList<ServerFolder> Folders, IReadOnlyList<ServerFolder> Places, bool Truncated);

/// <summary>
/// The installation page (setup wizard, Administration › System › Installation): the first library's folder, the
/// listening address and ffmpeg, written to <see cref="InstallationSettings"/>. These decide where data lives and which
/// program runs, so they need the setup code, not a right: only whoever runs the server knows it.
/// </summary>
public static class InstallationEndpoints
{
    public const int MaxFolders = 2000;

    public static RouteGroupBuilder MapInstallationEndpoints(this RouteGroupBuilder api)
    {
        var installation = api.MapGroup("/installation").WithTags("Installation");

        installation.MapPost("/check", Check)
            .AllowAnonymous()
            .RequireRateLimiting(SessionEndpoints.SignInRateLimit)
            .WithName("CheckInstallationCode")
            .WithSummary("Checks the setup code and answers the installation settings with a token for the other installation routes.")
            .WithDescription("The token goes in the `X-SFE-Installation` header; it lasts an hour, and ends when the setup code changes.");

        installation.MapGet("/", Get)
            .AllowAnonymous()
            .WithName("GetInstallation")
            .WithSummary("The library folder, listening address and ffmpeg of the server, and which of them its environment decides.")
            .WithDescription("Needs the token of `CheckInstallationCode` in the `X-SFE-Installation` header.");

        installation.MapPut("/", Save)
            .AllowAnonymous()
            .WithName("SaveInstallation")
            .WithSummary("Saves the library folder, listening address or ffmpeg into the installation file (data/installation.json).")
            .WithDescription("""
                Needs the token of `CheckInstallationCode` in the `X-SFE-Installation` header. A new folder or ffmpeg starts
                the libraries again at once; a new address applies after a restart (`restartRequired`). Values set by the
                server's environment variables or command line cannot be changed here.
                """);

        installation.MapGet("/folders", Folders)
            .AllowAnonymous()
            .WithName("ListServerFolders")
            .WithSummary("The folders of the server's disks, to pick the library folder or ffmpeg's folder.")
            .WithDescription("Needs the token of `CheckInstallationCode` in the `X-SFE-Installation` header. Without `path`: the drives and usual places.");

        return api;
    }

    private static async Task<Results<Ok<InstallationGrant>, ValidationProblem>> Check(
        InstallationAccess access, InstallationSettings settings, ILoggerFactory loggers, InstallationCheck input, CancellationToken cancellationToken)
    {
        if (access.Grant(input.Code) is not { } token)
        {
            loggers.CreateLogger(typeof(InstallationEndpoints)).LogWarning("Installation page refused: wrong setup code");
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["This is not the server's setup code."] });
        }

        return TypedResults.Ok(new InstallationGrant(token, await settings.DescribeAsync(cancellationToken)));
    }

    private static async Task<Results<Ok<InstallationView>, UnauthorizedHttpResult>> Get(
        HttpContext context, InstallationAccess access, InstallationSettings settings, CancellationToken cancellationToken) =>
        access.IsGranted(context) ? TypedResults.Ok(await settings.DescribeAsync(cancellationToken)) : TypedResults.Unauthorized();

    private static async Task<Results<Ok<InstallationSaved>, ValidationProblem, UnauthorizedHttpResult>> Save(
        HttpContext context, InstallationAccess access, InstallationSettings settings, TenantRegistry libraries, ILoggerFactory loggers,
        InstallationInput input, CancellationToken cancellationToken)
    {
        if (!access.IsGranted(context))
            return TypedResults.Unauthorized();

        var (errors, changed) = settings.Save(input);
        if (errors is not null)
            return TypedResults.ValidationProblem(errors);

        if (changed.Count > 0)
            loggers.CreateLogger(typeof(InstallationEndpoints)).LogWarning("Installation page: {Keys} saved in {File}", string.Join(", ", changed), settings.FilePath);
        // The libraries' services read the folder and ffmpeg once: start them again over the new values.
        if (changed.Any(key => key != InstallationSettings.UrlsKey))
            await libraries.RestartAsync(cancellationToken);

        var view = await settings.DescribeAsync(cancellationToken);
        return TypedResults.Ok(new InstallationSaved(view, view.RestartRequired));
    }

    private static Results<Ok<ServerFolders>, ValidationProblem, UnauthorizedHttpResult> Folders(
        HttpContext context, InstallationAccess access, IConfiguration configuration, string? path = null)
    {
        if (!access.IsGranted(context))
            return TypedResults.Unauthorized();

        if (string.IsNullOrWhiteSpace(path))
            return TypedResults.Ok(new ServerFolders(null, null, Drives(), Places(configuration), false));

        var folder = LibraryPaths.NormalizeRoot(path);
        if (!Directory.Exists(folder))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["path"] = ["This folder does not exist on the server."] });

        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        List<ServerFolder> folders;
        try
        {
            folders = Directory.EnumerateDirectories(folder, "*", options)
                .Where(dir => !Path.GetFileName(dir).StartsWith('.'))
                .Take(MaxFolders + 1)
                .Select(dir => new ServerFolder(Path.GetFileName(dir), dir))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["path"] = ["SFE cannot read this folder."] });
        }

        var truncated = folders.Count > MaxFolders;
        folders = folders.Take(MaxFolders).OrderBy(f => f.Name, NaturalComparer.Instance).ToList();
        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(folder))?.FullName;
        return TypedResults.Ok(new ServerFolders(Path.TrimEndingDirectorySeparator(folder) is { Length: > 0 } trimmed && !trimmed.EndsWith(':') ? trimmed : folder,
            parent, folders, [], truncated));
    }

    private static List<ServerFolder> Drives()
    {
        if (!OperatingSystem.IsWindows())
            return [new ServerFolder("/", "/")];
        return DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .Select(drive => new ServerFolder(Label(drive), drive.RootDirectory.FullName))
            .ToList();

        static string Label(DriveInfo drive)
        {
            var name = drive.Name.TrimEnd('\\');
            return drive.VolumeLabel is { Length: > 0 } label ? $"{label} ({name})" : name;
        }
    }

    /// <summary>The home folder, the current library folder and the usual media folders that exist.</summary>
    private static List<ServerFolder> Places(IConfiguration configuration)
    {
        var places = new List<ServerFolder>();
        Add("Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (configuration[InstallationSettings.RootKey] is { Length: > 0 } root)
            Add("Library folder now", Path.TrimEndingDirectorySeparator(LibraryPaths.NormalizeRoot(root)));
        Add("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Add("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        Add("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        return places;

        void Add(string name, string folder)
        {
            if (folder.Length > 0 && Directory.Exists(folder) && !places.Any(p => string.Equals(p.Path, folder, LibraryPaths.PathComparison)))
                places.Add(new ServerFolder(name, folder));
        }
    }
}
