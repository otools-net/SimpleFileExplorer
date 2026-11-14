using System.Reflection;
using System.Text.RegularExpressions;
using Sfe.Api;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// The problem <c>type</c> words are listed in one place, <see cref="ProblemTypes"/>: every word the server answers
/// with comes from it, and the notes list the same words.
/// </summary>
[TestClass]
public sealed partial class ProblemTypesTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(TestSamples.Folder, "..", "..", "..")
    );

    [TestMethod]
    public void TheListHoldsEveryWordOnceInOrder()
    {
        var constants = typeof(ProblemTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        CollectionAssert.AreEquivalent(constants, ProblemTypes.All.ToList(), "ProblemTypes.All lists every constant");
        CollectionAssert.AllItemsAreUnique(ProblemTypes.All.ToList());
        CollectionAssert.AreEqual(ProblemTypes.All.Order(StringComparer.Ordinal).ToList(), ProblemTypes.All.ToList());
        Assert.IsEmpty(ProblemTypes.All.Where(word => !WordShape().IsMatch(word)).ToList(), "Lower case, hyphens");
    }

    /// <summary>The constants named <c>…Type</c> next to the endpoints are words of the list.</summary>
    [TestMethod]
    public void EveryTypeConstantOfTheServerIsInTheList()
    {
        var outside = TypeConstants().Where(constant => !ProblemTypes.All.Contains(constant.Value)).ToList();

        Assert.IsGreaterThan(20, TypeConstants().Count);
        Assert.IsEmpty(outside, "Not in ProblemTypes: " + string.Join(", ", outside.Select(c => c.Key)));
    }

    /// <summary>
    /// Every <c>type:</c> given to a problem in src/Sfe.Api is a word of the list (<c>ProblemTypes.X</c>, or a
    /// constant named <c>…Type</c> checked above), a parameter passing one on (<c>type</c>), or none.
    /// </summary>
    [TestMethod]
    public void EveryTypeTheServerAnswersWithIsInTheList()
    {
        var names = TypeConstants().Keys.Select(key => key[(key.LastIndexOf('.') + 1)..]).ToHashSet();
        var arguments = ServerSources()
            .SelectMany(file =>
                TypeArgument()
                    .Matches(File.ReadAllText(file))
                    .Select(argument => (File: Path.GetFileName(file), Value: argument.Groups["value"].Value))
            )
            .ToList();

        var unknown = arguments.Where(argument => !IsKnown(argument.Value, names)).ToList();

        Assert.IsGreaterThan(40, arguments.Count, "The problems of src/Sfe.Api are found");
        Assert.IsEmpty(
            unknown,
            "Problem types not taken from ProblemTypes:\n"
                + string.Join("\n", unknown.Select(a => $"{a.File}: {a.Value}"))
        );
    }

    [TestMethod]
    public void TheNotesListTheSameWords()
    {
        var note = File.ReadAllText(Path.Combine(RepositoryRoot, "agents", "docs", "development", "api-versioning.md"));

        var missing = ProblemTypes.All.Where(word => !note.Contains($"`{word}`", StringComparison.Ordinal)).ToList();

        Assert.IsEmpty(
            missing,
            "Not listed in agents/docs/development/api-versioning.md: " + string.Join(", ", missing)
        );
    }

    private static IEnumerable<string> ServerSources()
    {
        var obj = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        return Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src", "Sfe.Api"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(obj, StringComparison.Ordinal));
    }

    private static bool IsKnown(string value, HashSet<string> names) =>
        Shape().IsMatch(value)
        && Reference()
            .Matches(value)
            .Select(name => name.Value)
            .All(name =>
                name is "type" or "null"
                || name.StartsWith("ProblemTypes.", StringComparison.Ordinal)
                || names.Contains(name[(name.LastIndexOf('.') + 1)..])
            );

    /// <summary>Every public constant of the server named <c>…Type</c>, by <c>Class.Name</c>.</summary>
    private static Dictionary<string, string> TypeConstants() =>
        typeof(ProblemTypes)
            .Assembly.GetTypes()
            .Where(type => type != typeof(ProblemTypes))
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string) && field.Name.EndsWith("Type"))
            .ToDictionary(field => $"{field.DeclaringType!.Name}.{field.Name}", f => (string)f.GetRawConstantValue()!);

    [GeneratedRegex("^[a-z]+(-[a-z]+)*$")]
    private static partial Regex WordShape();

    /// <summary>A named argument <c>type:</c>, not the search term of a description (<c>`type:image`</c>).</summary>
    [GeneratedRegex(@"(?<!`)\btype:\s*(?<value>[^,)\r\n]+)")]
    private static partial Regex TypeArgument();

    /// <summary>A name, or a choice between two names: <c>a == b ? XType : YType</c>.</summary>
    [GeneratedRegex(@"^(?:[\w.]+\s*==\s*[\w.]+\s*\?\s*)?[\w.]+(?:\s*:\s*[\w.]+)?\s*$")]
    private static partial Regex Shape();

    [GeneratedRegex(@"[\w.]+(?=\s*(?::|$))")]
    private static partial Regex Reference();
}
