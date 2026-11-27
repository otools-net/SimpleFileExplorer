using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Access;

/// <summary>
/// What a new space (tenant) is made of: its name and short name checked (the rules of
/// <see cref="AccessService.CreateTenantAsync"/>), and the space itself with its built-in groups.
/// </summary>
internal static partial class NewTenants
{
    /// <summary>
    /// The name and short name <paramref name="input"/> asks for, checked; a short name made from the name is made
    /// unique (“family-2”), one typed must be free.
    /// </summary>
    public static async Task<AccessResult<(string Name, string Slug)>> CheckAsync(
        AccessDbContext db,
        TenantInput input,
        CancellationToken cancellationToken
    )
    {
        var name = input.Name?.Trim();
        if (NameError(name) is { } nameError)
            return AccessResult<(string, string)>.Invalid("name", nameError);

        var slug = string.IsNullOrWhiteSpace(input.Slug) ? Slugify(name!) : input.Slug.Trim().ToLowerInvariant();
        if (!SlugRegex().IsMatch(slug))
        {
            return AccessResult<(string, string)>.Invalid(
                "slug",
                "The short name can only have lowercase letters, digits and “-” (at most 64), "
                    + "starting with a letter or digit."
            );
        }

        if (string.IsNullOrWhiteSpace(input.Slug))
            slug = await UniqueSlugAsync(db, slug, cancellationToken);
        else if (await db.Tenants.AnyAsync(t => t.Slug == slug, cancellationToken))
            return AccessResult<(string, string)>.Invalid("slug", $"Another space has the short name “{slug}”.");

        if (DisabledRightsError(input.DisabledRights) is { } rightsError)
            return AccessResult<(string, string)>.Invalid("disabledRights", rightsError);
        return AccessResult<(string, string)>.Ok((name!, slug));
    }

    /// <summary>Why <paramref name="name"/> (trimmed) can't name a space; null when it can.</summary>
    public static string? NameError(string? name) =>
        string.IsNullOrEmpty(name) || name.Length > AccessService.MaxNameLength
            ? $"A space needs a name of at most {AccessService.MaxNameLength} characters."
            : null;

    /// <summary>Why <paramref name="rights"/> can't be switched off in a space; null when they can.</summary>
    public static string? DisabledRightsError(IReadOnlyList<string>? rights) =>
        rights?.FirstOrDefault(r => !RightCatalog.IsTenantRight(r)) is { } unknown
            ? $"“{unknown}” is not a right of a space."
            : null;

    /// <summary>A library with its three built-in groups, granted the catalog's default rights.</summary>
    public static Tenant Create(string name, string slug) =>
        new()
        {
            Name = name,
            Slug = slug,
            Created = DateTime.UtcNow,
            RightsRevision = RightCatalog.Revision,
            Groups = Enum.GetValues<BuiltInGroup>()
                .Select(builtIn => new UserGroup
                {
                    Name = builtIn.ToString(),
                    BuiltIn = builtIn,
                    Rights = RightCatalog
                        .TenantRights.Where(r => r.DefaultGroups.Contains(builtIn))
                        .Select(r => new GroupRight { Right = r.Id })
                        .ToList(),
                })
                .ToList(),
        };

    /// <summary>"Family Photos (2020)" → "family-photos-2020"; accents dropped, other letters left out.</summary>
    public static string Slugify(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
            }
            else if (
                builder.Length > 0
                && builder[^1] != '-'
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark
            )
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        slug = slug.Length > 64 ? slug[..64].TrimEnd('-') : slug;
        return slug.Length == 0 ? "library" : slug;
    }

    private static async Task<string> UniqueSlugAsync(
        AccessDbContext db,
        string slug,
        CancellationToken cancellationToken
    )
    {
        var candidate = slug;
        for (var n = 2; await db.Tenants.AnyAsync(t => t.Slug == candidate, cancellationToken); n++)
            candidate = $"{slug[..Math.Min(slug.Length, 60)]}-{n}";
        return candidate;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex SlugRegex();
}
