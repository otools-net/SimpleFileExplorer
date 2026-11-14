using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Sfe.Api.Security;

namespace Sfe.Api;

/// <summary>
/// What SFE adds to the OpenAPI document (<c>/openapi/v1.json</c>): the application version, the rights each route
/// needs (<c>x-sfe-rights</c>) and the problem <c>type</c> words (<c>x-sfe-problem-types</c>). Both extensions are
/// part of v1's contract: the committed baseline (tests/samples/openapi/v1.json) holds them, so a right needed where
/// none was, or a word removed, fails <c>OpenApiBaselineTests</c>.
/// </summary>
public static class ApiDocument
{
    public const string RightsExtension = "x-sfe-rights";
    public const string ProblemTypesExtension = "x-sfe-problem-types";

    public static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer(
            (document, _, _) =>
            {
                // The document's version is the application's (its default is the document name, "v1").
                document.Info.Version = AppVersion.Version;
                document.Extensions ??= new Dictionary<string, IOpenApiExtension>();
                document.Extensions[ProblemTypesExtension] = new JsonNodeExtension(Strings(ProblemTypes.All));
                return Task.CompletedTask;
            }
        );
        options.AddOperationTransformer(
            (operation, context, _) =>
            {
                AddRights(operation, context.Description.ActionDescriptor.EndpointMetadata);
                return Task.CompletedTask;
            }
        );
    }

    /// <summary>
    /// One list per requirement of the route: each list is satisfied by any one of its rights, and every list must
    /// be. A route without the extension needs no right (<c>AllowAnonymous</c>).
    /// </summary>
    private static void AddRights(OpenApiOperation operation, IList<object> metadata)
    {
        var requirements = metadata.OfType<RightMetadata>().Select(right => (JsonNode)Strings(right.AnyOf)).ToArray();
        if (requirements.Length == 0)
            return;
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions[RightsExtension] = new JsonNodeExtension(new JsonArray(requirements));
    }

    private static JsonArray Strings(IEnumerable<string> words) =>
        new(words.Select(word => (JsonNode)JsonValue.Create(word)).ToArray());
}
