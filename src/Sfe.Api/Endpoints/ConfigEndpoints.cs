using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Editing;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Settings;
using Sfe.Core.Streaming;
using Sfe.Core.SystemCommands;
using Sfe.Core.Tagging;
using Sfe.Core.Thumbnails;
using Sfe.Core.Trickplay;

namespace Sfe.Api.Endpoints;

/// <param name="Trickplay">Videos may have trickplay (see <c>/api/trickplay/files/{path}</c>).</param>
public sealed record ClientFeatures(bool Thumbnails, bool Trickplay, bool Shutdown, bool Dismount);

/// <param name="Name">Shown on its button, e.g. "Open in VLC".</param>
/// <param name="Url">
/// Link template: <c>{url}</c> = the file's full address, <c>{encodedUrl}</c> = the same percent-encoded; without
/// either the address is appended.
/// </param>
public sealed record ExternalPlayer(string Name, string Url);

/// <summary>Smallest shorter side, in pixels, of a high-resolution image and of a high-resolution video.</summary>
public sealed record ResolutionThresholds(int Image, int Video);

/// <param name="OutputFolder">Library folder edited pictures are saved in, under their originals' folders; empty = next to the originals.</param>
/// <param name="Quality">Quality (1-100) of the JPEG and WebP pictures the editor encodes.</param>
public sealed record EditorConfig(string OutputFolder, int Quality);

/// <param name="Always">Every video plays converted (<c>Streaming:Mode</c> = <c>Always</c>), not only those the browser can't play.</param>
public sealed record StreamingConfig(bool Always);

/// <param name="DeletePermanently">Every delete skips the trash (<c>Files:DeletePermanently</c>).</param>
/// <param name="TrashRetentionDays">Days in the trash before the purge; 0 = until emptied.</param>
public sealed record FilesConfig(bool DeletePermanently, int TrashRetentionDays);

