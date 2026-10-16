using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Api.Tenancy;
using Sfe.Core.Access;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <param name="Root">Full path of the space's library folder on the server.</param>
/// <param name="Members">Accounts with a group in the space.</param>
/// <param name="Groups">Groups of the space, the built-in ones included.</param>
public sealed record TenantDetails(int Id, string Slug, string Name, bool GuestAccess, bool IsDefault, bool Disabled, string Root,
    IReadOnlyList<string> DisabledRights, int Members, int Groups);

public sealed record FolderChoice(string Name, string Path);

/// <param name="Path">The folder listed; null for the list of allowed roots.</param>
/// <param name="Parent">The folder above, while it is still inside an allowed root; null at a root.</param>
public sealed record FolderPicker(string? Path, string? Parent, IReadOnlyList<FolderChoice> Folders);

public static class TenantEndpoints
{
    public static RouteGroupBuilder MapTenantEndpoints(this RouteGroupBuilder api)
    {
        var tenants = api.MapGroup("/tenants").WithTags("Spaces").RequireRight(Rights.ManageTenants);

        tenants.MapGet("/", List)
            .WithName("GetTenants")
            .WithSummary("Every space (tenant), with its library folder and its numbers of accounts and groups.");

        tenants.MapGet("/folders", Folders)
            .NotLibraryPaths()
            .WithName("GetTenantFolders")
            .WithSummary("Folders a space's library folder can be chosen in (`Sfe:Tenants:AllowedRoots`, else the configured root).")
            .WithDescription("Without `path`, the allowed roots; with one, its sub-folders (hidden ones left out).");

        tenants.MapPost("/", Create)
            .WithName("CreateTenant")
            .WithSummary("Creates a space on a folder inside the allowed roots, with its own index, tags, settings and groups.")
            .WithDescription("""
                The index of its library goes to `Sfe:Tenants:ConnectionString` (with `{tenant}` for its short name), else next to the first
                space's (SQLite: `tenants/{slug}.db` beside its file; SQL Server: database `{database}_{slug}` on the same
                server, which the server's account must be allowed to create). Its caches go under `{CacheDirectory}/tenants/{slug}`.
                """);

        tenants.MapPut("/{id:int}", Update)
            .WithName("UpdateTenant")
            .WithSummary("Renames a space, opens or closes it to guests, disables it, and sets the rights nobody gets there.");

        tenants.MapDelete("/{id:int}", Delete)
            .WithName("DeleteTenant")
            .WithSummary("Deletes a space's groups and settings; its index database and caches stay on the disk.");

        return api;
    }

    private static async Task<Ok<List<TenantDetails>>> List(AccessService accounts, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var details = new List<TenantDetails>();
        foreach (var tenant in await accounts.GetTenantsAsync(cancellationToken))
            details.Add(await DetailsAsync(accounts, configuration, tenant, cancellationToken));
        return TypedResults.Ok(details);
    }

