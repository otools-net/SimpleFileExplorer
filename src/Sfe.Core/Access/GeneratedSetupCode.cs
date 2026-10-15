using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Sfe.Core.Options;

namespace Sfe.Core.Access;

/// <summary>
/// The setup code the server makes itself when its configuration gives none (<see cref="SetupOptions.CodeFile"/>):
/// printed in the console at start-up and kept in the data folder, so a new install needs no file edited before its
/// first start. Whoever can read the console or the data folder runs the server, which is what the code proves. A
/// configured <c>Sfe:Setup:Code</c> always wins. The file stays after the setup: the update page asks for the code too.
/// </summary>
public sealed class GeneratedSetupCode(string filePath, ILogger<GeneratedSetupCode> logger)
    : IPostConfigureOptions<SetupOptions>, IOptionsChangeTokenSource<SetupOptions>
{
    /// <summary>No 0/O, 1/I/L: people retype it from a console or a text file.</summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private const int Groups = 4;
    private const int GroupLength = 4;

    private readonly Lock _lock = new();
    private CancellationTokenSource _changed = new();

    /// <summary>Full path of the file.</summary>
    public string FilePath { get; } = filePath;

    public string? Name => Microsoft.Extensions.Options.Options.DefaultName;

    public IChangeToken GetChangeToken() => new CancellationChangeToken(_changed.Token);

    /// <summary>The code in the file; null when there is none (or it can't be read).</summary>
    public string? Read()
    {
        try
        {
            return File.Exists(FilePath) && File.ReadAllText(FilePath).Trim() is { Length: > 0 } code ? code : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Cannot read the setup code file {Path}", FilePath);
            return null;
        }
    }

    /// <summary>The code in the file, made and written first when there is none.</summary>
    /// <exception cref="IOException">The data folder is not writable.</exception>
    public string Ensure()
    {
        lock (_lock)
        {
            if (Read() is { } existing)
                return existing;

            var code = Generate();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, code + Environment.NewLine);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            // IOptionsMonitor<SetupOptions> reads the new file on its next use.
            var changed = _changed;
            _changed = new CancellationTokenSource();
            changed.Cancel();
            return code;
        }
    }

    public void PostConfigure(string? name, SetupOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Code) || Read() is not { } code)
            return;
        options.Code = code;
        options.CodeIsGenerated = true;
    }

    /// <summary>A new code: <c>XXXX-XXXX-XXXX-XXXX</c>, 16 random letters and digits.</summary>
    internal static string Generate()
    {
        var chars = RandomNumberGenerator.GetString(Alphabet, Groups * GroupLength);
        return string.Join('-', Enumerable.Range(0, Groups).Select(i => chars.Substring(i * GroupLength, GroupLength)));
    }

    /// <summary>A code as typed, without dashes, spaces and case.</summary>
    public static string Normalize(string? code) =>
        new string((code ?? string.Empty).Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
}
