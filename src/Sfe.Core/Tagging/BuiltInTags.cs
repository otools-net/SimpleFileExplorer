namespace Sfe.Core.Tagging;

/// <summary>
/// Tags every library starts with, as in TagStudio: "Favorite" and "Archived" (hidden from search), both under the
/// "Meta Tags" category. They can be edited but not deleted.
/// </summary>
public static class BuiltInTags
{
    public const int Archived = 1;
    public const int Favorite = 2;
    public const int Meta = 3;

    public static bool IsBuiltIn(int tagId) => tagId is Archived or Favorite or Meta;

    // Seed data (HasData) uses anonymous objects: seeded entities cannot carry navigations.
    internal static object[] Tags =>
    [
        new { Id = Archived, Name = "Archived", Shorthand = (string?)null, Color = (string?)"red", IsCategory = false, IsHidden = true, DisambiguationId = (int?)null },
        new { Id = Favorite, Name = "Favorite", Shorthand = (string?)null, Color = (string?)"yellow", IsCategory = false, IsHidden = false, DisambiguationId = (int?)null },
        new { Id = Meta, Name = "Meta Tags", Shorthand = (string?)null, Color = (string?)null, IsCategory = true, IsHidden = false, DisambiguationId = (int?)null },
    ];

    internal static object[] Aliases =>
    [
        new { Id = 1, TagId = Archived, Name = "Archive" },
        new { Id = 2, TagId = Favorite, Name = "Favorited" },
        new { Id = 3, TagId = Favorite, Name = "Favorites" },
        new { Id = 4, TagId = Meta, Name = "Meta" },
        new { Id = 5, TagId = Meta, Name = "Meta Tag" },
    ];

    internal static object[] Parents =>
    [
        new { ParentId = Meta, ChildId = Archived },
        new { ParentId = Meta, ChildId = Favorite },
    ];
}
