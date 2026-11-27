namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>Every group of routes of the API, mapped under <see cref="ApiRoutes.Base"/> by Program.cs.</summary>
public static class ApiEndpoints
{
    public static RouteGroupBuilder MapApiEndpoints(this RouteGroupBuilder api)
    {
        api.MapSetupEndpoints();
        api.MapInstallationEndpoints();
        api.MapMigrationEndpoints();
        api.MapSessionEndpoints();
        api.MapAccessEndpoints();
        api.MapTokenEndpoints();
        api.MapFolderRuleEndpoints();
        api.MapTenantEndpoints();
        api.MapConfigEndpoints();
        api.MapLibraryEndpoints();
        api.MapScanEndpoints();
        api.MapTagEndpoints();
        api.MapTrickplayEndpoints();
        api.MapStreamingEndpoints();
        api.MapTaskEndpoints();
        api.MapFileTrackingEndpoints();
        api.MapEditorEndpoints();
        api.MapFileEndpoints();
        api.MapTransferEndpoints();
        api.MapUploadEndpoints();
        api.MapArchiveEndpoints();
        api.MapPropertyEndpoints();
        api.MapStorageEndpoints();
        api.MapTextEndpoints();
        api.MapVersionEndpoints();
        api.MapEpubEndpoints();
        api.MapExploreEndpoints();
        api.MapFlashEndpoints();
        api.MapChangeEndpoints();
        api.MapSettingsEndpoints();
        api.MapQuickLinkEndpoints();
        api.MapSystemEndpoints();
        api.MapLogEndpoints();
        api.MapKeepAliveEndpoints();
        api.MapHelpEndpoints();
        MapConnectionEndpoints(api);
        return api;
    }

    private static void MapConnectionEndpoints(RouteGroupBuilder api)
    {
        api.MapConnectionProviderEndpoints();
        api.MapConnectionSignInEndpoints();
        api.MapConnectionEndpoints();
        api.MapConnectionFileEndpoints();
        api.MapConnectionPreviewEndpoints();
        api.MapConnectionWriteEndpoints();
        api.MapConnectionIndexEndpoints();
        api.MapConnectionSearchEndpoints();
        api.MapConnectionTagEndpoints();
        api.MapConnectionUploadEndpoints();
        api.MapConnectionArchiveEndpoints();
        api.MapConnectionFolderRuleEndpoints();
    }
}
