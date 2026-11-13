#:package Microsoft.CodeAnalysis.CSharp@5.0.0
#:property PublishAot=false
// Moves the long OpenAPI descriptions of route maps into fields (readability plan, phase 5: a Map… method lists routes,
// long descriptions are constants by their routes): each `.WithDescription(<raw string>)` gets a field named after the
// route's `.WithName("X")` (`XDescription`), just before the method that maps it; `const` when the string has no
// holes, `static readonly` when it is interpolated. The string's lines move together, so its value is unchanged. Run
// CSharpier after it. See ../README.md.
//
//   dotnet run agents/tools/readability/extract-descriptions/ExtractDescriptions.cs -- <file>...
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: ExtractDescriptions.cs <file>...");
    return 2;
}
var total = 0;
foreach (var path in args)
    total += Extract(path);
Console.WriteLine($"{total} description(s) moved into fields");
return 0;

int Extract(string path)
{
    var source = File.ReadAllText(path);
    var eol = source.Contains("\r\n") ? "\r\n" : "\n";
    var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
    // Each description argument over several lines (a raw string, or strings joined by +), with its route's name and
    // its method.
    var found = root.DescendantNodes()
        .OfType<InvocationExpressionSyntax>()
        .Where(call => call.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "WithDescription" })
        .Select(call => (call, literal: call.ArgumentList.Arguments.SingleOrDefault()?.Expression))
        .Where(x => (IsRawString(x.literal) || IsConcatenation(x.literal)) && x.literal!.ToString().Contains('\n'))
        .Select(x =>
            (x.call, x.literal, name: RouteName(x.call), method: x.call.FirstAncestorOrSelf<MethodDeclarationSyntax>())
        )
        .Where(x => x.name is not null && x.method is not null && x.method.Identifier.Text.StartsWith("Map"))
        .ToList();
    if (found.Count == 0)
        return 0;

    // Edits from the end of the file, so that earlier offsets stay right.
    var edits = new List<(int Start, int End, string Text)>();
    foreach (var group in found.GroupBy(x => x.method!))
    {
        var methodStart = group.Key.GetFirstToken().GetPreviousToken().Span.End;
        var insertAt = source.IndexOf('\n', methodStart) + 1;
        var methodIndent = Indent(source, group.Key.SpanStart);
        var fields =
            eol
            + string.Concat(
                group.Select(x =>
                {
                    var interpolated = x.literal!.DescendantNodesAndSelf()
                        .OfType<InterpolatedStringExpressionSyntax>()
                        .Any();
                    var declaration =
                        $"{methodIndent}private {(interpolated ? "static readonly" : "const")} string "
                        + $"{x.name}Description = ";
                    return declaration + Reindent(source, x.literal!, methodIndent + "    ", eol) + ";" + eol + eol;
                })
            );
        edits.Add((insertAt, insertAt, fields));
        foreach (var x in group)
            edits.Add((x.literal!.SpanStart, x.literal.Span.End, x.name + "Description"));
    }
    var result = source;
    foreach (var (start, end, text) in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.End))
        result = result[..start] + text + result[end..];
    File.WriteAllText(path, result);
    Console.WriteLine($"{path}: {found.Count}");
    return found.Count;
}

/** Strings joined by `+` (`"a " + "b"`, as split-strings.mjs writes long ones). */
static bool IsConcatenation(ExpressionSyntax? expression) =>
    expression is BinaryExpressionSyntax add
    && add.IsKind(SyntaxKind.AddExpression)
    && IsString(add.Left)
    && IsString(add.Right);

static bool IsString(ExpressionSyntax expression) =>
    expression.IsKind(SyntaxKind.StringLiteralExpression)
    || expression is InterpolatedStringExpressionSyntax
    || IsConcatenation(expression);

static bool IsRawString(ExpressionSyntax? expression) =>
    expression switch
    {
        LiteralExpressionSyntax literal => literal.Token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken),
        InterpolatedStringExpressionSyntax interpolated => interpolated.StringStartToken.IsKind(
            SyntaxKind.InterpolatedMultiLineRawStringStartToken
        ),
        _ => false,
    };

/** The `.WithName("X")` of the same chain of calls, before or after the description. */
static string? RouteName(InvocationExpressionSyntax description)
{
    var chainTop = description;
    while (chainTop.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax next })
        chainTop = next;
    return chainTop
        .DescendantNodesAndSelf()
        .OfType<InvocationExpressionSyntax>()
        .Where(call => call.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "WithName" })
        .Select(call => call.ArgumentList.Arguments.SingleOrDefault()?.Expression)
        .OfType<LiteralExpressionSyntax>()
        .Select(literal => literal.Token.ValueText)
        .FirstOrDefault();
}

static string Indent(string source, int position)
{
    var lineStart = source.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
    var i = lineStart;
    while (i < source.Length && source[i] == ' ')
        i++;
    return source[lineStart..i];
}

/**
 * The raw string's text with its lines moved so that its closing quotes sit at <paramref name="indent"/>: every line
 * after the first moves by the same amount, which keeps the string's value.
 */
static string Reindent(string source, ExpressionSyntax literal, string indent, string eol)
{
    var lines = literal.ToString().Split(eol);
    var closing = lines[^1];
    var oldIndent = closing.Length - closing.TrimStart().Length;
    var delta = indent.Length - oldIndent;
    return string.Join(
        eol,
        lines.Select(
            (line, i) =>
                i == 0 || line.Trim().Length == 0 && i < lines.Length - 1 ? line.TrimEnd()
                : delta >= 0 ? new string(' ', delta) + line
                : line[Math.Min(-delta, line.Length - line.TrimStart().Length)..]
        )
    );
}
