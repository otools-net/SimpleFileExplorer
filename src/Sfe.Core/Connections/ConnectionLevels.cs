using Sfe.Core.Options;

namespace Sfe.Core.Connections;

/// <summary>The levels as <see cref="ConnectionOptions.Levels"/> names them.</summary>
public static class ConnectionLevels
{
    public static string Name(ConnectionLevel level) =>
        level switch
        {
            ConnectionLevel.Account => ConnectionOptions.AccountLevel,
            ConnectionLevel.Library => ConnectionOptions.LibraryLevel,
            ConnectionLevel.Server => ConnectionOptions.ServerLevel,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
        };

    /// <summary>
    /// The settings let connections of <paramref name="level"/> be made and used (those made before the level was
    /// taken out stay, unusable).
    /// </summary>
    public static bool IsAllowed(ConnectionLevel level, ConnectionOptions options) =>
        Enum.IsDefined(level) && options.Levels.Contains(Name(level), StringComparer.OrdinalIgnoreCase);

    /// <summary>Connections of this kind and level can be used now (or made, with a sign-in method).</summary>
    public static bool IsUsable(string kind, ConnectionLevel level, ConnectionOptions options) =>
        ConnectionProviders.IsAllowed(kind, options) && IsAllowed(level, options);
}
