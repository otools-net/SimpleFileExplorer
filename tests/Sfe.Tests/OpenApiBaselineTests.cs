using Otools.Net.Sfe.Tests.Infrastructure.OpenApi;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The v1 API keeps its promises: the OpenAPI document the server serves now is compared with the baseline
/// committed in tests/samples/openapi/v1.json, by the rules of agents/docs/development/api-versioning.md
/// (<see cref="ApiBreaks"/>). Adding is allowed and only asks for the baseline to be written again in the same
/// commit, so that what is added is kept from then on.
/// </summary>
[TestClass]
public sealed class OpenApiBaselineTests
{
    private static string? _current;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task TheApiKeepsTheV1ContractOfTheBaseline()
    {
        var baseline = ApiContract.Read(await File.ReadAllTextAsync(OpenApiDocuments.BaselinePath, Token));
        var current = ApiContract.Read(await CurrentAsync());

        var breaks = ApiBreaks.Find(baseline, current);

        Assert.IsEmpty(
            breaks,
            "The v1 API changed in a way that breaks its clients:\n  "
                + string.Join("\n  ", breaks)
                + "\nKeep v1 as it was and add instead (a new route, a new optional input, a new property), or make "
                + "the change in /api/v2 for that route group (agents/docs/development/api-versioning.md). Only for a "
                + $"new major version of the API, write the baseline again: {OpenApiDocuments.UpdateCommand}"
        );
    }

    [TestMethod]
    public async Task TheBaselineHoldsEverythingTheApiOffers()
    {
        var baseline = ApiContract.Read(await File.ReadAllTextAsync(OpenApiDocuments.BaselinePath, Token));
        var current = ApiContract.Read(await CurrentAsync());

        var differences = ApiBreaks.Differences(baseline, current);

        Assert.IsEmpty(
            differences,
            "The v1 API differs from its baseline (tests/samples/openapi/v1.json):\n  "
                + string.Join("\n  ", differences)
                + "\nWhen nothing breaks (TheApiKeepsTheV1ContractOfTheBaseline passes), write the baseline again in "
                + $"the same commit, so that what was added is kept from now on: {OpenApiDocuments.UpdateCommand}"
        );
    }

    /// <summary>The documented command: writes the document served now as the baseline.</summary>
    [TestMethod]
    public async Task WritesTheBaselineWhenAsked()
    {
        if (Environment.GetEnvironmentVariable(OpenApiDocuments.UpdateVariable) != "write")
        {
            Assert.Inconclusive($"Set {OpenApiDocuments.UpdateVariable}=write to write the OpenAPI baseline.");
            return;
        }

        var document = (await CurrentAsync()).ReplaceLineEndings("\n");

        Directory.CreateDirectory(Path.GetDirectoryName(OpenApiDocuments.BaselinePath)!);
        await File.WriteAllTextAsync(OpenApiDocuments.BaselinePath, document, Token);
        Assert.IsNotEmpty(ApiContract.Read(document));
    }

    private CancellationToken Token => TestContext.CancellationToken;

    private async Task<string> CurrentAsync() => _current ??= await OpenApiDocuments.ReadCurrentAsync(Token);
}
