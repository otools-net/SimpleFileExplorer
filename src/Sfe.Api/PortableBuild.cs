using System.Reflection;

namespace Sfe.Api;

/// <summary>
/// Portable builds (<c>-p:SfePortable=true</c>, made by <c>build/</c>) keep their settings, web files and data folder
/// next to the executable. The host would otherwise look for them in the working directory, which is wherever the
/// app was started from. Other builds (<c>dotnet run</c>, tests, IIS) keep the host defaults.
/// </summary>
internal static class PortableBuild
{
    public static bool IsPortable { get; } = typeof(PortableBuild).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(a => a.Key == "SfePortable" && a.Value == "true");

    public static WebApplicationOptions CreateOptions(string[] args)
    {
        if (!IsPortable)
            return new WebApplicationOptions { Args = args };

        // A content root given on the command line or in the environment still wins: the AppImage runs from a
        // read-only image and passes its writable data folder (~/.config/sfe) that way.
        var givenRoot = new ConfigurationBuilder()
            .AddEnvironmentVariables("DOTNET_")
            .AddEnvironmentVariables("ASPNETCORE_")
            .AddCommandLine(args)
            .Build()[HostDefaults.ContentRootKey];

        return new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = string.IsNullOrEmpty(givenRoot) ? AppContext.BaseDirectory : null,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        };
    }
}
