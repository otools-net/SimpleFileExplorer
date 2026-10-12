namespace Sfe.Core.Tagging;

/// <param name="Slug">Value stored in <see cref="Data.Tag.Color"/>.</param>
/// <param name="Hex">Background color; the client picks a readable text color for it.</param>
public sealed record TagColor(string Slug, string Name, string Hex);

/// <summary>Tag palette: TagStudio's "standard" colors.</summary>
public static class TagColors
{
    public static IReadOnlyList<TagColor> All { get; } =
    [
        new("red", "Red", "#E22C3C"),
        new("red-orange", "Red Orange", "#E83726"),
        new("orange", "Orange", "#ED6022"),
        new("amber", "Amber", "#FA9A2C"),
        new("yellow", "Yellow", "#FFD63D"),
        new("lime", "Lime", "#92E649"),
        new("green", "Green", "#45D649"),
        new("teal", "Teal", "#22D589"),
        new("cyan", "Cyan", "#3DDBDB"),
        new("blue", "Blue", "#3B87F0"),
        new("indigo", "Indigo", "#874FF5"),
        new("purple", "Purple", "#BB4FF0"),
        new("pink", "Pink", "#FF62AF"),
        new("magenta", "Magenta", "#F64680"),
    ];

    public static bool IsDefined(string slug) => All.Any(c => c.Slug == slug);
}
