using Otools.Net.Sfe.Tests.Infrastructure.OpenApi;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Writes the OpenAPI document to the file named by <c>SFE_OPENAPI_OUT</c>, so that a refactoring can show the API is
/// unchanged by comparing the documents written before and after it (agents/tools/readability/references.mjs).
/// Inconclusive without the variable: the document's content is covered by the route tests and
/// <see cref="OpenApiBaselineTests"/>.
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

        var document = await OpenApiDocuments.ReadCurrentAsync(TestContext.CancellationToken);

        await File.WriteAllTextAsync(target, document, TestContext.CancellationToken);
        StringAssert.Contains(document, $"{V1}/search");
    }
}