/// <param name="DataServer">When set, the client links files to this server instead of <c>/api/files/raw</c>: a base the path is appended to, or a template with <c>{url}</c> / <c>{encodedUrl}</c> (<see cref="DataServerUrl"/>).</param>
/// <param name="PageSize">Items the client loads at a time while browsing a folder.</param>
/// <param name="SearchPageSize">Results the client loads at a time while searching.</param>
/// <param name="TagColors">Palette for tag colors, see <see cref="Sfe.Core.Tagging.TagColors"/>.</param>
/// <param name="MaxFilesPerTagChange">Files one tag change may name: the client splits larger selections.</param>
/// <param name="LargeImageSize">Images larger than this (bytes) are previewed through their thumbnail only.</param>
/// <param name="SeekSeconds">Seconds the video player jumps back or forward.</param>
/// <param name="Loop">Videos start over at their end until the player's loop button is switched off.</param>
/// <param name="TrickplayHover">Hovering a video in a listing plays its trickplay over the thumbnail.</param>
/// <param name="FolderPictures">Pictures on a folder card (see <c>/api/browse/covers</c>); 0 = folders without pictures.</param>
/// <param name="SearchExplain">The search box offers to put the query into words.</param>
/// <param name="SearchHistory">Searches each browser remembers and suggests in the search box; 0 = none.</param>
/// <param name="SingleViewOriginals">The "one per row" view shows images themselves, not their thumbnails.</param>
/// <param name="ShowResolution">Listings and the viewer show the resolution of images and videos.</param>
/// <param name="ShowDuration">Listings and the viewer show the duration of videos and audio files.</param>
/// <param name="ResolutionMark">
/// Thresholds of the HR/LR marks on the previews of images and videos; null = no marks (<c>Browse:ResolutionMark</c>).
/// </param>
/// <param name="RootPath">
/// Full path of the library root on the server, ending with its directory separator, for the buttons copying full
/// paths; null when they are off (<c>Browse:CopyPaths</c>), so that the server's layout is not given away.
/// </param>
/// <param name="ExternalPlayer">App that videos and audio can be opened in (<c>Player:ExternalUrl</c>); null = none.</param>
/// <param name="Theme">Theme of the browsers that have not picked one themselves.</param>
/// <param name="Version">Version of the server (<c>Directory.Build.props</c>), e.g. <c>1.2.0</c>.</param>
/// <param name="Commit">Short hash of the commit it was built from, when known.</param>
/// <param name="Rights">What the session may do in the library (<see cref="Sfe.Core.Access.Rights"/>): the client hides the rest.</param>
/// <param name="User">The signed-in account; null for a visitor without one.</param>
/// <param name="Tenant">The library being browsed.</param>
/// <param name="Tenants">The libraries the account can switch to (empty for a visitor without one).</param>
/// <param name="Editor">The picture editor's settings; null without the right to edit pictures.</param>
/// <param name="Streaming">
/// Videos the browser can't play are converted by the server (<c>/api/streaming/play</c>); null when streaming is off,
/// ffmpeg is missing, or without the right <c>files.stream</c>.
/// </param>
public sealed record ClientConfig(string RootName, string? DataServer, int PageSize, int SearchPageSize, ClientFeatures Features, IReadOnlyList<TagColor> TagColors, int MaxFilesPerTagChange, long LargeImageSize, int SeekSeconds, bool Loop, bool TrickplayHover, int FolderPictures, bool SearchExplain, int SearchHistory, bool SingleViewOriginals, bool AnimatedHover, bool ShowResolution, bool ShowDuration, ResolutionThresholds? ResolutionMark, string? RootPath, ExternalPlayer? ExternalPlayer, Theme Theme, string Version, string? Commit,
    IReadOnlyCollection<string> Rights, SessionAccount? User, SessionTenant? Tenant, IReadOnlyList<SessionTenant> Tenants, EditorConfig? Editor, StreamingConfig? Streaming,
    FilesConfig? Files = null);

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
        LibraryPaths paths, ThumbnailService thumbnails, TrickplayService trickplay, VideoStreamer streamer, SystemCommandService system, IOptionsMonitor<SfeOptions> options,
        CurrentAccess current, SettingsService preferences, AccessService accounts, RightsEvaluator rights, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        var server = options.CurrentValue;
        // The library's settings with the user's own preferences over them.
        var settings = await preferences.GetEffectiveAsync(state.Tenant?.Id, state.User?.Id, cancellationToken);
        var tenants = await accounts.GetCachedTenantsAsync(cancellationToken);
        var reachable = state.User is { } account ? await rights.GetTenantIdsAsync(account.Id, cancellationToken) : [];
        return TypedResults.Ok(new ClientConfig(
            paths.RootName,
            DataServerUrl.Normalize(server.DataServer),
            settings.Browse.DefaultPageSize,
            settings.Search.DefaultPageSize,
            new ClientFeatures(thumbnails.IsAvailable, trickplay.IsAvailable, system.CanShutdown && state.Has(Rights.Shutdown), system.CanDismount && state.Has(Rights.Dismount)),
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
                    Math.Clamp(server.Browse.VideoHighResolutionPixels, 1, SettingsService.MaxHighResolutionPixels))
                : null,
            server.Browse.CopyPaths && state.Has(Rights.CopyPaths) ? paths.Root : null,
            server.Player.ExternalUrl is { } url && PlayerOptions.IsAllowedExternalUrl(url) && state.Has(Rights.ExternalPlayer)
                ? new ExternalPlayer(string.IsNullOrWhiteSpace(server.Player.ExternalName) ? "external player" : server.Player.ExternalName, url)
                : null,
            settings.Appearance.Theme,
            AppVersion.Version,
            AppVersion.Commit,
            state.Access.Rights.Order(StringComparer.Ordinal).ToList(),
            state.User is { } user ? new SessionAccount(user.Id, user.UserName, user.DisplayName, user.Kind, user.IsServerAdmin, user.IsLocal) : null,
            state.Tenant is { } tenant ? new SessionTenant(tenant.Id, tenant.Slug, tenant.Name) : null,
            tenants.Where(t => reachable.Contains(t.Id)).Select(t => new SessionTenant(t.Id, t.Slug, t.Name)).ToList(),
            state.Has(Rights.EditImages)
                ? new EditorConfig(
                    LibraryPaths.NormalizeRelative(server.Editor.OutputFolder?.Trim()) ?? string.Empty,
                    Math.Clamp(server.Editor.Quality, EditedImages.MinQuality, 100))
                : null,
            streamer.IsAvailable && state.Has(Rights.Stream) && state.Has(Rights.ViewFiles)
                ? new StreamingConfig(streamer.Mode == StreamingMode.Always)
                : null,
            state.Has(Rights.DeleteFiles) || state.Has(Rights.DeletePermanently)
                ? new FilesConfig(settings.Files.DeletePermanently, Math.Max(0, settings.Files.Trash.RetentionDays))
                : null));
    }
}
