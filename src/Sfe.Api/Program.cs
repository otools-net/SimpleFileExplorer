using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Sfe.Api;
using Sfe.Api.Endpoints;
using Sfe.Api.Maintenance;
using Sfe.Api.Security;
using Sfe.Api.Tenancy;
using Sfe.Core;
using Sfe.Core.Access;
using Sfe.Core.Options;

var builder = WebApplication.CreateBuilder(PortableBuild.CreateOptions(args));

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
api.MapFallback(() => TypedResults.NotFound()).AllowAnonymous();

app.MapFallbackToFile("index.html");

// The install's version first, read without changing anything: one of an earlier version waits for its update,
// confirmed with the setup code on the update page, before anything touches its databases or settings. Otherwise the
// schema, the first library, the rights added since and the settings are brought up as at every start.
var migration = app.Services.GetRequiredService<MigrationCoordinator>();
await migration.InitializeAsync();
var code = app.Services.GetRequiredService<IOptionsMonitor<SetupOptions>>().CurrentValue;
switch (migration.Status)
{
    case MigrationStatus.Required:
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
            if (await setup.IsRequiredAsync())
            {
                if (code.HasUsableCode)
                    app.Logger.LogWarning("No administrator yet: open the app in a browser and enter the setup code (Sfe:Setup:Code) to create one");
                else
                    app.Logger.LogWarning(
                        "No administrator yet and no setup code: set Sfe:Setup:Code (e.g. the environment variable Sfe__Setup__Code) to at least {Length} characters, restart, then open the app",
                        SetupOptions.MinimumCodeLength);
            }
            else if (!string.IsNullOrWhiteSpace(code.Code) && !code.Recover)
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

app.Run();

/// <summary>Entry point marker for integration tests.</summary>
public partial class Program;
