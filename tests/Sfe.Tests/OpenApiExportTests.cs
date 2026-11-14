using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Writes the OpenAPI document to the file named by <c>SFE_OPENAPI_OUT</c>, so that a refactoring can show the API is
/// unchanged by comparing the documents written before and after it (agents/tools/readability/references.mjs).
/// Inconclusive without the variable: the document's content is covered by the route tests.
/// </summary>
[TestClass]
public sealed class OpenApiExportTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task WritesTheOpenApiDocumentWhenAsked()
    {
        var target = Environment.GetEnvironmentVariable("SFE_OPENAPI_OUT");
        if (string.IsNullOrEmpty(target))
        {
            Assert.Inconclusive("Set SFE_OPENAPI_OUT to a file path to write the OpenAPI document there.");
            return;
        }

        var document = await ReadDocumentAsync();

        await File.WriteAllTextAsync(target, document, TestContext.CancellationToken);
        StringAssert.Contains(document, $"{V1}/search");
    }

    private async Task<string> ReadDocumentAsync()
    {
        using var library = new TestLibrary();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var (key, value) in library.Settings())
                builder.UseSetting(key, value);
        });
        // Signed in as the server administrator the setup creates, who has the right to read the API's documentation.
        using var client = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        return await client.GetStringAsync("/openapi/v1.json", TestContext.CancellationToken);
    }
}
