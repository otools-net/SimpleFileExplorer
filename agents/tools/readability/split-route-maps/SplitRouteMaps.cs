#:package Microsoft.CodeAnalysis.CSharp@5.0.0
#:property PublishAot=false
// Splits a long `Map…Endpoints(this RouteGroupBuilder api)` into one method per route group (readability plan, phase
// 5): its statements are cut at each `var x = api.MapGroup("/path")…;`, each part moved as it is into
// `Map<X>(RouteGroupBuilder api)` just after it, called in the same order (registration order is kept). A map with
// fewer than two groups is reported and left (by hand). Run CSharpier after it. See ../README.md.
//
//   dotnet run agents/tools/readability/split-route-maps/SplitRouteMaps.cs -- [--min <lines>] <file>...
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var minAt = Array.IndexOf(args, "--min");
var minLines = minAt >= 0 ? int.Parse(args[minAt + 1]) : 61;
var paths = args.Where((a, i) => a != "--min" && (minAt < 0 || i != minAt + 1)).ToList();
var total = 0;
foreach (var path in paths)
    total += Split(path);
Console.WriteLine($"{total} map(s) split");
return 0;

int Split(string path)
{
    var source = File.ReadAllText(path);
    var eol = source.Contains("\r\n") ? "\r\n" : "\n";
    var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
    var maps = root.DescendantNodes()
        .OfType<MethodDeclarationSyntax>()
        .Where(m =>
            m.Identifier.Text.StartsWith("Map") && m.Identifier.Text.EndsWith("Endpoints") && m.Body is not null
        )
        .Where(m => Lines(m) >= minLines)
        .ToList();
    var existing = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Select(m => m.Identifier.Text).ToHashSet();
    var edits = new List<(int Start, int End, string Text)>();
    foreach (var map in maps)
    {
        var statements = map.Body!.Statements;
        var starts = statements.Select((s, i) => (s, i)).Where(x => GroupOf(x.s) is not null).Select(x => x.i).ToList();
        if (starts.Count < 2)
        {
            Console.Error.WriteLine($"{path}: {map.Identifier.Text} has {starts.Count} group(s): by hand");
            continue;
        }
        var end = statements.Count - (statements.Last() is ReturnStatementSyntax ? 1 : 0);
        var indent = new string(' ', map.GetLeadingTrivia().ToString().Split('\n').Last().Length);
        var calls = new List<string>();
        var methods = new List<string>();
        for (var c = 0; c < starts.Count; c++)
        {
            var from = starts[c];
            var to = c + 1 < starts.Count ? starts[c + 1] : end;
            var (variable, route) = GroupOf(statements[from])!.Value;
            var name = "Map" + char.ToUpperInvariant(variable[0]) + variable[1..];
            if (existing.Contains(name) || methods.Any(m => m.Contains($" {name}(")))
                name += "Routes";
            calls.Add($"{indent}    {name}(api);");
            var body = string.Concat(statements.Skip(from).Take(to - from).Select(s => s.ToFullString()));
            methods.Add(
                $"{eol}{indent}/// <summary>The routes under <c>{route}</c>.</summary>{eol}"
                    + $"{indent}private static void {name}(RouteGroupBuilder api){eol}{indent}{{{eol}"
                    + body.TrimStart('\r', '\n').TrimEnd()
                    + $"{eol}{indent}}}{eol}"
            );
        }
        // The map keeps what comes before the first group, then calls the parts, then returns.
        var head = string.Concat(statements.Take(starts[0]).Select(s => s.ToFullString()));
        var newBody =
            $"{{{eol}{head.TrimStart('\r', '\n')}{(head.Trim().Length > 0 ? eol : "")}"
            + string.Join(eol, calls)
            + $"{eol}{indent}    return api;{eol}{indent}}}";
        edits.Add((map.Body.SpanStart, map.Body.Span.End, newBody + eol + string.Concat(methods).TrimEnd()));
        Console.WriteLine($"{path}: {map.Identifier.Text} -> {calls.Count} parts");
    }
    if (edits.Count == 0)
        return 0;
    var result = source;
    foreach (var (start, endAt, text) in edits.OrderByDescending(e => e.Start))
        result = result[..start] + text + result[endAt..];
    File.WriteAllText(path, result);
    return edits.Count;
}

static int Lines(SyntaxNode node) =>
    node.GetLocation().GetLineSpan() is var s ? s.EndLinePosition.Line - s.StartLinePosition.Line + 1 : 0;

/** `var x = api.MapGroup("/path")…;`: its variable and route; null for any other statement. */
static (string Variable, string Route)? GroupOf(StatementSyntax statement)
{
    if (statement is not LocalDeclarationStatementSyntax { Declaration.Variables: [var variable] })
        return null;
    var mapGroup = variable
        .Initializer?.Value.DescendantNodesAndSelf()
        .OfType<InvocationExpressionSyntax>()
        .FirstOrDefault(call =>
            call.Expression
                is MemberAccessExpressionSyntax
                {
                    Name.Identifier.Text: "MapGroup",
                    Expression: IdentifierNameSyntax { Identifier.Text: "api" },
                }
        );
    var route = mapGroup?.ArgumentList.Arguments.FirstOrDefault()?.Expression as LiteralExpressionSyntax;
    return route is null ? null : (variable.Identifier.Text, route.Token.ValueText);
}
