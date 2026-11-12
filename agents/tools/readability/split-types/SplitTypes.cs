#:package Microsoft.CodeAnalysis.CSharp@5.0.0
#:property PublishAot=false
// Moves the top-level types of C# files into files of their own (readability plan, phase 3), text unchanged: each
// type with its doc comment and attributes, the file's usings copied (`dotnet format style` then removes those not
// needed), same namespace. See ../README.md.
//
//   dotnet run agents/tools/readability/split-types/SplitTypes.cs -- [--dry-run] <file or folder>...
//
// Where a type goes:
// - the type named like its file stays;
// - `XEndpoints.cs`: the other types go to `XContracts.cs` (the requests and answers of its routes);
// - `X.cs` listed in `ModelsOf` (a class with a family of small records): the others go to `XModels.cs`;
// - a file that is already a group (`…Models.cs`, `…Contracts.cs`) and within 400 lines is left;
// - anything else: `<Type>.cs` next to it.
// A destination that exists already and was not written by this run is refused (the file is left as it was).
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var dryRun = args.Contains("--dry-run");
var roots = args.Where(a => a != "--dry-run").ToList();
if (roots.Count == 0)
{
    Console.Error.WriteLine("usage: SplitTypes.cs [--dry-run] <file or folder>...");
    return 2;
}

// Classes whose records are one family: they go together into <Class>Models.cs.
string[] modelsOf = ["EditableSettings"];

var files = roots
    .SelectMany(root =>
        Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories) : [root]
    )
    .Where(path =>
        !path.Split(Path.DirectorySeparatorChar, '/')
            .Any(part => part is "bin" or "obj" or "Migrations" or "AccessMigrations")
    )
    .Order(StringComparer.Ordinal)
    .ToList();

var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var moved = 0;
foreach (var path in files)
    moved += Split(path);
Console.WriteLine($"{moved} type(s) {(dryRun ? "to move" : "moved")}");
return 0;

int Split(string path)
{
    var source = File.ReadAllText(path);
    var eol = source.Contains("\r\n") ? "\r\n" : "\n";
    var root = CSharpSyntaxTree
        .ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))
        .GetCompilationUnitRoot();
    if (root.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
    {
        Console.Error.WriteLine($"{path}: does not parse, left");
        return 0;
    }
    // Program.cs (top-level statements) and anything else than one file-scoped namespace: left.
    if (root.Members.Count != 1 || root.Members[0] is not FileScopedNamespaceDeclarationSyntax ns)
        return 0;
    var types = ns.Members.Where(m => m is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax).ToList();
    if (types.Count < 2)
        return 0;

    var fileName = Path.GetFileNameWithoutExtension(path);
    var folder = Path.GetDirectoryName(path)!;
    var lines = source.Split('\n').Length;
    if ((fileName.EndsWith("Models") || fileName.EndsWith("Contracts")) && lines <= 400)
        return 0;

    string? group =
        fileName.EndsWith("Endpoints") ? fileName[..^"Endpoints".Length] + "Contracts"
        : modelsOf.Contains(fileName) ? fileName + "Models"
        : null;
    var destinations = new Dictionary<string, List<MemberDeclarationSyntax>>(StringComparer.OrdinalIgnoreCase);
    foreach (var type in types)
    {
        var name = NameOf(type);
        if (name == fileName || type.Modifiers.Any(SyntaxKind.FileKeyword))
            continue;
        var target = Path.Combine(folder, (group ?? name) + ".cs");
        if (!destinations.TryGetValue(target, out var list))
            destinations[target] = list = [];
        list.Add(type);
    }
    if (destinations.Count == 0)
        return 0;
    foreach (var target in destinations.Keys)
    {
        if (File.Exists(target) && !written.Contains(target))
        {
            Console.Error.WriteLine($"{path}: {Path.GetFileName(target)} exists already, file left");
            return 0;
        }
    }

    // The head of every new file: the usings, then the namespace.
    var head = new StringBuilder();
    foreach (var u in root.Usings)
        head.Append(u.ToFullString().TrimStart('\r', '\n'));
    if (root.Usings.Count > 0)
        head.Append(eol);
    head.Append("namespace ").Append(ns.Name.ToString()).Append(';').Append(eol);

    foreach (var (target, members) in destinations)
    {
        var body = string.Join(eol, members.Select(m => TrimBlankLines(m.ToFullString(), eol)));
        Console.WriteLine($"{path} -> {Path.GetFileName(target)}: {string.Join(", ", members.Select(NameOf))}");
        if (dryRun)
            continue;
        if (written.Contains(target))
            File.AppendAllText(target, eol + body + eol);
        else
            File.WriteAllText(target, head + eol + body + eol);
        written.Add(target);
    }
    if (dryRun)
        return destinations.Values.Sum(m => m.Count);

    var removed = destinations.Values.SelectMany(m => m).ToHashSet();
    if (ns.Members.All(removed.Contains))
    {
        // Nothing was named after the file: it is gone, its types each in theirs.
        File.Delete(path);
        return removed.Count;
    }
    var kept = root.RemoveNodes(removed, SyntaxRemoveOptions.KeepNoTrivia)!.ToFullString();
    File.WriteAllText(path, CollapseBlankLines(kept, eol));
    return removed.Count;
}

static string NameOf(MemberDeclarationSyntax type) =>
    type switch
    {
        BaseTypeDeclarationSyntax t => t.Identifier.Text,
        DelegateDeclarationSyntax d => d.Identifier.Text,
        _ => "?",
    };

/** A member's text without the blank lines before it (its comments and attributes stay) and its end trimmed. */
static string TrimBlankLines(string text, string eol)
{
    var lines = text.Split(eol).ToList();
    while (lines.Count > 0 && lines[0].Trim().Length == 0)
        lines.RemoveAt(0);
    return string.Join(eol, lines).TrimEnd();
}

/** No two blank lines in a row, one newline at the end. */
static string CollapseBlankLines(string text, string eol)
{
    var result = new List<string>();
    foreach (var line in text.Split(eol))
    {
        if (line.Trim().Length == 0 && result.Count > 0 && result[^1].Trim().Length == 0)
            continue;
        result.Add(line);
    }
    return string.Join(eol, result).TrimEnd() + eol;
}
