using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Options;

namespace Sfe.Api.Endpoints;

/// <param name="DisabledByServer">Rights the server switches off for everyone (<c>Sfe:Rights:Disabled</c>).</param>
/// <param name="DisabledInTenant">Rights switched off in the space asked about by a server administrator.</param>
public sealed record RightsCatalogInfo(IReadOnlyList<RightDefinition> Rights, IReadOnlyList<string> DisabledByServer, IReadOnlyList<string> DisabledInTenant);

/// <param name="AnonymousGuestsAllowed">The server lets spaces open to visitors without an account (<c>Sfe:Security:AnonymousGuests</c>).</param>
public sealed record TenantAccessInfo(int Id, string Name, bool GuestAccess, bool AnonymousGuestsAllowed, IReadOnlyList<string> DisabledRights);

public sealed record GuestAccessChange(bool GuestAccess);

/// <summary>
/// Accounts, groups and guest access. A space's administrators (<see cref="Rights.ManageMembers"/>) manage the
/// request's space; server administrators may name any space with <c>tenant</c>, so that they manage every space from
/// wherever they are.
/// </summary>
public static class AccessEndpoints
{
    private const string TenantParameter = "`tenant`: the id of another space than the request's one (server administrators only).";

    public static RouteGroupBuilder MapAccessEndpoints(this RouteGroupBuilder api)
    {
        var access = api.MapGroup("/access").WithTags("Access");

        access.MapGet("/rights", GetRights)
            .RequireRight(Rights.ManageMembers, Rights.ManageUsers, Rights.ManageTenants)
            .WithName("GetRights")
            .WithSummary("Every right, with those the server and the space switch off.")
            .WithDescription(TenantParameter);

        access.MapGet("/users", GetUsers)
            .RequireRight(Rights.ManageMembers, Rights.ManageUsers)
            .WithName("GetUsers")
            .WithSummary("Accounts: every one for server administrators (`all=true`), else those of the space.")
            .WithDescription(TenantParameter);

        access.MapPost("/users", CreateUser)
            .RequireRight(Rights.ManageMembers, Rights.ManageUsers)
            .WithName("CreateUser")
            .WithSummary("Creates a local account.")
            .WithDescription("A space's administrator can only put it in that space's groups, and cannot make server administrators.");

        access.MapPut("/users/{id:int}", UpdateUser)
            .RequireRight(Rights.ManageMembers, Rights.ManageUsers)
            .WithName("UpdateUser")
            .WithSummary("Changes an account (a new password or status ends its sessions).")
            .WithDescription("""
                Server administrators replace every group of the account (`groupIds`: those of all its spaces). A space's
                administrator changes only the account's groups in that space, unless the account belongs to that space
                alone and is no server administrator.
                """);

        access.MapDelete("/users/{id:int}", DeleteUser)
            .RequireRight(Rights.ManageMembers, Rights.ManageUsers)
            .WithName("DeleteUser")
            .WithSummary("Deletes an account, or (for a space's administrator) takes it out of the space.");

        access.MapGet("/groups", GetGroups)
            .RequireRight(Rights.ManageMembers, Rights.ManageUsers)
            .WithName("GetGroups")
            .WithSummary("The groups of the space, with their rights and members; those of every space with `all=true` (server administrators).")
            .WithDescription(TenantParameter);

        access.MapPost("/groups", CreateGroup)
            .RequireRight(Rights.ManageMembers)
            .WithName("CreateGroup")
            .WithSummary("Creates a group in the space.")
            .WithDescription(TenantParameter);

        access.MapPut("/groups/{id:int}", UpdateGroup)
            .RequireRight(Rights.ManageMembers)
            .WithName("UpdateGroup")
            .WithSummary("Changes a group's name, directory group, rights and members.")
            .WithDescription($"""
                The Guests group cannot get a right guests may never have. Only server administrators add accounts that are
                in none of the space's groups yet. {TenantParameter}
                """);

        access.MapDelete("/groups/{id:int}", DeleteGroup)
            .RequireRight(Rights.ManageMembers)
            .WithName("DeleteGroup")
            .WithSummary("Deletes a group (not a built-in one).")
            .WithDescription(TenantParameter);

        access.MapGet("/tenant", GetTenantAccess)
            .RequireRight(Rights.ManageMembers)
            .WithName("GetTenantAccess")
            .WithSummary("Guest access of the space and the rights switched off in it.");

        access.MapPut("/tenant/guest-access", SetGuestAccess)
            .RequireRight(Rights.ManageMembers)
            .WithName("SetGuestAccess")
            .WithSummary("Opens the space to visitors without an account (with its Guests group's rights), or closes it.");

        return api;
    }

