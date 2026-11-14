using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticAssets;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Writes every endpoint with what guards it to the file named by <c>SFE_ROUTES_OUT</c>: method and pattern, name,
/// rights, folder scope, authorization policies, rate limit, and the types of its metadata in order. What the OpenAPI
/// document does not show, so that a refactoring of the route maps can be compared before and after it
/// (agents/tools/readability/references.mjs). Inconclusive without the variable.
/// </summary>
[TestClass]
public sealed class RouteExportTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task WritesTheRoutesWhenAsked()
    {
        var target = Environment.GetEnvironmentVariable("SFE_ROUTES_OUT");
        if (string.IsNullOrEmpty(target))
        {
            Assert.Inconclusive("Set SFE_ROUTES_OUT to a file path to write the routes there.");
            return;
        }

        using var library = new TestLibrary();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var (key, value) in library.Settings())
                builder.UseSetting(key, value);
        });
        using var client = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var routes = factory
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            // The client's files are routes too, one per file of the build: left out (they change with every build).
            .Where(e => e.Metadata.GetMetadata<StaticAssetDescriptor>() is null)
            .Select(Describe)
            .ToList();

        await File.WriteAllTextAsync(target, string.Join('\n', routes) + '\n', TestContext.CancellationToken);
        Assert.IsGreaterThan(50, routes.Count);
    }

    /// <summary>
    /// Not the compiler's own bookkeeping on the handler (where it puts <c>NullableContextAttribute</c> depends on the
    /// rest of the class), nor generated types.
    /// </summary>
    private static bool IsMeaningful(string typeName) =>
        !typeName.Contains('<')
        && typeName
            is not "NullableContextAttribute"
                and not "NullableAttribute"
                and not "CompilerGeneratedAttribute"
                and not "DebuggerStepThroughAttribute"
                and not "AsyncStateMachineAttribute";

    private static string Describe(RouteEndpoint endpoint)
    {
        var metadata = endpoint.Metadata;
        var text = new StringBuilder();
        text.Append(string.Join('/', metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []))
            .Append(' ')
            .Append(endpoint.RoutePattern.RawText);
        text.Append("\n  name: ").Append(metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        foreach (var right in metadata.GetOrderedMetadata<RightMetadata>())
            text.Append("\n  rights: ").Append(string.Join(", ", right.AnyOf));
        foreach (var folder in metadata.GetOrderedMetadata<FolderMetadata>())
            text.Append("\n  folders: ").Append(folder.How ?? "(not library paths)");
        foreach (var policy in metadata.GetOrderedMetadata<IAuthorizeData>())
            text.Append("\n  authorize: ").Append(policy.Policy ?? "(default)");
        foreach (var limit in metadata.GetOrderedMetadata<EnableRateLimitingAttribute>())
            text.Append("\n  rate limit: ").Append(limit.PolicyName);
        text.Append("\n  metadata: ")
            .Append(string.Join(", ", metadata.Select(m => m.GetType().Name).Where(IsMeaningful)));
        return text.ToString();
    }
}
