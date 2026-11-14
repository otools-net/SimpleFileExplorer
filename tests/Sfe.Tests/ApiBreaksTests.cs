using System.Text.Json.Nodes;
using Sfe.Api;
using Sfe.Tests.Infrastructure.OpenApi;

namespace Sfe.Tests;

/// <summary>
/// The rules of <see cref="ApiBreaks"/> on small documents: adding passes, removing, renaming or asking more fails.
/// </summary>
[TestClass]
public sealed class ApiBreaksTests
{
    [TestMethod]
    public void TheSameDocumentBreaksNothing()
    {
        Assert.IsEmpty(Breaks(_ => { }));
        Assert.IsEmpty(ApiBreaks.Differences(ApiContract.Read(Document()), ApiContract.Read(Document())));
    }

    [TestMethod]
    public void AddingRoutesOptionalInputsPropertiesStatusesAndWordsBreaksNothing()
    {
        var breaks = Breaks(document =>
        {
            Paths(document)["/api/v1/other"] = new JsonObject { ["get"] = Operation() };
            Parameters(document).Add(Parameter("sort", required: false));
            Property(document, "Tag")["count"] = new JsonObject { ["type"] = "integer" };
            Responses(document)["404"] = new JsonObject { ["description"] = "Not Found" };
            Enum(document).Add("purple");
            ProblemTypesOf(document).Add("new-case");
            Rights(document)[0]!.AsArray().Add("tags.edit");
        });

        Assert.IsEmpty(breaks, string.Join("\n", breaks));
    }

    [TestMethod]
    public void AddingStillAsksForTheBaselineToBeWrittenAgain()
    {
        var baseline = ApiContract.Read(Document());
        var grown = Build();
        Paths(grown)["/api/v1/other"] = new JsonObject { ["get"] = Operation() };

        var differences = ApiBreaks.Differences(baseline, ApiContract.Read(grown.ToJsonString()));

        CollectionAssert.Contains(differences.ToList(), "GET /api/v1/other: Route");
    }

    [TestMethod]
    public void RemovingARouteBreaksIt() =>
        AssertBreaks(document => Paths(document).Remove("/api/v1/tags"), "GET /api/v1/tags: route removed");

    [TestMethod]
    public void RenamingAParameterBreaks() =>
        AssertBreaks(
            document => Parameters(document)[0]!["name"] = "folder",
            "parameter query:path: parameter removed or renamed"
        );

    [TestMethod]
    public void ANewRequiredParameterBreaks() =>
        AssertBreaks(
            document => Parameters(document).Add(Parameter("sort", required: true)),
            "parameter query:sort: new required input"
        );

    [TestMethod]
    public void AnOptionalParameterMadeRequiredBreaks() =>
        AssertBreaks(
            document => Parameters(document)[0]!["required"] = true,
            "parameter query:path: now required in what clients send"
        );

    [TestMethod]
    public void RenamingAPropertyBreaks() =>
        AssertBreaks(
            document =>
            {
                var properties = Property(document, "Tag");
                properties.Remove("name");
                properties["title"] = new JsonObject { ["type"] = "string" };
            },
            "schema Tag · property name: property removed or renamed"
        );

    [TestMethod]
    public void ANewRequiredPropertyInWhatClientsSendBreaks() =>
        AssertBreaks(
            document =>
            {
                Property(document, "TagInput")["color"] = new JsonObject { ["type"] = "string" };
                Schema(document, "TagInput")["required"]!.AsArray().Add("color");
            },
            "schema TagInput · property color: new required property in what clients send"
        );

    [TestMethod]
    public void AnEnumWordRemovedBreaks() =>
        AssertBreaks(document => Enum(document).RemoveAt(0), "schema Color · enum: word blue removed or renamed");

    [TestMethod]
    public void AStatusRemovedBreaks() =>
        AssertBreaks(
            document => Responses(document).Remove("200"),
            "GET /api/v1/tags · status 200: status no longer answered"
        );

    [TestMethod]
    public void ARightNeededWhereNoneWasBreaks() =>
        AssertBreaks(
            document => ((JsonObject)Paths(document)["/api/v1/tags"]!["get"]!).Remove(ApiDocument.RightsExtension),
            "a right is needed where none was",
            fromChanged: true
        );

    [TestMethod]
    public void AnotherRightInsteadOfTheOneNeededBreaks() =>
        AssertBreaks(
            document => Rights(document)[0]![0] = "tags.edit",
            "GET /api/v1/tags · rights: a right is needed where none was (tags.edit"
        );

    [TestMethod]
    public void ADefaultChangedBreaks() =>
        AssertBreaks(
            document => Parameters(document)[0]!["schema"]!["default"] = "Photos",
            "parameter query:path: default changed from \"\" to \"Photos\""
        );

    [TestMethod]
    public void AnInputAcceptingLessBreaks() =>
        AssertBreaks(
            document => Parameters(document)[0]!["schema"]!["type"] = "string",
            "parameter query:path: type changed from null|string to string"
        );