    private static async Task<Results<Ok<RightsCatalogInfo>, NotFound>> GetRights(
        CurrentAccess current, AccessService accounts, IOptionsMonitor<RightOptions> rights, CancellationToken cancellationToken, int? tenant = null)
    {
        var state = await current.GetAsync();
        if (SpaceOf(state, tenant) is not { } space)
            return TypedResults.NotFound();
        var disabledHere = space == state.TenantId ? state.Tenant?.DisabledRights ?? [] : (await accounts.GetTenantAsync(space, cancellationToken))?.DisabledRights;
        if (disabledHere is null)
            return TypedResults.NotFound();

        var disabled = rights.CurrentValue.Disabled;
        return TypedResults.Ok(new RightsCatalogInfo(
            RightCatalog.All,
            RightCatalog.All.Where(r => RightCatalog.MatchesAny(disabled, r.Id)).Select(r => r.Id).ToList(),
            disabledHere));
    }

    private static async Task<Results<Ok<IReadOnlyList<UserInfo>>, NotFound>> GetUsers(
        AccessService accounts, CurrentAccess current, CancellationToken cancellationToken, bool all = false, int? tenant = null)
    {
        var state = await current.GetAsync();
        if (all && state.Has(Rights.ManageUsers))
            return TypedResults.Ok(await accounts.GetUsersAsync(null, cancellationToken));
        return SpaceOf(state, tenant) is { } space
            ? TypedResults.Ok(await accounts.GetUsersAsync(space, cancellationToken))
            : TypedResults.NotFound();
    }

