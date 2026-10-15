using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Options;
using Sfe.Api;
using Sfe.Api.Endpoints;
using Sfe.Api.Maintenance;
using Sfe.Api.Security;
using Sfe.Api.Tenancy;
using Sfe.Core;
using Sfe.Core.Access;
using Sfe.Core.Options;
using Sfe.Core.Paths;

// "--install-service" / "--uninstall-service" (Windows): register the program as a service, then stop.
if (WindowsServiceSetup.TryRun(args) is { } serviceExitCode)
    return serviceExitCode;

WebApplicationBuilder builder;
try
{
    builder = WebApplication.CreateBuilder(PortableBuild.CreateOptions(args));
    // This machine's own values (appsettings.Local.json, git-ignored) right after appsettings.json, so that
    // appsettings.{Environment}.json still overrides them; after all of them, what the installation page wrote (library
    // folder, address, ffmpeg). The environment and the command line still have the last word.
    var local = new JsonConfigurationSource { Path = Path.Combine(builder.Environment.ContentRootPath, "appsettings.Local.json"), Optional = true, ReloadOnChange = true };
    local.ResolveFileProvider();
    var main = builder.Configuration.Sources.ToList().FindIndex(source => source is JsonConfigurationSource { Path: "appsettings.json" });
    builder.Configuration.Sources.Insert(main >= 0 ? main + 1 : InstallationSettings.AfterAppSettings(builder.Configuration), local);
    InstallationSettings.AddTo(builder.Configuration, InstallationSettings.ResolvePath(builder.Configuration, builder.Environment.ContentRootPath));
}
catch (InvalidDataException ex)
{
    return StartupErrors.ConfigurationFile(ex);
}

// Integrates with the service manager when started by one: Windows service or systemd unit (no-op otherwise).
builder.Host.UseWindowsService();
builder.Host.UseSystemd();

// Relative paths in the settings (thumbnail cache, SQLite file, keys) live next to the application.
builder.Services.AddSfeAccess(builder.Configuration, builder.Environment.ContentRootPath);
// Each library (tenant) has its own index, caches and workers (TenantRegistry); requests use their library's.
builder.Services.AddSfeTenancy(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddSfeSecurity(builder.Configuration, builder.Environment.ContentRootPath);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddProblemDetails();
// The document carries the application version (its default is the document name, "v1").
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Version = AppVersion.Version;
    return Task.CompletedTask;
}));

var app = builder.Build();
app.Logger.LogInformation("SFE {Version} ({Commit})", AppVersion.Version, AppVersion.Commit ?? "unknown commit");

app.UseExceptionHandler();
app.UseStatusCodePages();

// Who asks, then what the setup, the CSRF header and the documentation pages require, then each route's rights.
app.UseAuthentication();
app.UseMiddleware<RequestGuard>();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<TenantMiddleware>();

app.MapOpenApi().RequireRight(Rights.ApiDocs);
// Swagger UI's files are embedded in the Swashbuckle package and served by the app itself.
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "api/docs";
    // Relative to /api/docs/index.html, so it also works when the app is hosted under a virtual directory.
    options.SwaggerEndpoint("../../openapi/v1.json", "SFE API v1");
    options.DocumentTitle = "SFE API";
    // Swagger UI would otherwise send the OpenAPI document to validator.swagger.io for its validity badge.
    options.ConfigObject.ValidatorUrl = null;
});

app.UseDefaultFiles();
app.MapStaticAssets();

var api = app.MapGroup("/api");
api.MapSetupEndpoints();
api.MapInstallationEndpoints();
api.MapMigrationEndpoints();
api.MapSessionEndpoints();
api.MapAccessEndpoints();
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
api.MapSettingsEndpoints();
api.MapQuickLinkEndpoints();
api.MapSystemEndpoints();
api.MapLogEndpoints();
api.MapFallback(() => TypedResults.NotFound()).AllowAnonymous();

app.MapFallbackToFile("index.html");

