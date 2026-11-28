using Microsoft.AspNetCore.Http.HttpResults;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Api.Tenancy;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Logs;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

using SetResult = Results<Ok<IReadOnlyList<FolderRuleView>>, NoContent, ValidationProblem, NotFound, ProblemHttpResult>;

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
        {FolderRuleService.MaxRules} rules. With `validateOnly=true`, only the checks run (a wizard's step): 204,
        nothing changed. {TenantParameter}
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
        if (await ManagedSpace.FindAsync(current, tenants, tenant, cancellationToken) is not { } space)
            return TypedResults.NotFound();
        return TypedResults.Ok(await ViewsAsync(rules, accounts, space, cancellationToken));
    }

    private static async Task<SetResult> Set(
        [AsParameters] FolderRuleServices services,
        FolderRuleChange change,
        CancellationToken cancellationToken,
        int? tenant = null,
        bool validateOnly = false
    )
    {
        var (rules, accounts, tenants, current, loggers) = services;
        if (await ManagedSpace.FindAsync(current, tenants, tenant, cancellationToken) is not { } space)
            return TypedResults.NotFound();
        var principal = new FolderPrincipalRef(change.GroupId, change.UserId);
        var result = validateOnly
            ? await rules.CheckAsync(space.Id, change.Path, principal, change.Level, cancellationToken)
            : await rules.SetAsync(
                space.Id,
                change.Path,
                principal,
                change.Level,
                space.State.User?.Id,
                cancellationToken
            );
        if (Refusal(result) is { } refused)
            return refused;
        if (validateOnly)
            return TypedResults.NoContent();

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

    /// <summary>The answer to a refused rule change; null when it was not refused.</summary>
    private static SetResult? Refusal(AccessResult<FolderRuleInfo?> result) =>
        result.Status switch
        {
            AccessStatus.Invalid => TypedResults.ValidationProblem(result.Errors!),
            AccessStatus.Conflict => TypedResults.Problem(result.Problem, statusCode: StatusCodes.Status409Conflict),
            AccessStatus.NotFound => TypedResults.NotFound(),
            _ => null,
        };

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
        if (await ManagedSpace.FindAsync(current, tenants, tenant, cancellationToken) is not { } space)
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
        if (await ManagedSpace.FindAsync(current, tenants, tenant, cancellationToken) is not { } space)
            return TypedResults.NotFound();
        if (!space.Paths.TryResolve(path, out var full) || space.Paths.ToRelative(full) is not { } folder)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["path"] = ["Not a folder of the library."] }
            );
        }

        return TypedResults.Ok(
            await new FolderAccessReports(rules, accounts, evaluator, space).BuildAsync(folder, full, cancellationToken)
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
        if (await ManagedSpace.FindAsync(current, tenants, tenant, cancellationToken) is not { } space)
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
