using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Sfe.Api;
using Sfe.Api.Endpoints;
using Sfe.Api.Security;
using Sfe.Api.Tenancy;
using Sfe.Core;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Options;
using Sfe.Core.Settings;

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
api.MapSessionEndpoints();
api.MapAccessEndpoints();
api.MapTenantEndpoints();
api.MapConfigEndpoints();
api.MapLibraryEndpoints();
api.MapScanEndpoints();
api.MapTagEndpoints();
api.MapTrickplayEndpoints();
api.MapTaskEndpoints();
api.MapFileTrackingEndpoints();
api.MapSettingsEndpoints();
api.MapSystemEndpoints();
api.MapFallback(() => TypedResults.NotFound()).AllowAnonymous();

app.MapFallbackToFile("index.html");

// Read from the configuration: the options themselves read the libraries' settings, which need the database first.
if (builder.Configuration.GetSection($"{SfeOptions.SectionName}:{nameof(SfeOptions.Database)}").Get<DatabaseOptions>()?.MigrateOnStartup ?? true)
{
    try
    {
        // The accounts and libraries first: each library initializes its own index when it starts (TenantHost).
        await app.Services.GetRequiredService<AccessDatabaseInitializer>().InitializeAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "The accounts' database cannot be created or updated; nobody can sign in until it is fixed");
    }
}

try
{
    // The library of the configuration becomes the first library (tenant) with its index, tags and settings.
    var library = await app.Services.GetRequiredService<AccessService>().EnsureDefaultTenantAsync("Library");
    await app.Services.GetRequiredService<SettingLayers>().MovePerLibrarySettingsAsync(library.Id);
    app.Services.GetRequiredService<SettingLayers>().NotifyChanged();

    var setup = app.Services.GetRequiredService<SetupService>();
    var code = app.Services.GetRequiredService<IOptionsMonitor<SetupOptions>>().CurrentValue;
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
        app.Logger.LogInformation("Sfe:Setup:Code is set but no longer needed: an administrator exists");
    }
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Cannot read the accounts and libraries; nobody can sign in until the database is fixed");
}

app.Run();

/// <summary>Entry point marker for integration tests.</summary>
public partial class Program;
