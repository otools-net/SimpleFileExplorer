using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Api.Tenancy;
using Sfe.Core.Access;
using Sfe.Core.Library;
using Sfe.Core.Logs;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <param name="Name">The group's name, or the account's display or user name.</param>
/// <param name="Exists">The folder is still there (false once it was moved, renamed or deleted outside SFE).</param>
public sealed record FolderRuleView(int Id, string Path, int? GroupId, int? UserId, string Name, FolderLevel Level, bool Exists, DateTime Created);

/// <param name="Path">Folder, relative to the library root (empty: the root).</param>
/// <param name="GroupId">The group the rule is for; or <paramref name="UserId"/> for one account.</param>
/// <param name="Level">The level there; null removes the rule (the folder inherits again).</param>
public sealed record FolderRuleChange(string? Path, int? GroupId, int? UserId, FolderLevel? Level);

/// <param name="From">Where the rules are now: a folder that is no longer there.</param>
/// <param name="To">The folder they now apply to.</param>
public sealed record FolderRuleRepoint(string? From, string? To);

/// <summary>A group of the space, or one of its accounts, at a folder.</summary>
/// <param name="Level">
/// A group's level there (from its nearest rule, Write without one); an account's own rule's, null without one (its
/// groups decide).
/// </param>
/// <param name="Explicit">The level comes from a rule on this very folder.</param>
/// <param name="Sees">What it all comes to: it may browse the folder (accounts: with all their groups and rules).</param>
/// <param name="ChangesFiles">It may change the folder's files (a right such as editing pictures, and Write here).</param>
public sealed record FolderAccessEntry(int? GroupId, int? UserId, string Name, FolderLevel? Level, bool Explicit, bool Sees, bool ChangesFiles);

/// <param name="Visitors">Visitors without an account (the Guests group), when the space lets them in; null otherwise.</param>
public sealed record FolderAccessReport(string Path, bool Exists, IReadOnlyList<FolderAccessEntry> Groups, IReadOnlyList<FolderAccessEntry> Accounts, FolderAccessEntry? Visitors);

/// <summary>
/// Folder rules of a space (agents/docs/features/folder-access.md): who may see or change each folder. For those who
/// manage the space's groups (<see cref="Rights.ManageMembers"/>); server administrators name any space with
/// <c>tenant</c>. They see every folder of the space, whatever rules apply to themselves.
/// </summary>
public static class FolderRuleEndpoints
{
    private const string TenantParameter = "`tenant`: the id of another space than the request's one (server administrators only).";

    private const string ManagersSeeAll = "folder rules are managed for the whole space: managers see every folder's rules and names";

    public static RouteGroupBuilder MapFolderRuleEndpoints(this RouteGroupBuilder api)
    {
        var access = api.MapGroup("/access").WithTags("Access");

        access.MapGet("/folder-rules", List)
            .RequireRight(Rights.ManageMembers)
            .WithName("GetFolderRules")
            .WithSummary("The space's folder rules: what a group, or one account, may do in a folder and below it.")
            .WithDescription(TenantParameter);

        access.MapPut("/folder-rules", Set)
            .RequireRight(Rights.ManageMembers)
            .FolderScoped(ManagersSeeAll)
            .WithName("SetFolderRule")
            .WithSummary("Sets the level of a group or an account at a folder (`hidden`, `read`, `write`), or removes its rule (`level: null`); answers every rule.")
            .WithDescription($"""
                The nearest rule above a path decides each group's and account's level there; a group without one has
                `write`. An account rule only adds to what its groups give. A space has at most {FolderRuleService.MaxRules}
                rules. {TenantParameter}
                """);

        access.MapPost("/folder-rules/repoint", Repoint)
            .RequireRight(Rights.ManageMembers)
            .FolderScoped(ManagersSeeAll)
            .WithName("RepointFolderRules")
            .WithSummary("Moves the rules of a folder that is no longer there (and of its sub-folders) to another folder.")
            .WithDescription($"""
                For a folder moved or renamed outside SFE. 409 while `from` still exists, or when `to` (or a folder below it)
                has rules already. {TenantParameter}
                """);

        access.MapGet("/folder-access", Report)
            .RequireRight(Rights.ManageMembers)
            .FolderScoped(ManagersSeeAll)
            .WithName("GetFolderAccess")
            .WithSummary("Who may see and change a folder: every group and account of the space, and visitors.")
            .WithDescription(TenantParameter);

        access.MapGet("/folders", Folders)
            .RequireRight(Rights.ManageMembers)
            .FolderScoped(ManagersSeeAll)
            .WithName("GetSpaceFolders")
            .WithSummary("The sub-folders of a folder of the space, for choosing where a rule goes (every folder, whatever rules apply to the asker).")
            .WithDescription(TenantParameter);

        return api;
    }

