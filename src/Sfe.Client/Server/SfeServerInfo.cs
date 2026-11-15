using System;
using System.Collections.Generic;
using System.Linq;
using Otools.Net.Sfe.Client.Server;

namespace Otools.Net.Sfe.Client;

/// <summary>What the server tells the caller about itself: version, mode, the caller's rights and libraries.</summary>
public sealed class SfeServerInfo
{
    private readonly HashSet<SfeFunction> _functions;

    internal SfeServerInfo(ConfigAnswer config)
    {
        Version = config.Version;
        Commit = config.Commit;
        Mode = config.Mode;
        Rights = config.Rights;
        Library = config.Tenant;
        Libraries = config.Tenants;
        _functions = FunctionsOf(config);
        ConnectionKinds = (config.Connections?.Kinds ?? [])
            .Where(kind => kind.Capabilities is not null)
            .GroupBy(kind => kind.Kind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Capabilities!, StringComparer.Ordinal);
    }

    /// <summary>The server's version (<c>1.85.1</c>).</summary>
    public string Version { get; }

    /// <summary>The commit it was built from, when it says.</summary>
    public string? Commit { get; }

    /// <summary>Whether it answers its own computer only or others too.</summary>
    public SfeServerMode Mode { get; }

    /// <summary>The rights of the caller (the token's, within its account's) in the library it works in.</summary>
    public IReadOnlyList<string> Rights { get; }

    /// <summary>The library the calls work in; null when the caller reaches none.</summary>
    public SfeLibrary? Library { get; }

    /// <summary>The libraries the caller reaches.</summary>
    public IReadOnlyList<SfeLibrary> Libraries { get; }

    /// <summary>The server's version as a number, when it reads as one (a pre-release keeps its numbers).</summary>
    public Version? ParsedVersion =>
        System.Version.TryParse(Version.Split('-', '+')[0], out var parsed) ? parsed : null;

    /// <summary>The capabilities of the providers of the connection kinds offered, by kind.</summary>
    internal IReadOnlyDictionary<string, SfeConnectionCapabilities> ConnectionKinds { get; }

    /// <summary>Whether the server offers <paramref name="function"/> to the caller.</summary>
    /// <param name="function">The function asked for.</param>
    public bool Supports(SfeFunction function) => _functions.Contains(function);

    /// <summary>Whether the caller has <paramref name="right"/> (<c>files.upload</c>…) in its library.</summary>
    /// <param name="right">A right's id, as the API names it.</param>
    public bool HasRight(string right) => Rights.Contains(right, StringComparer.Ordinal);

    private static HashSet<SfeFunction> FunctionsOf(ConfigAnswer config)
    {
        var functions = new HashSet<SfeFunction>();
        if (config.Features?.Thumbnails == true)
            functions.Add(SfeFunction.Thumbnails);
        if (config.Features?.Trickplay == true)
            functions.Add(SfeFunction.Trickplay);
        if (config.Connections is not null)
            functions.Add(SfeFunction.Connections);
        if (config.AccessTokens?.Enabled == true)
            functions.Add(SfeFunction.AccessTokens);
        return functions;
    }
}
