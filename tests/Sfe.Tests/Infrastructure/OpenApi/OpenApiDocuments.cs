using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Otools.Net.Sfe.Tests.Infrastructure.OpenApi;

/// <summary>The v1 OpenAPI document as the server serves it now, and the baseline committed in tests/samples.</summary>
public static class OpenApiDocuments
{
    /// <summary>The variable that makes <c>OpenApiBaselineTests</c> write the baseline: <c>write</c>.</summary>
    public const string UpdateVariable = "SFE_OPENAPI_BASELINE";

    /// <summary>The command that writes the baseline (agents/docs/development/api-versioning.md).</summary>
    public const string UpdateCommand =
        $"{UpdateVariable}=write dotnet test tests/Sfe.Tests --filter "
        + "FullyQualifiedName~OpenApiBaselineTests.WritesTheBaselineWhenAsked";

    public static string BaselinePath { get; } =
        Path.GetFullPath(Path.Combine(TestSamples.Folder, "..", "openapi", "v1.json"));

    /// <summary>
    /// <c>/openapi/v1.json</c> of a new server, read by the server administrator the setup creates (who has the right
    /// to read the API's documentation).
    /// </summary>
    public static async Task<string> ReadCurrentAsync(CancellationToken cancellationToken)
    {
        using var library = new TestLibrary();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var (key, value) in library.Settings())
                builder.UseSetting(key, value);
        });
        using var client = await ApiClients.SetUpAsync(factory, cancellationToken);
        return await client.GetStringAsync("/openapi/v1.json", cancellationToken);
    }
}
