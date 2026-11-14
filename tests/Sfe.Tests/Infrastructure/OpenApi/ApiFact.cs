namespace Sfe.Tests.Infrastructure.OpenApi;

/// <summary>
/// One promise of the API read from its OpenAPI document (<see cref="ApiContract"/>): a route, a parameter, a body, a
/// status, the rights of a route, a schema, a property, an enum's words, the problem <c>type</c> words.
/// </summary>
/// <param name="Kind">What it is (<see cref="FactKind"/>).</param>
/// <param name="Required">A parameter, body or property that must be there.</param>
/// <param name="Types">
/// The JSON types it may have, each as <c>type:format</c>, <c>ref:Schema</c>, <c>array&lt;…&gt;</c>.
/// </param>
/// <param name="Default">The default value, as JSON text.</param>
/// <param name="Words">An enum's words, the problem type words, or the rights (one entry per requirement, its rights
/// joined by <c>|</c>).</param>
/// <param name="Flow">Whether clients send it, receive it, or both (a property of a schema used both ways).</param>
public sealed record ApiFact(
    FactKind Kind,
    bool Required = false,
    IReadOnlyList<string>? Types = null,
    string? Default = null,
    IReadOnlyList<string>? Words = null,
    Flow Flow = Flow.Both
)
{
    /// <summary>The fact as one line, to compare a document with its baseline and to show what differs.</summary>
    public string Text =>
        string.Join(
            " ",
            new[]
            {
                Kind.ToString(),
                Required ? "required" : null,
                Types is null ? null : "types=" + string.Join("|", Types),
                Default is null ? null : "default=" + Default,
                Words is null ? null : "words=" + string.Join(",", Words),
            }.Where(part => part is not null)
        );

    public bool Equals(ApiFact? other) => other is not null && Text == other.Text && Flow == other.Flow;

    public override int GetHashCode() => HashCode.Combine(Text, Flow);
}
