using Microsoft.AspNetCore.Http.HttpResults;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Api.Tenancy;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Logs;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// Folder rules of a space (agents/docs/features/folder-access.md): who may see or change each folder. For those who
/// manage the space's groups (<see cref="Rights.ManageMembers"/>); server administrators name any space with
/// <c>tenant</c>. They see every folder of the space, whatever rules apply to themselves.
/// </summary>
public static class FolderRuleEndpoints
{
    private const string TenantParameter =
        "`tenant`: the id of another space than the request's one (server administrators only).";

    private const string ManagersSeeAll =
        "folder rules are managed for the whole space: managers see every folder's rules and names";

    private static readonly string SetFolderRuleDescription = $"""
        The nearest rule above a path decides each group's and account's level there; a group without one has
        `write`. An account rule only adds to what its groups give. A space has at most
        {FolderRuleService.MaxRules} rules. {TenantParameter}
        """;

    private static readonly string RepointFolderRulesDescription = $"""
        For a folder moved or renamed outside SFE. 409 while `from` still exists, or when `to` (or a folder
        below it) has rules already. {TenantParameter}
        """;

    public static RouteGroupBuilder MapFolderRuleEndpoints(this RouteGroupBuilder api)
    {
        var access = api.MapGroup("/access").WithTags("Access");

        access
            .MapGet("/folder-rules", List)
            .RequireRight(Rights.ManageMembers)
            .WithName("GetFolderRules")
            .WithSummary("The space's folder rules: what a group, or one account, may do in a folder and below it.")
            .WithDescription(TenantParameter);

        access
            .MapPut("/folder-rules", Set)
            .RequireRight(Rights.ManageMembers)
            .FolderScoped(ManagersSeeAll)
            .WithName("SetFolderRule")
            .WithSummary(
                "Sets the level of a group or an account at a folder (`hidden`, `read`, `write`), "
                    + "or removes its rule (`level: null`); answers every rule."
            )
            .WithDescription(SetFolderRuleDescription);

        access
            .MapPost("/folder-rules/repoint", Repoint)
            .RequireRight(Rights.ManageMembers)
            .FolderScoped(ManagersSeeAll)
            .WithName("RepointFolderRules")
            .WithSummary(
                "Moves the rules of a folder that is no longer there (and of its sub-folders) to another folder."
            )
            .WithDescription(RepointFolderRulesDescription);

        access
            .MapGet("/folder-access", Report)
            .RequireRight(Rights.ManageMembers)
            .FolderScoped(ManagersSeeAll)
            .WithName("GetFolderAccess")
            .WithSummary("Who may see and change a folder: every group and account of the space, and visitors.")
            .WithDescription(TenantParameter);

        access
            .MapGet("/folders", Folders)
            .RequireRight(Rights.ManageMembers)
            .FolderScoped(ManagersSeeAll)
            .WithName("GetSpaceFolders")
            .WithSummary(
                "The sub-folders of a folder of the space, for choosing where a rule goes (every folder, "
                    + "whatever rules apply to the asker)."
            )
            .WithDescription(TenantParameter);

        return api;
    }

    private static async Task<Results<Ok<IReadOnlyList<FolderRuleView>>, NotFound>> List(
        [AsParameters] FolderRuleServices services,
        CancellationToken cancellationToken,
        int? tenant = null
    )
    {
        var (rules, accounts, tenants, current, _) = services;
        if (await SpaceAsync(current, tenants, tenant, cancellationToken) is not { } space)
            return TypedResults.NotFound();
        return TypedResults.Ok(await ViewsAsync(rules, accounts, space, cancellationToken));
    }

    private static async Task<
        Results<Ok<IReadOnlyList<FolderRuleView>>, ValidationProblem, NotFound, ProblemHttpResult>
    > Set(
        [AsParameters] FolderRuleServices services,
        FolderRuleChange change,
        CancellationToken cancellationToken,
        int? tenant = null
    )
    {
        var (rules, accounts, tenants, current, loggers) = services;
        if (await SpaceAsync(current, tenants, tenant, cancellationToken) is not { } space)
            return TypedResults.NotFound();
        var principal = new FolderPrincipalRef(change.GroupId, change.UserId);
        var result = await rules.SetAsync(
            space.Id,
            change.Path,
            principal,
            change.Level,
            space.State.User?.Id,
            cancellationToken
        );
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
        if (
            change.GroupId is { } groupId
            && change.Level is FolderLevel.Read or FolderLevel.Write
            && (await accounts.GetBuiltInGroupAsync(space.Id, Core.Data.BuiltInGroup.Guests, cancellationToken))?.Id
                == groupId
        )
        {
            logger.LogSecurityWarning(
                "Folder rule: {User} lets the Guests group {Level} “{Path}”",
                who,
                change.Level,
                path
            );
        }
        else
        {
            logger.LogInformation(
                "Folder rule: {User} set {Principal} to {Level} at “{Path}”",
                who,
                change.GroupId is { } g ? $"group {g}" : $"account {change.UserId}",
                change.Level?.ToString() ?? "inherit",
                path
            );
        }

        return TypedResults.Ok(await ViewsAsync(rules, accounts, space, cancellationToken));
    }