    private static Results<Ok<FolderPicker>, NotFound> Folders(IConfiguration configuration, string? path = null)
    {
        var roots = AllowedRoots(configuration);
        if (string.IsNullOrWhiteSpace(path))
            return TypedResults.Ok(new FolderPicker(null, null, roots.Where(Directory.Exists).Select(r => new FolderChoice(r, r)).ToList()));

        var folder = Full(path);
        if (folder is null || !roots.Any(root => IsInside(root, folder)) || !Directory.Exists(folder))
            return TypedResults.NotFound();

        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));
        var folders = new DirectoryInfo(folder).EnumerateDirectories()
            .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden) && !d.Name.StartsWith('.'))
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(d => new FolderChoice(d.Name, d.FullName))
            .ToList();
        return TypedResults.Ok(new FolderPicker(folder, parent is not null && roots.Any(root => IsInside(root, parent)) ? parent : null, folders));
    }

    private static async Task<Results<Created<TenantDetails>, ValidationProblem, ProblemHttpResult>> Create(
        AccessService accounts, TenantRegistry registry, IConfiguration configuration, TenantInput input,
        CancellationToken cancellationToken)
    {
        var root = Full(input.Root);
        if (root is null || !Directory.Exists(root))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["root"] = ["Choose an existing folder of the server."] });
        if (!AllowedRoots(configuration).Any(allowed => IsInside(allowed, root)))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["root"] = ["This folder is outside the folders spaces may use (Sfe:Tenants:AllowedRoots)."] });

        var result = await accounts.CreateTenantAsync(input, root, cancellationToken);
        if (result.Status != AccessStatus.Ok)
            return result.Status == AccessStatus.Invalid
                ? TypedResults.ValidationProblem(result.Errors!)
                : TypedResults.Problem(result.Problem, statusCode: StatusCodes.Status409Conflict);

        // Its index database is created now, so that a wrong connection string shows at once.
        try
        {
            await registry.GetAsync(result.Value!.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await accounts.DeleteTenantAsync(result.Value!.Id, cancellationToken);
            return TypedResults.Problem($"The index of the space's library cannot be created: {ex.Message}", statusCode: StatusCodes.Status409Conflict);
        }

        var details = await DetailsAsync(accounts, configuration, result.Value!, cancellationToken);
        return TypedResults.Created($"/api/tenants/{details.Id}", details);
    }

    private static async Task<Results<Ok<TenantDetails>, ValidationProblem, NotFound, ProblemHttpResult>> Update(
        AccessService accounts, TenantRegistry registry, IConfiguration configuration, int id, TenantInput input, CancellationToken cancellationToken)
    {
        var result = await accounts.UpdateTenantAsync(id, input, cancellationToken);
        switch (result.Status)
        {
            case AccessStatus.NotFound:
                return TypedResults.NotFound();
            case AccessStatus.Invalid:
                return TypedResults.ValidationProblem(result.Errors!);
            case AccessStatus.Conflict:
                return TypedResults.Problem(result.Problem, statusCode: StatusCodes.Status409Conflict);
        }

        if (result.Value!.Disabled)
            await registry.StopAsync(id);
        return TypedResults.Ok(await DetailsAsync(accounts, configuration, result.Value, cancellationToken));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Delete(
        AccessService accounts, TenantRegistry registry, int id, CancellationToken cancellationToken)
    {
        var result = await accounts.DeleteTenantAsync(id, cancellationToken);
        if (result.Status == AccessStatus.NotFound)
            return TypedResults.NotFound();
        if (result.Status != AccessStatus.Ok)
            return TypedResults.Problem(result.Problem, statusCode: StatusCodes.Status409Conflict);

        await registry.StopAsync(id);
        return TypedResults.NoContent();
    }

    private static async Task<TenantDetails> DetailsAsync(AccessService accounts, IConfiguration configuration, TenantInfo tenant, CancellationToken cancellationToken) =>
        new(tenant.Id, tenant.Slug, tenant.Name, tenant.GuestAccess, tenant.IsDefault, tenant.Disabled,
            tenant.Root ?? ConfiguredRoot(configuration) ?? string.Empty, tenant.DisabledRights,
            (await accounts.GetUsersAsync(tenant.Id, cancellationToken)).Count, (await accounts.GetGroupsAsync(tenant.Id, cancellationToken)).Count);

    /// <summary><c>Sfe:Tenants:AllowedRoots</c>, else the configured root: full paths.</summary>
    private static List<string> AllowedRoots(IConfiguration configuration)
    {
        var allowed = configuration.GetSection($"{SfeOptions.SectionName}:{nameof(SfeOptions.Tenants)}").Get<TenantOptions>()?.AllowedRoots ?? [];
        return (allowed.Count > 0 ? allowed : [ConfiguredRoot(configuration) ?? string.Empty])
            .Select(Full)
            .OfType<string>()
            .Distinct(LibraryPaths.PathComparer)
            .ToList();
    }

    private static string? ConfiguredRoot(IConfiguration configuration) =>
        Full(configuration[$"{SfeOptions.SectionName}:{nameof(SfeOptions.Root)}"]);

    private static string? Full(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var full = Path.GetFullPath(LibraryPaths.ExpandHome(path.Trim()));
            return Path.IsPathRooted(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary><paramref name="folder"/> is <paramref name="root"/> or below it (by the file system's rules for case).</summary>
    private static bool IsInside(string root, string folder)
    {
        var relative = Path.GetRelativePath(root, folder);
        return relative == "." || !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
