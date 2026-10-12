using System.Reflection;

namespace Sfe.Api;

/// <summary>
/// Version of the running application, from <c>Directory.Build.props</c> (or a build's <c>-p:Version</c>). The SDK
/// writes it with the commit built into the informational version: <c>1.2.0+&lt;sha&gt;</c>.
/// </summary>
internal static class AppVersion
{
    private static readonly string Informational = typeof(AppVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>Semantic version without the build metadata, e.g. <c>1.2.0</c> or <c>1.3.0-beta.1</c>.</summary>
    public static string Version { get; } = Informational.Split('+')[0];

    /// <summary>Short hash of the commit built, when the build knew it.</summary>
    public static string? Commit { get; } = Informational.Split('+') is [_, var sha, ..] && sha.Length > 0
        ? sha[..Math.Min(7, sha.Length)]
        : null;
}
