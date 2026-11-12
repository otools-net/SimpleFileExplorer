using Sfe.Core.Library;

namespace Sfe.Api.Endpoints;

/// <param name="Path">Relative to the library root; empty for the root.</param>
/// <param name="Name">The folder's or file's name (the library's name for the root).</param>
/// <param name="Kind">What it was when added: <c>directory</c>, or the kind of file by its extension.</param>
/// <param name="Exists">Still there: false once it was moved, renamed or deleted.</param>
public sealed record QuickLinkView(string Path, string Name, ItemKind Kind, string? Extension, bool Exists);

/// <param name="Paths">Folders and files to add, relative to the library root, in order.</param>
/// <param name="Position">
/// Where they go: before the link at this place (0-based) of the list. Links already in the list move there. Without
/// it, new links go at the end and the others stay where they are.
/// </param>
public sealed record QuickLinkAdd(IReadOnlyList<string>? Paths, int? Position);