    private static async Task<Results<Created<UserInfo>, ValidationProblem, ProblemHttpResult>> CreateUser(
        AccessService accounts, CurrentAccess current, UserInput input, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        var serverWide = state.Has(Rights.ManageUsers);
        if (!serverWide && input.GroupIds is not { Count: > 0 })
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["groupIds"] = ["Put the account in at least one group of the space."] });
        var result = await accounts.CreateUserAsync(serverWide ? input : input with { IsServerAdmin = false }, serverWide ? null : state.TenantId, cancellationToken);
        return result.Status switch
        {
            AccessStatus.Ok => TypedResults.Created($"/api/access/users/{result.Value!.Id}", result.Value),
            AccessStatus.Invalid => TypedResults.ValidationProblem(result.Errors!),
            _ => TypedResults.Problem(result.Problem, statusCode: StatusCodes.Status409Conflict),
        };
    }

    private static async Task<Results<Ok<UserInfo>, ValidationProblem, NotFound, ProblemHttpResult>> UpdateUser(
        AccessService accounts, CurrentAccess current, int id, UserInput input, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (await accounts.GetUserAsync(id, cancellationToken) is not { } target)
            return TypedResults.NotFound();

        AccessResult<UserInfo> result;
        if (state.Has(Rights.ManageUsers))
            result = await accounts.UpdateUserAsync(id, input, null, cancellationToken);
        else if (!IsInTenant(target, state.TenantId))
            return TypedResults.NotFound();
        else if (OwnedBy(target, state.TenantId))
            result = await accounts.UpdateUserAsync(id, input, state.TenantId, cancellationToken);
        else
        {
            // Shared with other spaces (or a server administrator): only its groups here are this space's business.
            var kept = new UserInput(target.UserName, target.DisplayName, null, target.Kind, target.IsServerAdmin, target.Disabled, input.GroupIds);
            result = await accounts.UpdateUserAsync(id, kept, state.TenantId, cancellationToken);
        }

        return result.Status switch
        {
            AccessStatus.Ok => TypedResults.Ok(result.Value!),
            AccessStatus.Invalid => TypedResults.ValidationProblem(result.Errors!),
            AccessStatus.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Problem(result.Problem, statusCode: StatusCodes.Status409Conflict),
        };
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteUser(
        AccessService accounts, CurrentAccess current, int id, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (await accounts.GetUserAsync(id, cancellationToken) is not { } target)
            return TypedResults.NotFound();
        if (state.User?.Id == id)
            return TypedResults.Problem("You cannot delete your own account.", statusCode: StatusCodes.Status409Conflict);

        if (!state.Has(Rights.ManageUsers))
        {
            if (!IsInTenant(target, state.TenantId))
                return TypedResults.NotFound();
            if (!OwnedBy(target, state.TenantId))
            {
                // Out of this space only: its other spaces keep it.
                var left = new UserInput(target.UserName, target.DisplayName, null, target.Kind, target.IsServerAdmin, target.Disabled, []);
                var removed = await accounts.UpdateUserAsync(id, left, state.TenantId, cancellationToken);
                return removed.Status == AccessStatus.Ok ? TypedResults.NoContent() : TypedResults.Problem(removed.Problem, statusCode: StatusCodes.Status409Conflict);
            }
        }

        var result = await accounts.DeleteUserAsync(id, cancellationToken);
        return result.Status switch
        {
            AccessStatus.Ok => TypedResults.NoContent(),
            AccessStatus.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Problem(result.Problem, statusCode: StatusCodes.Status409Conflict),
        };
    }

    private static async Task<Results<Ok<IReadOnlyList<GroupInfo>>, NotFound>> GetGroups(
        AccessService accounts, CurrentAccess current, CancellationToken cancellationToken, bool all = false, int? tenant = null)
    {
        var state = await current.GetAsync();
        // The account editor of server administrators offers the groups of every space.
        if (all && state.Has(Rights.ManageUsers))
            return TypedResults.Ok(await accounts.GetGroupsAsync(null, cancellationToken));
        return SpaceOf(state, tenant) is { } space && (space != state.TenantId || state.Has(Rights.ManageMembers))
            ? TypedResults.Ok(await accounts.GetGroupsAsync(space, cancellationToken))
            : TypedResults.NotFound();
    }

    private static async Task<Results<Created<GroupInfo>, ValidationProblem, NotFound, ProblemHttpResult>> CreateGroup(
        AccessService accounts, CurrentAccess current, GroupInput input, CancellationToken cancellationToken, int? tenant = null)
    {
        var state = await current.GetAsync();
        return SpaceOf(state, tenant) is not { } space
            ? TypedResults.NotFound()
            : await accounts.CreateGroupAsync(space, input, MembersOfSpaceOnly(state), cancellationToken) switch
            {
                { Status: AccessStatus.Ok } ok => TypedResults.Created($"/api/access/groups/{ok.Value!.Id}", ok.Value),
                { Status: AccessStatus.Invalid } invalid => TypedResults.ValidationProblem(invalid.Errors!),
                { Status: AccessStatus.NotFound } => TypedResults.NotFound(),
                var other => TypedResults.Problem(other.Problem, statusCode: StatusCodes.Status409Conflict),
            };
    }

    private static async Task<Results<Ok<GroupInfo>, ValidationProblem, NotFound, ProblemHttpResult>> UpdateGroup(
        AccessService accounts, CurrentAccess current, int id, GroupInput input, CancellationToken cancellationToken, int? tenant = null)
    {
        var state = await current.GetAsync();
        return SpaceOf(state, tenant) is not { } space
            ? TypedResults.NotFound()
            : await accounts.UpdateGroupAsync(space, id, input, MembersOfSpaceOnly(state), cancellationToken) switch
            {
                { Status: AccessStatus.Ok } ok => TypedResults.Ok(ok.Value!),
                { Status: AccessStatus.Invalid } invalid => TypedResults.ValidationProblem(invalid.Errors!),
                { Status: AccessStatus.NotFound } => TypedResults.NotFound(),
                var other => TypedResults.Problem(other.Problem, statusCode: StatusCodes.Status409Conflict),
            };
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteGroup(
        AccessService accounts, CurrentAccess current, int id, CancellationToken cancellationToken, int? tenant = null) =>
        SpaceOf(await current.GetAsync(), tenant) is not { } space
            ? TypedResults.NotFound()
            : await accounts.DeleteGroupAsync(space, id, cancellationToken) switch
            {
                { Status: AccessStatus.Ok } => TypedResults.NoContent(),
                { Status: AccessStatus.NotFound } => TypedResults.NotFound(),
                var other => TypedResults.Problem(other.Problem, statusCode: StatusCodes.Status409Conflict),
            };

    private static async Task<Results<Ok<TenantAccessInfo>, NotFound>> GetTenantAccess(CurrentAccess current, IOptionsMonitor<SecurityOptions> security)
    {
        var state = await current.GetAsync();
        return state.Tenant is { } tenant
            ? TypedResults.Ok(new TenantAccessInfo(tenant.Id, tenant.Name, tenant.GuestAccess, security.CurrentValue.AnonymousGuests, tenant.DisabledRights))
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<TenantAccessInfo>, NotFound>> SetGuestAccess(
        AccessService accounts, CurrentAccess current, IOptionsMonitor<SecurityOptions> security, GuestAccessChange change, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        return await accounts.SetGuestAccessAsync(state.TenantId, change.GuestAccess, cancellationToken) is { } tenant
            ? TypedResults.Ok(new TenantAccessInfo(tenant.Id, tenant.Name, tenant.GuestAccess, security.CurrentValue.AnonymousGuests, tenant.DisabledRights))
            : TypedResults.NotFound();
    }

    /// <summary>
    /// The space a request is about: the request's own without <paramref name="tenant"/>, any other for server
    /// administrators (<see cref="Rights.ManageTenants"/>); null when the caller may not manage the one named.
    /// </summary>
    private static int? SpaceOf(AccessState state, int? tenant) =>
        tenant is not { } id || id == state.TenantId ? state.TenantId : state.Has(Rights.ManageTenants) ? id : null;

    /// <summary>
    /// Only those who manage every account (<see cref="Rights.ManageUsers"/>) put accounts of other spaces, or of none,
    /// in a space's groups, as with <see cref="UpdateUser"/>.
    /// </summary>
    private static bool MembersOfSpaceOnly(AccessState state) => !state.Has(Rights.ManageUsers);

    private static bool IsInTenant(UserInfo user, int tenantId) => user.Groups.Any(g => g.TenantId == tenantId);

    /// <summary>The account belongs to this space alone and is no server administrator: its administrators may change it all.</summary>
    private static bool OwnedBy(UserInfo user, int tenantId) => !user.IsServerAdmin && user.Groups.All(g => g.TenantId == tenantId);
}
