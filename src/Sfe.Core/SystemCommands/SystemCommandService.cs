using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Options;

namespace Sfe.Core.SystemCommands;

/// <summary>Host power/drive commands exposed to the UI.</summary>
/// <remarks>Commands run through the system shell: <c>cmd.exe /c</c> on Windows, <c>/bin/sh -c</c> elsewhere.</remarks>
public sealed class SystemCommandService(IOptions<SfeOptions> options, ILogger<SystemCommandService> logger)
{
    /// <summary>Forced shutdown a few seconds from now, so the HTTP response can still be sent.</summary>
    public const string WindowsShutdownCommand = "shutdown /s /f /t 5";

    /// <summary>Shutdown in one minute (the smallest delay <c>shutdown</c> accepts besides "now"); <c>shutdown -c</c> cancels it.</summary>
    public const string UnixShutdownCommand = "shutdown -h +1";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    private readonly SystemCommandOptions _options = options.Value.System;

    public bool CanShutdown => _options.EnableShutdown && !string.IsNullOrWhiteSpace(ShutdownCommand);
    public bool CanDismount => !string.IsNullOrWhiteSpace(_options.DismountCommand);

    private string ShutdownCommand => string.IsNullOrWhiteSpace(_options.ShutdownCommand)
        ? OperatingSystem.IsWindows() ? WindowsShutdownCommand : UnixShutdownCommand
        : _options.ShutdownCommand;

    public Task<string> ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (!CanShutdown)
            throw new InvalidOperationException("Shutdown is disabled.");

        logger.LogWarning("Shutdown requested: {Command}", ShutdownCommand);
        return RunShellAsync(ShutdownCommand, cancellationToken);
    }

    public Task<string> DismountAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDismount)
            throw new InvalidOperationException("Dismount is disabled.");

        logger.LogWarning("Dismount requested: {Command}", _options.DismountCommand);
        return RunShellAsync(_options.DismountCommand!, cancellationToken);
    }

    /// <returns>The command output (stdout then stderr).</returns>
    private static async Task<string> RunShellAsync(string command, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            // cmd.exe parses its own command line: pass it verbatim rather than quoted as a single argument.
            start.FileName = "cmd.exe";
            start.Arguments = "/c " + command;
        }
        else
        {
            start.FileName = "/bin/sh";
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(command);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {start.FileName}.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        // Both streams are drained concurrently: reading one to the end first can deadlock on a full pipe,
        // and would wait for the command to finish before the timeout even starts.
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = (await stdout).Trim();
            var error = (await stderr).Trim();
            if (process.ExitCode != 0)
                error = $"{error}\n(exit code {process.ExitCode})".Trim();
            return string.IsNullOrEmpty(error) ? output : $"{output}\n{error}".Trim();
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (cancellationToken.IsCancellationRequested)
                throw;
            return "(timed out)";
        }
    }
}
