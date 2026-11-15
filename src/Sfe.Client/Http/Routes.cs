using System;
using System.Linq;

namespace Otools.Net.Sfe.Client.Http;

/// <summary>
/// The v1 routes the facade calls, relative to the server's address (so that a virtual directory is kept), with their
/// values escaped.
/// </summary>
internal static class Routes
{
    public const string Base = "api/v1/";

    /// <summary><c>api/v1/{route}</c>.</summary>
    public static string V1(string route) => Base + route;

    /// <summary><c>api/v1/{route}?path=…</c> with <paramref name="path"/> escaped.</summary>
    public static string WithPath(string route, string path) => V1($"{route}?path={Escape(path)}");

    /// <summary>
    /// <c>api/v1/{route}/a/b%20c</c>: each segment of <paramref name="path"/> escaped, the slashes kept.
    /// </summary>
    public static string WithPathSegments(string route, string path) =>
        V1($"{route}/{string.Join("/", SfePath.Normalize(path).Split('/').Select(Escape))}");

    /// <summary>A query value or path segment, escaped.</summary>
    public static string Escape(string value) => Uri.EscapeDataString(value);
}