    [TestMethod]
    public void AnAnswerGivingNullWhereItDidNotBreaks() =>
        AssertBreaks(
            document => Property(document, "Tag")["name"]!["type"] = new JsonArray("null", "string"),
            "schema Tag · property name: type changed from string to null|string"
        );

    [TestMethod]
    public void AProblemTypeWordRemovedBreaks() =>
        AssertBreaks(
            document => ProblemTypesOf(document).RemoveAt(0),
            "problem types: word file-exists removed or renamed"
        );

    /// <summary>
    /// <paramref name="fromChanged"/>: the change is applied to the baseline (to take a right away from it) rather
    /// than to the current document.
    /// </summary>
    private static void AssertBreaks(Action<JsonObject> change, string expected, bool fromChanged = false)
    {
        var breaks = fromChanged ? Breaks(_ => { }, change) : Breaks(change);

        Assert.IsTrue(
            breaks.Any(problem => problem.Contains(expected, StringComparison.Ordinal)),
            $"Expected a break containing \"{expected}\"; found:\n{string.Join("\n", breaks)}"
        );
    }

    private static IReadOnlyList<string> Breaks(Action<JsonObject> change, Action<JsonObject>? baselineChange = null)
    {
        var baseline = Build();
        baselineChange?.Invoke(baseline);
        var current = Build();
        change(current);
        return ApiBreaks.Find(ApiContract.Read(baseline.ToJsonString()), ApiContract.Read(current.ToJsonString()));
    }

    private static string Document() => Build().ToJsonString();

    private static JsonObject Build() =>
        new()
        {
            ["openapi"] = "3.1.1",
            ["paths"] = new JsonObject
            {
                ["/api/v1/tags"] = new JsonObject { ["get"] = Operation(), ["post"] = Creation() },
            },
            ["components"] = new JsonObject { ["schemas"] = Schemas() },
            [ApiDocument.ProblemTypesExtension] = new JsonArray("file-exists", "scan-running"),
        };

    private static JsonObject Operation() =>
        new()
        {
            ["parameters"] = new JsonArray(Parameter("path", required: false, defaultValue: "")),
            ["responses"] = new JsonObject
            {
                ["200"] = new JsonObject
                {
                    ["content"] = new JsonObject
                    {
                        ["application/json"] = new JsonObject { ["schema"] = Reference("Tag") },
                    },
                },
            },
            [ApiDocument.RightsExtension] = new JsonArray(new JsonArray("tags.view")),
        };

    private static JsonObject Creation() =>
        new()
        {
            ["requestBody"] = new JsonObject
            {
                ["required"] = true,
                ["content"] = new JsonObject
                {
                    ["application/json"] = new JsonObject { ["schema"] = Reference("TagInput") },
                },
            },
            ["responses"] = new JsonObject { ["204"] = new JsonObject { ["description"] = "No Content" } },
        };

    private static JsonObject Schemas() =>
        new()
        {
            ["Tag"] = new JsonObject
            {
                ["required"] = new JsonArray("name"),
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["name"] = new JsonObject { ["type"] = "string" },
                    ["color"] = Reference("Color"),
                },
            },
            ["TagInput"] = new JsonObject
            {
                ["required"] = new JsonArray("name"),
                ["type"] = "object",
                ["properties"] = new JsonObject { ["name"] = new JsonObject { ["type"] = "string" } },
            },
            ["Color"] = new JsonObject { ["enum"] = new JsonArray("blue", "red") },
        };

    private static JsonObject Parameter(string name, bool required, string? defaultValue = null)
    {
        var schema = new JsonObject { ["type"] = new JsonArray("null", "string") };
        if (defaultValue is not null)
            schema["default"] = defaultValue;
        return new JsonObject
        {
            ["name"] = name,
            ["in"] = "query",
            ["required"] = required,
            ["schema"] = schema,
        };
    }

    private static JsonObject Reference(string schema) => new() { ["$ref"] = "#/components/schemas/" + schema };

    private static JsonObject Paths(JsonObject document) => document["paths"]!.AsObject();

    private static JsonObject Get(JsonObject document) => Paths(document)["/api/v1/tags"]!["get"]!.AsObject();

    private static JsonArray Parameters(JsonObject document) => Get(document)["parameters"]!.AsArray();

    private static JsonObject Responses(JsonObject document) => Get(document)["responses"]!.AsObject();

    private static JsonArray Rights(JsonObject document) => Get(document)[ApiDocument.RightsExtension]!.AsArray();

    private static JsonObject Schema(JsonObject document, string name) =>
        document["components"]!["schemas"]![name]!.AsObject();

    private static JsonObject Property(JsonObject document, string schema) =>
        Schema(document, schema)["properties"]!.AsObject();

    private static JsonArray Enum(JsonObject document) => Schema(document, "Color")["enum"]!.AsArray();

    private static JsonArray ProblemTypesOf(JsonObject document) =>
        document[ApiDocument.ProblemTypesExtension]!.AsArray();
}
