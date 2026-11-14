using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Editing;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Settings;
using Sfe.Core.Tagging;

namespace Sfe.Api.Endpoints;

public static class ConfigEndpoints
{
    public static RouteGroupBuilder MapConfigEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/config", GetConfig)
            .RequireRight(Rights.Browse)
            .WithName("GetConfig")
            .WithTags("Config")
            .WithSummary("Settings the web client needs, with the session's own preferences and rights.");

        return api;
    }

    private static async Task<Ok<ClientConfig>> GetConfig(
        [AsParameters] ConfigServices services,
        CancellationToken cancellationToken
    )
    {
        var (
            paths,
            thumbnails,
            trickplay,
            streamer,
            options,
            current,
            preferences,
            accounts,
            rights,
            configuration,
            _,
            _
        ) = services;
        var state = await current.GetAsync();
        var server = options.CurrentValue;
        // The library's settings with the user's own preferences over them.
        var settings = await preferences.GetEffectiveAsync(state.Tenant?.Id, state.User?.Id, cancellationToken);
        var tenants = await accounts.GetCachedTenantsAsync(cancellationToken);
        var reachable = state.User is { } account ? await rights.GetTenantIdsAsync(account.Id, cancellationToken) : [];
        return TypedResults.Ok(
            new ClientConfig(
                paths.RootName,
                DataServerUrl.Normalize(server.DataServer),
                settings.Browse.DefaultPageSize,
                settings.Search.DefaultPageSize,
                new ClientFeatures(thumbnails.IsAvailable, trickplay.IsAvailable),
                TagColors.All,
                TagService.MaxFilesPerChange,
                server.Thumbnails.LargeImageKilobytes * 1024L,
                settings.Player.SeekSeconds,
                settings.Player.Loop,
                settings.Trickplay.HoverPreview,
                Math.Clamp(settings.Browse.FolderPictures, 0, LibraryBrowser.MaxFolderPictures),
                server.Search.Explain,
                Math.Clamp(settings.Search.HistorySize, 0, SettingsService.MaxSearchHistory),
                settings.Thumbnails.SingleViewOriginals,
                settings.Thumbnails.AnimatedHover,
                settings.Browse.ShowResolution,
                settings.Browse.ShowDuration,
                settings.Browse.ResolutionMark
                    ? new ResolutionThresholds(
                        Math.Clamp(server.Browse.ImageHighResolutionPixels, 1, SettingsService.MaxHighResolutionPixels),
                        Math.Clamp(server.Browse.VideoHighResolutionPixels, 1, SettingsService.MaxHighResolutionPixels)
                    )
                    : null,
                server.Browse.CopyPaths && state.Has(Rights.CopyPaths) ? paths.Root : null,
                server.Player.ExternalUrl is { } url
                && PlayerOptions.IsAllowedExternalUrl(url)
                && state.Has(Rights.ExternalPlayer)
                    ? new ExternalPlayer(
                        string.IsNullOrWhiteSpace(server.Player.ExternalName)
                            ? "external player"
                            : server.Player.ExternalName,
                        url
                    )
                    : null,
                settings.Appearance.Theme,
                AppVersion.Version,
                AppVersion.Commit,
                state.Access.Rights.Order(StringComparer.Ordinal).ToList(),
                state.User is { } user
                    ? new SessionAccount(
                        user.Id,
                        user.UserName,
                        user.DisplayName,
                        user.Kind,
                        user.IsServerAdmin,
                        user.IsLocal
                    )
                    : null,
                state.Tenant is { } tenant ? new SessionTenant(tenant.Id, tenant.Slug, tenant.Name) : null,
                tenants
                    .Where(t => reachable.Contains(t.Id))
                    .Select(t => new SessionTenant(t.Id, t.Slug, t.Name))
                    .ToList(),
                state.Has(Rights.EditImages)
                    ? new EditorConfig(
                        LibraryPaths.NormalizeRelative(server.Editor.OutputFolder?.Trim()) ?? string.Empty,
                        Math.Clamp(server.Editor.Quality, EditedImages.MinQuality, 100)
                    )
                    : null,
                streamer.IsAvailable && state.Has(Rights.Stream) && state.Has(Rights.ViewFiles)
                    ? new StreamingConfig(streamer.Mode == StreamingMode.Always)
                    : null,
                state.Has(Rights.DeleteFiles)
                || state.Has(Rights.DeletePermanently)
                || state.Has(Rights.UploadFiles)
                || state.Has(Rights.FileSettings)
                    ? new FilesConfig(
                        settings.Files.DeletePermanently,
                        Math.Max(0, settings.Files.Trash.RetentionDays),
                        Math.Max(0, settings.Files.Uploads.MaxFileSizeMegabytes),
                        Math.Max(1, settings.Files.Uploads.KeepUnfinishedHours)
                    )
                    : null,
                settings.Reading,
                settings.FileTypes.Text,
                ServerModes.InEffect(configuration),
                settings.Player.Flash && state.Has(Rights.ViewFiles),
                state.Has(Rights.ViewFiles)
                    ? new ArchivesConfig(Math.Max(0, server.Files.Archives.MaxCompressedTarMegabytes))
                    : null,
                ConnectionsOf(state, server.Connections, services.Connections),
                TokensOf(state, services.Tokens)
            )
        );
    }

    /// <summary>What the session may know about connections: the levels and the kinds one can be made of.</summary>
    private static ConnectionsConfig? ConnectionsOf(
        AccessState state,
        ConnectionOptions options,
        ConnectionProviders providers
    ) =>
        options.Enabled && ConnectionRightIds.Any(state.Has)
            ? new ConnectionsConfig(
                options.Levels,
                [
                    .. providers
                        .Offered(options)
                        .Select(p => new ConnectionKind(
                            p.Kind,
                            p.Name,
                            p.Icon,
                            p.SignInMethods(options),
                            p.Capabilities
                        )),
                ]
            )
            : null;

    /// <summary>Whether tokens work and their longest life, for a session that may have or manage them.</summary>
    private static AccessTokensConfig? TokensOf(AccessState state, AccessTokens tokens) =>
        state.Has(Rights.OwnTokens) || state.Has(Rights.ManageTokens)
            ? new AccessTokensConfig(tokens.Options.Enabled, tokens.Options.MaxLifetimeDays)
            : null;

    private static readonly string[] ConnectionRightIds =
    [
        Rights.UseConnections,
        Rights.OwnConnections,
        Rights.LibraryConnections,
        Rights.ServerConnections,
    ];
}