// The install's version first, read without changing anything: one of an earlier version waits for its update,
// confirmed with the setup code on the update page, before anything touches its databases or settings. Otherwise the
// schema, the first library, the rights added since and the settings are brought up as at every start.
var migration = app.Services.GetRequiredService<MigrationCoordinator>();
await migration.InitializeAsync();
var setupOptions = app.Services.GetRequiredService<IOptionsMonitor<SetupOptions>>();
var code = setupOptions.CurrentValue;
// The addresses the server listens on from now (the installation page tells when a restart is due).
app.Services.GetRequiredService<InstallationSettings>();
switch (migration.Status)
{
    case MigrationStatus.Required:
        code = EnsureSetupCode(code);
        app.Logger.LogWarning(
            "The databases and settings belong to SFE {Installed}; this is SFE {Version}. Nobody can use the app until they are updated: open it in a browser and confirm the update with the setup code{Hint}",
            migration.DatabaseVersion ?? "(before 1.37.0)", AppVersion.Version,
            code.HasUsableCode ? "" : $" (set Sfe:Setup:Code to at least {SetupOptions.MinimumCodeLength} characters first, then restart)");
        break;
    case MigrationStatus.Newer:
        app.Logger.LogError("The databases belong to SFE {Installed}, later than this SFE {Version}: install that version again, or restore a backup",
            migration.DatabaseVersion, AppVersion.Version);
        break;
    case MigrationStatus.None:
        try
        {
            var setup = app.Services.GetRequiredService<SetupService>();
            if (await setup.IsRequiredAsync() || code.Recover)
            {
                code = EnsureSetupCode(code);
                if (!code.HasUsableCode)
                    app.Logger.LogWarning(
                        "No administrator yet and no usable setup code: set Sfe:Setup:Code (e.g. the environment variable Sfe__Setup__Code) to at least {Length} characters, restart, then open the app",
                        SetupOptions.MinimumCodeLength);
                else if (!code.CodeIsGenerated)
                    app.Logger.LogWarning("No administrator yet: open the app in a browser and enter the setup code (Sfe:Setup:Code) to create one");
            }
            else if (!string.IsNullOrWhiteSpace(code.Code) && !code.Recover && !code.CodeIsGenerated)
            {
                app.Logger.LogInformation("Sfe:Setup:Code is set but no longer needed: an administrator exists (keep it for the next update)");
            }
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Cannot read the accounts and libraries; nobody can sign in until the database is fixed");
        }
        break;
}

if (app.Configuration[InstallationSettings.RootKey] is { Length: > 0 } root && !Directory.Exists(LibraryPaths.NormalizeRoot(root)))
    app.Logger.LogWarning("The library folder {Root} does not exist: pick another in Administration › System › Installation (or set Sfe:Root)", root);

try
{
    await app.StartAsync();
}
catch (IOException ex) when (ex.InnerException is AddressInUseException)
{
    return StartupErrors.AddressInUse(ex);
}

if (code.CodeIsGenerated && (migration.Status == MigrationStatus.Required || code.Recover || await SetupStillRequiredAsync()))
    AnnounceSetupCode(code.Code!);
BrowserLauncher.OpenIfWanted(app);
await app.WaitForShutdownAsync();
return 0;

// Without a code in the configuration, the one the server makes itself (data/setup-code.txt), so that nobody has to
// edit a file before the first start or an update.
SetupOptions EnsureSetupCode(SetupOptions current)
{
    if (!string.IsNullOrWhiteSpace(current.Code))
        return current;
    var generated = app.Services.GetRequiredService<GeneratedSetupCode>();
    try
    {
        generated.Ensure();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        app.Logger.LogError(ex, "Cannot write the setup code to {Path}: set Sfe:Setup:Code instead", generated.FilePath);
    }

    return setupOptions.CurrentValue;
}

async Task<bool> SetupStillRequiredAsync()
{
    try
    {
        return await app.Services.GetRequiredService<SetupService>().IsRequiredAsync();
    }
    catch (Exception)
    {
        return false;
    }
}

// After the start, so that the addresses are known and the box is the last thing in the console.
void AnnounceSetupCode(string setupCode)
{
    var address = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
        .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?.Addresses.FirstOrDefault();
    var url = BrowserLauncher.BrowserUrl(address) ?? "SFE's address";
    app.Logger.LogWarning("""

        ==============================================================
          Open {Url} in a browser and enter this setup code:

              {Code}

          (also kept in {File})
        ==============================================================
        """, url, setupCode, app.Services.GetRequiredService<GeneratedSetupCode>().FilePath);
}


/// <summary>Entry point marker for integration tests.</summary>
public partial class Program;
