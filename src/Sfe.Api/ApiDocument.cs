using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Otools.Net.Sfe.Api.Security;

namespace Otools.Net.Sfe.Api;

/// <summary>
/// What SFE adds to the OpenAPI document (<c>/openapi/v1.json</c>): the application version, the rights each route
/// needs (<c>x-sfe-rights</c>) and the problem <c>type</c> words (<c>x-sfe-problem-types</c>). Both extensions are
/// part of v1's contract: the committed baseline (tests/samples/openapi/v1.json) holds them, so a right needed where
/// none was, or a word removed, fails <c>OpenApiBaselineTests</c>. It also declares the access tokens' bearer scheme
/// (<see cref="BearerScheme"/>), so that Swagger UI's "Authorize" sends one, except to the routes for sessions only
/// (<c>x-sfe-session-only</c>, <see cref="SessionOnlyRoutes"/>).
/// </summary>
public static class ApiDocument
{
    public const string RightsExtension = "x-sfe-rights";
    public const string ProblemTypesExtension = "x-sfe-problem-types";
    public const string SessionOnlyExtension = "x-sfe-session-only";

    /// <summary>The name of the access tokens' security scheme in the document.</summary>
    public const string BearerScheme = "accessToken";

    private const string BearerDescription = """
        A personal access token (My account › Access tokens), sent as `Authorization: Bearer sfe_…`. Without one,
        the browser's session cookie is used. Routes marked `x-sfe-session-only` refuse tokens (403
        `session-only`).
        """;

    public static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer(
            (document, _, _) =>
            {
                // The document's version is the application's (its default is the document name, "v1").
                document.Info.Version = AppVersion.Version;
                document.Extensions ??= new Dictionary<string, IOpenApiExtension>();
                document.Extensions[ProblemTypesExtension] = new JsonNodeExtension(Strings(ProblemTypes.All));
                AddBearerScheme(document);
                return Task.CompletedTask;
            }
        );
        options.AddOperationTransformer(
            (operation, context, _) =>
            {
                AddRights(operation, context.Description.ActionDescriptor.EndpointMetadata);
                MarkSessionOnly(operation, context.Description.ActionDescriptor.EndpointMetadata);
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

    /// <summary>
    /// The bearer scheme, asked by every route: with a token, or (the empty requirement) with the session cookie.
    /// </summary>
    private static void AddBearerScheme(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "sfe_<lookup>_<secret>",
            Description = BearerDescription,
        };
        document.Security =
        [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [] },
            new OpenApiSecurityRequirement(),
        ];
    }

    /// <summary>
    /// A route for sessions only says so, and asks for no scheme: Swagger UI then calls it with the cookie alone.
    /// </summary>
    private static void MarkSessionOnly(OpenApiOperation operation, IList<object> metadata)
    {
        if (!metadata.OfType<SessionOnlyMetadata>().Any())
            return;
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions[SessionOnlyExtension] = new JsonNodeExtension(JsonValue.Create(true));
        operation.Security = [new OpenApiSecurityRequirement()];
    }

    private static JsonArray Strings(IEnumerable<string> words) =>
        new(words.Select(word => (JsonNode)JsonValue.Create(word)).ToArray());
}
