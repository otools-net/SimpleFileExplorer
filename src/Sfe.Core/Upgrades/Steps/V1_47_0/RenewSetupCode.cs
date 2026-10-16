using Microsoft.Extensions.Logging;

namespace Sfe.Core.Upgrades.Steps.V1_47_0;

/// <summary>
/// 1.47.0: the setup code the server made itself is renewed. Earlier versions printed it through the logger, so it may
/// be in the server's log (1.46.0 keeps the log in the database and in daily files, which backups hold); from 1.47.0 it
/// is only written to the console. The file is deleted: the next time a code is needed (an update, a recovery), a new
/// one is made, printed and kept in the file. A configured code (<c>Sfe:Setup:Code</c>) is the administrator's own
/// and is not touched.
/// </summary>
/// <remarks>
/// Idempotent: with nothing in the file, nothing happens. With <c>Sfe:Database:MigrateOnStartup</c> off (steps run at
/// every start) the generated code changes at every start, when only a recovery needs it, which prints it at start-up.
/// </remarks>
public sealed class RenewSetupCode : IUpgradeStep
{
    public SemanticVersion Version { get; } = new(1, 47, 0);

    public string Description => "Renewing the setup code the server made itself (earlier versions logged it)";

    public Task RunAsync(UpgradeContext context, CancellationToken cancellationToken)
    {
        if (context.GeneratedCode is not { } code)
            return Task.CompletedTask;

        try
        {
            if (code.Renew())
                context.Note($"The setup code changes: a new one will be shown in the server's console and kept in {code.FilePath} when needed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth failing the update over: the code can still be changed by deleting the file.
            context.Logger.LogWarning(ex, "Cannot delete the setup code file {Path}: delete it so that a new code is made", code.FilePath);
            context.Note($"Could not renew the setup code: delete {code.FilePath} so that a new one is made.");
        }

        return Task.CompletedTask;
    }
}