    private static async Task<Results<Ok<IReadOnlyList<FolderRuleView>>, NotFound>> List(
        FolderRuleService rules, AccessService accounts, TenantRegistry tenants, CurrentAccess current, CancellationToken cancellationToken, int? tenant = null)
    {
        if (await SpaceAsync(current, tenants, tenant, cancellationToken) is not { } space)
            return TypedResults.NotFound();
        return TypedResults.Ok(await ViewsAsync(rules, accounts, space, cancellationToken));
    }

    private static async Task<Results<Ok<IReadOnlyList<FolderRuleView>>, ValidationProblem, NotFound, ProblemHttpResult>> Set(
        FolderRuleService rules, AccessService accounts, TenantRegistry tenants, CurrentAccess current, ILoggerFactory loggers,
        FolderRuleChange change, CancellationToken cancellationToken, int? tenant = null)
    {
        if (await SpaceAsync(current, tenants, tenant, cancellationToken) is not { } space)
            return TypedResults.NotFound();
        var principal = new FolderPrincipalRef(change.GroupId, change.UserId);
        var result = await rules.SetAsync(space.Id, change.Path, principal, change.Level, space.State.User?.Id, cancellationToken);
        switch (result.Status)
        {
            case AccessStatus.Invalid:
                return TypedResults.ValidationProblem(result.Errors!);
            case AccessStatus.Conflict:
                return TypedResults.Problem(result.Problem, statusCode: StatusCodes.Status409Conflict);
            case AccessStatus.NotFound:
                return TypedResults.NotFound();
        }

        var logger = loggers.CreateLogger(typeof(FolderRuleEndpoints));
        var who = space.State.User?.UserName;
        var path = LibraryPaths.NormalizeRelative(change.Path) ?? string.Empty;
        // Opening a folder to visitors without an account is what a security review wants to find.
        if (change.GroupId is { } groupId && change.Level is FolderLevel.Read or FolderLevel.Write
            && (await accounts.GetBuiltInGroupAsync(space.Id, Core.Data.BuiltInGroup.Guests, cancellationToken))?.Id == groupId)
            logger.LogSecurityWarning("Folder rule: {User} lets the Guests group {Level} “{Path}”", who, change.Level, path);
        else
            logger.LogInformation("Folder rule: {User} set {Principal} to {Level} at “{Path}”", who, change.GroupId is { } g ? $"group {g}" : $"account {change.UserId}",
                change.Level?.ToString() ?? "inherit", path);
        return TypedResults.Ok(await ViewsAsync(rules, accounts, space, cancellationToken));
    }

