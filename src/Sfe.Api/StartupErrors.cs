using System.Text.Json;

namespace Sfe.Api;

/// <summary>
/// What stops SFE from starting, said in words rather than a stack trace: a configuration file that isn't valid JSON,
/// a port another program uses. A portable package started by a double click keeps its window open to show it.
/// </summary>
internal static class StartupErrors
{
    /// <summary>A configuration file failed to load (<see cref="InvalidDataException"/> from the configuration).</summary>
    public static int ConfigurationFile(InvalidDataException error)
    {
        var json = error.InnerException as JsonException ?? error.InnerException?.InnerException as JsonException;
        var where = json?.LineNumber is { } line ? $" (line {line + 1}, position {json.BytePositionInLine + 1})" : "";
        return Report($"""
            SFE cannot start: {error.Message}
            The file is not valid JSON{where}. A missing or extra comma, quote or brace is the usual cause;
            in a Windows path, write each \ as \\ (e.g. "D:\\Media").
            Fix it, or rename the file to start again from the defaults.
            """);
    }

    /// <summary>The listening address is taken.</summary>
    public static int AddressInUse(IOException error) => Report($"""
        SFE cannot start: {error.Message}
        Another program (or another copy of SFE) uses this port. Close it, or start SFE on another port,
        e.g. Sfe.Api --urls http://localhost:5081
        """);

    private static int Report(string message)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(message);
        Console.Error.WriteLine();
        // Started by a double click, the window would close before anyone reads it.
        if (PortableBuild.IsPortable && OperatingSystem.IsWindows() && Environment.UserInteractive && !Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Press a key to close this window.");
            Console.ReadKey(intercept: true);
        }

        return 1;
    }
}
