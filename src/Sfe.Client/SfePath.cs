using System;
using System.Linq;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// Paths of the API: relative to the library's root, with <c>/</c> between names, <c>""</c> for the root. Never an
/// operating system path: these helpers don't look at any disk.
/// </summary>
public static class SfePath
{
    /// <summary>The root of the library.</summary>
    public const string Root = "";

    /// <summary>
    /// The path without empty names: no <c>/</c> at either end, none doubled (<c>"/Photos//2026/"</c> is
    /// <c>"Photos/2026"</c>). A <c>\</c> is left as it is: it is a name character on a Linux server.
    /// </summary>
    /// <param name="path">A path of the API; null reads as the root.</param>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return Root;
        return string.Join("/", path!.Split('/').Where(name => name.Length > 0));
    }

    /// <summary>The paths and names given, joined with <c>/</c> (<c>Combine("Photos", "2026/a.jpg")</c>).</summary>
    /// <param name="parts">Paths or names, in order; empty ones are skipped.</param>
    public static string Combine(params string?[] parts)
    {
        if (parts is null)
            throw new ArgumentNullException(nameof(parts));
        return Normalize(string.Join("/", parts.Where(part => !string.IsNullOrEmpty(part))));
    }

    /// <summary>The folder holding <paramref name="path"/>: <c>""</c> (the root) for a top-level item.</summary>
    /// <param name="path">A path of the API.</param>
    public static string Parent(string path)
    {
        var normalized = Normalize(path);
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? Root : normalized.Substring(0, slash);
    }

    /// <summary>The last name of <paramref name="path"/>: <c>"a.jpg"</c> for <c>"Photos/a.jpg"</c>.</summary>
    /// <param name="path">A path of the API.</param>
    public static string Name(string path)
    {
        var normalized = Normalize(path);
        return normalized.Substring(normalized.LastIndexOf('/') + 1);
    }
}