    private static async Task<Results<Ok<IReadOnlyList<FolderRuleView>>, ValidationProblem, NotFound, ProblemHttpResult>> Repoint(
        FolderRuleService rules, AccessService accounts, TenantRegistry tenants, CurrentAccess current, ILoggerFactory loggers,
        FolderRuleRepoint change, CancellationToken cancellationToken, int? tenant = null)
    {
        if (await SpaceAsync(current, tenants, tenant, cancellationToken) is not { } space)
            return TypedResults.NotFound();
        if (space.Paths.TryResolve(change.From, out var from) && (Directory.Exists(from) || File.Exists(from)))
            return TypedResults.Problem("The folder is still there: its rules apply to it.", statusCode: StatusCodes.Status409Conflict);
        if (!space.Paths.TryResolve(change.To, out var to) || !Directory.Exists(to))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["to"] = ["Not a folder of the library."] });

        // The move of a folder, without the disk: nothing to undo between the steps.
        var begun = await rules.BeginMoveAsync(space.Id, change.From, change.To, keepAccess: false, space.State.User?.Id, cancellationToken);
        switch (begun.Status)
        {
            case AccessStatus.Invalid:
                return TypedResults.ValidationProblem(begun.Errors!);
            case AccessStatus.Conflict:
                return TypedResults.Problem(begun.Problem, statusCode: StatusCodes.Status409Conflict);
        }
        await using (var move = begun.Value!)
            await move.CompleteAsync(cancellationToken);

        loggers.CreateLogger(typeof(FolderRuleEndpoints)).LogSecurityWarning("Folder rules of “{From}” moved to “{To}” by {User}", change.From, change.To, space.State.User?.UserName);
        return TypedResults.Ok(await ViewsAsync(rules, accounts, space, cancellationToken));
    }

    private static async Task<Results<Ok<FolderAccessReport>, ValidationProblem, NotFound>> Report(
        FolderRuleService rules, AccessService accounts, RightsEvaluator evaluator, TenantRegistry tenants, CurrentAccess current,
        CancellationToken cancellationToken, string? path = null, int? tenant = null)
    {
        if (await SpaceAsync(current, tenants, tenant, cancellationToken) is not { } space)
            return TypedResults.NotFound();
        if (!space.Paths.TryResolve(path, out var full) || space.Paths.ToRelative(full) is not { } folder)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["path"] = ["Not a folder of the library."] });

        var all = await rules.GetAsync(space.Id, cancellationToken);
        var groups = await accounts.GetGroupsAsync(space.Id, cancellationToken);
        var users = await accounts.GetUsersAsync(space.Id, cancellationToken);
        var info = await accounts.GetTenantAsync(space.Id, cancellationToken);

        Dictionary<string, FolderLevel> RulesOf(FolderPrincipalRef principal) =>
            RightsEvaluator.RuleMap(all.Where(r => r.Principal == principal).Select(r => (r.Path, r.Level)));

        bool IsExplicit(FolderPrincipalRef principal) => all.Any(r => r.Principal == principal && LibraryPaths.PathComparer.Equals(r.Path, folder));

        async Task<(bool Sees, bool ChangesFiles)> EffectiveAsync(int? userId)
        {
            var access = (await evaluator.GetAsync(new AccessSubject(space.Id, userId), cancellationToken)).At(folder);
            return (access.Has(Rights.Browse), RightCatalog.All.Any(r => r.ChangesFiles && access.Has(r.Id)));
        }

        var groupEntries = groups.Select(g =>
        {
            var principal = FolderPrincipalRef.Group(g.Id);
            var level = FolderPolicy.LevelAt(RulesOf(principal), folder, true);
            var granted = g.Rights.ToHashSet();
            return new FolderAccessEntry(g.Id, null, g.Name, level, IsExplicit(principal),
                level >= FolderLevel.Read && granted.Contains(Rights.Browse),
                level == FolderLevel.Write && RightCatalog.All.Any(r => r.ChangesFiles && granted.Contains(r.Id)));
        }).ToList();

        var accountEntries = new List<FolderAccessEntry>();
        foreach (var user in users.OrderBy(u => u.DisplayName ?? u.UserName, StringComparer.CurrentCultureIgnoreCase))
        {
            var principal = FolderPrincipalRef.Account(user.Id);
            var (sees, changes) = await EffectiveAsync(user.Id);
            accountEntries.Add(new FolderAccessEntry(null, user.Id, user.DisplayName ?? user.UserName, FolderPolicy.LevelAt(RulesOf(principal), folder, false),
                IsExplicit(principal), sees, changes));
        }

        FolderAccessEntry? visitors = null;
        if (info is { GuestAccess: true } && groups.FirstOrDefault(g => g.BuiltIn == Core.Data.BuiltInGroup.Guests) is { } guests)
        {
            var (sees, changes) = await EffectiveAsync(null);
            visitors = new FolderAccessEntry(guests.Id, null, "Visitors without an account", groupEntries.First(e => e.GroupId == guests.Id).Level, false, sees, changes);
        }

        return TypedResults.Ok(new FolderAccessReport(folder, Directory.Exists(full), groupEntries, accountEntries, visitors));
    }

    /// <summary>Most folders listed: a picker, not a listing (a folder of a hundred thousand sub-folders isn't sent whole).</summary>
    private const int MaxPickerFolders = 1000;

    private static async Task<Results<Ok<FolderPicker>, NotFound>> Folders(
        TenantRegistry tenants, CurrentAccess current, CancellationToken cancellationToken, string? path = null, int? tenant = null)
    {
        if (await SpaceAsync(current, tenants, tenant, cancellationToken) is not { } space)
            return TypedResults.NotFound();
        if (!space.Paths.TryResolve(path, out var full) || !Directory.Exists(full) || space.Paths.ToRelative(full) is not { } folder)
            return TypedResults.NotFound();

        var browser = space.Host.Services.GetRequiredService<LibraryBrowser>();
        List<FolderChoice> folders;
        try
        {
            folders = [.. new DirectoryInfo(full).EnumerateDirectories("*", browser.EnumerationOptions)
                .Select(d => d.Name)
                .Order(NaturalComparer.Instance)
                .Take(MaxPickerFolders)
                .Select(name => new FolderChoice(name, folder.Length == 0 ? name : $"{folder}/{name}"))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            folders = [];
        }
        var parent = folder.Length == 0 ? null : folder.Contains('/') ? folder[..folder.LastIndexOf('/')] : string.Empty;
        return TypedResults.Ok(new FolderPicker(folder, parent, folders));
    }

    /// <summary>The space a request is about, with its library; null when the caller may not manage it (as in <see cref="AccessEndpoints"/>).</summary>
    private static async Task<ManagedSpace?> SpaceAsync(CurrentAccess current, TenantRegistry tenants, int? tenant, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        var id = tenant is not { } asked || asked == state.TenantId ? state.TenantId : state.Has(Rights.ManageTenants) ? asked : (int?)null;
        if (id is not { } spaceId || await tenants.GetAsync(spaceId, cancellationToken) is not { } host)
            return null;
        return new ManagedSpace(spaceId, state, host, host.Services.GetRequiredService<LibraryPaths>());
    }

    private sealed record ManagedSpace(int Id, AccessState State, TenantHost Host, LibraryPaths Paths);

    private static async Task<IReadOnlyList<FolderRuleView>> ViewsAsync(FolderRuleService rules, AccessService accounts, ManagedSpace space, CancellationToken cancellationToken)
    {
        var groups = (await accounts.GetGroupsAsync(space.Id, cancellationToken)).ToDictionary(g => g.Id, g => g.Name);
        var users = (await accounts.GetUsersAsync(space.Id, cancellationToken)).ToDictionary(u => u.Id, u => u.DisplayName ?? u.UserName);
        return [.. (await rules.GetAsync(space.Id, cancellationToken)).Select(r => new FolderRuleView(
            r.Id, r.Path, r.Principal.GroupId, r.Principal.UserId,
            r.Principal.GroupId is { } g ? groups.GetValueOrDefault(g, $"Group {g}") : users.GetValueOrDefault(r.Principal.UserId!.Value, $"Account {r.Principal.UserId}"),
            r.Level,
            space.Paths.TryResolve(r.Path, out var full) && (Directory.Exists(full) || File.Exists(full)),
            r.Created))];
    }
}
