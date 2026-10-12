using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Settings;

namespace Sfe.Api.Endpoints;

/// <param name="Keys">The settings this level locks for the levels below (its other lockable settings are unlocked).</param>
public sealed record SettingLocks(IReadOnlyList<string>? Keys);

public static class SettingsEndpoints
{
    /// <summary>Every right that changes some setting: the routes need one of them, each setting its own.</summary>
    private static readonly string[] AnySettingsRight =
        [Rights.ServerSettings, .. RightCatalog.All.Where(r => r.Id.StartsWith("settings.", StringComparison.Ordinal)).Select(r => r.Id)];

    public static RouteGroupBuilder MapSettingsEndpoints(this RouteGroupBuilder api)
    {
        var settings = api.MapGroup("/settings").WithTags("Settings").RequireRight(AnySettingsRight);
        const string LevelDescription = """
            `level` is `server` (the server settings page, over the configuration files: server administrators) or `tenant`
            (the library's settings, over the server's). Without it: `server` for server administrators, `tenant` otherwise.
            The library root, the database, ffmpeg and the system commands are only set in the configuration files.
            """;

        settings.MapGet("/", Get)
            .WithName("GetSettings")
            .WithSummary("Settings of a level, with the values in effect and, for each, where it comes from and whether it can be changed.")
            .WithDescription(LevelDescription);

        settings.MapPut("/", Save)
            .WithName("SaveSettings")
            .WithSummary("Saves the settings of a level; they apply right away.")
            .WithDescription(LevelDescription + """

                Only the settings the level may change are kept, when they differ from what it inherits. Changing one it may
                not change (locked above, outside its scope, or without the section's right) is a validation error.
                """);

        settings.MapPut("/locks", SaveLocks)
            .WithName("SaveSettingLocks")
            .WithSummary("Locks settings at a level: the levels below (libraries, users) cannot change them.")
            .WithDescription(LevelDescription);

        settings.MapDelete("/", Reset)
            .WithName("ResetSettings")
            .WithSummary("Forgets a level's values: the levels above apply again.")
            .WithDescription(LevelDescription);

        var preferences = api.MapGroup("/me/preferences").WithTags("Settings").RequireRight(Rights.OwnPreferences);

        preferences.MapGet("/", GetPreferences)
            .WithName("GetPreferences")
            .WithSummary("The signed-in user's own preferences (theme, page sizes, player...) over the library's settings.");

        preferences.MapPut("/", SavePreferences)
            .WithName("SavePreferences")
            .WithSummary("Saves the signed-in user's own preferences; settings locked by the library or the server cannot change.");

        preferences.MapDelete("/", ResetPreferences)
            .WithName("ResetPreferences")
            .WithSummary("Forgets the signed-in user's preferences: the library's settings apply again.");

        return api;
    }

    /// <summary>The level asked for and who may change what there; null when the caller may not use that level.</summary>
    private static (SettingLevel Level, Func<SettingDefinition, bool>? CanChange)? Resolve(AccessState state, string? level)
    {
        var server = state.Has(Rights.ServerSettings);
        var chosen = level?.ToLowerInvariant() switch
        {
            null or "" => server ? SettingLevel.Server : SettingLevel.Tenant,
            "server" => SettingLevel.Server,
            "tenant" or "library" => SettingLevel.Tenant,
            _ => (SettingLevel?)null,
        };
        if (chosen is null || chosen == SettingLevel.Server && !server)
            return null;
        return chosen == SettingLevel.Server ? (SettingLevel.Server, null) : (SettingLevel.Tenant, definition => state.Has(definition.Right));
    }

    private static async Task<Results<Ok<SettingsView>, ForbidHttpResult>> Get(
        SettingsService service, CurrentAccess current, CancellationToken cancellationToken, string? level = null)
    {
        var state = await current.GetAsync();
        if (Resolve(state, level) is not var (chosen, canChange))
            return TypedResults.Forbid();
        return TypedResults.Ok(await service.GetAsync(chosen, state.Tenant?.Id, null, canChange, cancellationToken));
    }

    private static async Task<Results<Ok<SettingsView>, ValidationProblem, ForbidHttpResult>> Save(
        SettingsService service, CurrentAccess current, EditableSettings values, CancellationToken cancellationToken, string? level = null)
    {
        var state = await current.GetAsync();
        if (Resolve(state, level) is not var (chosen, canChange))
            return TypedResults.Forbid();
        var (view, errors) = await service.SaveAsync(chosen, state.Tenant?.Id, null, values, canChange, cancellationToken);
        return view is null ? TypedResults.ValidationProblem(errors!) : TypedResults.Ok(view);
    }

    private static async Task<Results<Ok<SettingsView>, ValidationProblem, ForbidHttpResult>> SaveLocks(
        SettingsService service, CurrentAccess current, SettingLocks locks, CancellationToken cancellationToken, string? level = null)
    {
        var state = await current.GetAsync();
        if (Resolve(state, level) is not var (chosen, canChange))
            return TypedResults.Forbid();
        var (view, errors) = await service.SaveLocksAsync(chosen, state.Tenant?.Id, locks.Keys ?? [], canChange, cancellationToken);
        return view is null ? TypedResults.ValidationProblem(errors!) : TypedResults.Ok(view);
    }

    private static async Task<Results<Ok<SettingsView>, ForbidHttpResult>> Reset(
        SettingsService service, CurrentAccess current, CancellationToken cancellationToken, string? level = null)
    {
        var state = await current.GetAsync();
        if (Resolve(state, level) is not var (chosen, canChange))
            return TypedResults.Forbid();
        return TypedResults.Ok(await service.ResetAsync(chosen, state.Tenant?.Id, null, canChange, cancellationToken));
    }

    private static async Task<Ok<SettingsView>> GetPreferences(SettingsService service, CurrentAccess current, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        return TypedResults.Ok(await service.GetAsync(SettingLevel.User, state.Tenant?.Id, state.User!.Id, null, cancellationToken));
    }

    private static async Task<Results<Ok<SettingsView>, ValidationProblem>> SavePreferences(
        SettingsService service, CurrentAccess current, EditableSettings values, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        var (view, errors) = await service.SaveAsync(SettingLevel.User, state.Tenant?.Id, state.User!.Id, values, null, cancellationToken);
        return view is null ? TypedResults.ValidationProblem(errors!) : TypedResults.Ok(view);
    }

    private static async Task<Ok<SettingsView>> ResetPreferences(SettingsService service, CurrentAccess current, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        return TypedResults.Ok(await service.ResetAsync(SettingLevel.User, state.Tenant?.Id, state.User!.Id, null, cancellationToken));
    }
}