    private static async Task<
        Results<Ok<IReadOnlyList<FolderRuleView>>, ValidationProblem, NotFound, ProblemHttpResult>
    > Repoint(
        [AsParameters] FolderRuleServices services,
        FolderRuleRepoint change,
        CancellationToken cancellationToken,
        int? tenant = null
    )
    {
        var (rules, accounts, tenants, current, loggers) = services;
        if (await SpaceAsync(current, tenants, tenant, cancellationToken) is not { } space)
            return TypedResults.NotFound();
        if (space.Paths.TryResolve(change.From, out var from) && (Directory.Exists(from) || File.Exists(from)))
        {
            return TypedResults.Problem(
                "The folder is still there: its rules apply to it.",
                statusCode: StatusCodes.Status409Conflict
            );
        }

        if (!space.Paths.TryResolve(change.To, out var to) || !Directory.Exists(to))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["to"] = ["Not a folder of the library."] }
            );
        }

        // The move of a folder, without the disk: nothing to undo between the steps.
        var begun = await rules.BeginMoveAsync(
            space.Id,
            change.From,
            change.To,
            keepAccess: false,
            space.State.User?.Id,
            cancellationToken
        );
        switch (begun.Status)
        {
            case AccessStatus.Invalid:
                return TypedResults.ValidationProblem(begun.Errors!);
            case AccessStatus.Conflict:
                return TypedResults.Problem(begun.Problem, statusCode: StatusCodes.Status409Conflict);
        }
        await using (var move = begun.Value!)
            await move.CompleteAsync(cancellationToken);

        loggers
            .CreateLogger(typeof(FolderRuleEndpoints))
            .LogSecurityWarning(
                "Folder rules of “{From}” moved to “{To}” by {User}",
                change.From,
                change.To,
                space.State.User?.UserName
            );
        return TypedResults.Ok(await ViewsAsync(rules, accounts, space, cancellationToken));
    }

    private static async Task<Results<Ok<FolderAccessReport>, ValidationProblem, NotFound>> Report(
        [AsParameters] FolderRuleServices services,
        RightsEvaluator evaluator,
        CancellationToken cancellationToken,
        string? path = null,
        int? tenant = null
    )
    {
        var (rules, accounts, tenants, current, _) = services;
        if (await SpaceAsync(current, tenants, tenant, cancellationToken) is not { } space)
            return TypedResults.NotFound();
        if (!space.Paths.TryResolve(path, out var full) || space.Paths.ToRelative(full) is not { } folder)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["path"] = ["Not a folder of the library."] }
            );
        }

        var all = await rules.GetAsync(space.Id, cancellationToken);
        var groups = await accounts.GetGroupsAsync(space.Id, cancellationToken);
        var users = await accounts.GetUsersAsync(space.Id, cancellationToken);
        var info = await accounts.GetTenantAsync(space.Id, cancellationToken);

        Dictionary<string, FolderLevel> RulesOf(FolderPrincipalRef principal) =>
            RightsEvaluator.RuleMap(all.Where(r => r.Principal == principal).Select(r => (r.Path, r.Level)));

        bool IsExplicit(FolderPrincipalRef principal) =>
            all.Any(r => r.Principal == principal && LibraryPaths.PathComparer.Equals(r.Path, folder));

        async Task<(bool Sees, bool ChangesFiles)> EffectiveAsync(int? userId)
        {
            var access = (await evaluator.GetAsync(new AccessSubject(space.Id, userId), cancellationToken)).At(folder);
            return (access.Has(Rights.Browse), RightCatalog.All.Any(r => r.ChangesFiles && access.Has(r.Id)));
        }

        var groupEntries = groups
            .Select(g =>
            {
                var principal = FolderPrincipalRef.Group(g.Id);
                var level = FolderPolicy.LevelAt(RulesOf(principal), folder, true);
                var granted = g.Rights.ToHashSet();
                return new FolderAccessEntry(
                    g.Id,
                    null,
                    g.Name,
                    level,
                    IsExplicit(principal),
                    level >= FolderLevel.Read && granted.Contains(Rights.Browse),
                    level == FolderLevel.Write && RightCatalog.All.Any(r => r.ChangesFiles && granted.Contains(r.Id))
                );
            })
            .ToList();

        var accountEntries = new List<FolderAccessEntry>();
        foreach (var user in users.OrderBy(u => u.DisplayName ?? u.UserName, StringComparer.CurrentCultureIgnoreCase))
        {
            var principal = FolderPrincipalRef.Account(user.Id);
            var (sees, changes) = await EffectiveAsync(user.Id);
            accountEntries.Add(
                new FolderAccessEntry(
                    null,
                    user.Id,
                    user.DisplayName ?? user.UserName,
                    FolderPolicy.LevelAt(RulesOf(principal), folder, false),
                    IsExplicit(principal),
                    sees,
                    changes
                )
            );
        }

        FolderAccessEntry? visitors = null;
        if (
            info is { GuestAccess: true }
            && groups.FirstOrDefault(g => g.BuiltIn == Core.Data.BuiltInGroup.Guests) is { } guests
        )
        {
            var (sees, changes) = await EffectiveAsync(null);
            visitors = new FolderAccessEntry(
                guests.Id,
                null,
                "Visitors without an account",
                groupEntries.First(e => e.GroupId == guests.Id).Level,
                false,
                sees,
                changes
            );
        }

        return TypedResults.Ok(
            new FolderAccessReport(folder, Directory.Exists(full), groupEntries, accountEntries, visitors)
        );
    }

    /// <summary>
    /// Most folders listed: a picker, not a listing (a folder of a hundred thousand sub-folders isn't sent whole).
    /// </summary>
    private const int MaxPickerFolders = 1000;

    private static async Task<Results<Ok<FolderPicker>, NotFound>> Folders(
        TenantRegistry tenants,
        CurrentAccess current,
        CancellationToken cancellationToken,
        string? path = null,
        int? tenant = null
    )
    {
        if (await SpaceAsync(current, tenants, tenant, cancellationToken) is not { } space)
            return TypedResults.NotFound();
        if (
            !space.Paths.TryResolve(path, out var full)
            || !Directory.Exists(full)
            || space.Paths.ToRelative(full) is not { } folder
        )
        {
            return TypedResults.NotFound();
        }

        var browser = space.Host.Services.GetRequiredService<LibraryBrowser>();
        List<FolderChoice> folders;
        try
        {
            folders =
            [
                .. new DirectoryInfo(full)
                    .EnumerateDirectories("*", browser.EnumerationOptions)
                    .Select(d => d.Name)
                    .Order(NaturalComparer.Instance)
                    .Take(MaxPickerFolders)
                    .Select(name => new FolderChoice(name, folder.Length == 0 ? name : $"{folder}/{name}")),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            folders = [];
        }
        var parent =
            folder.Length == 0 ? null
            : folder.Contains('/') ? folder[..folder.LastIndexOf('/')]
            : string.Empty;
        return TypedResults.Ok(new FolderPicker(folder, parent, folders));
    }

    /// <summary>
    /// The space a request is about, with its library; null when the caller may not manage it (as in
    /// <see cref="AccessEndpoints"/>).
    /// </summary>
    private static async Task<ManagedSpace?> SpaceAsync(
        CurrentAccess current,
        TenantRegistry tenants,
        int? tenant,
        CancellationToken cancellationToken
    )
    {
        var state = await current.GetAsync();
        var id =
            tenant is not { } asked || asked == state.TenantId ? state.TenantId
            : state.Has(Rights.ManageTenants) ? asked
            : (int?)null;
        if (id is not { } spaceId || await tenants.GetAsync(spaceId, cancellationToken) is not { } host)
            return null;
        return new ManagedSpace(spaceId, state, host, host.Services.GetRequiredService<LibraryPaths>());
    }

    private sealed record ManagedSpace(int Id, AccessState State, TenantHost Host, LibraryPaths Paths);

    private static async Task<IReadOnlyList<FolderRuleView>> ViewsAsync(
        FolderRuleService rules,
        AccessService accounts,
        ManagedSpace space,
        CancellationToken cancellationToken
    )
    {
        var groups = (await accounts.GetGroupsAsync(space.Id, cancellationToken)).ToDictionary(g => g.Id, g => g.Name);
        var users = (await accounts.GetUsersAsync(space.Id, cancellationToken)).ToDictionary(
            u => u.Id,
            u => u.DisplayName ?? u.UserName
        );
        return
        [
            .. (await rules.GetAsync(space.Id, cancellationToken)).Select(r => new FolderRuleView(
                r.Id,
                r.Path,
                r.Principal.GroupId,
                r.Principal.UserId,
                r.Principal.GroupId is { } g
                    ? groups.GetValueOrDefault(g, $"Group {g}")
                    : users.GetValueOrDefault(r.Principal.UserId!.Value, $"Account {r.Principal.UserId}"),
                r.Level,
                space.Paths.TryResolve(r.Path, out var full) && (Directory.Exists(full) || File.Exists(full)),
                r.Created
            )),
        ];
    }
}
