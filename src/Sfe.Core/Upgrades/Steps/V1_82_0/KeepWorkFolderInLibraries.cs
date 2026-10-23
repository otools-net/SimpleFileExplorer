using Sfe.Core.Options;
using Sfe.Core.Settings;

namespace Sfe.Core.Upgrades.Steps.V1_82_0;

/// <summary>
/// 1.82.0: the work folder (the trash, unfinished uploads) is on the server by default, no longer in the library
/// (<see cref="FileManagementOptions.WorkFolder"/>). The libraries of an earlier install that took the default keep
/// theirs in the library, as their own setting: their trash stays a rename on their own drive, however large the file.
/// New installs and libraries created later get the server's.
/// </summary>
/// <remarks>
/// Idempotent: a library that has the setting is left alone. With <c>Sfe:Database:MigrateOnStartup</c> off (steps run
/// at every start) a file beside the settings file (<see cref="MarkerName"/>) tells it ran, so that a library created
/// after the update gets the new default too.
/// </remarks>
public sealed class KeepWorkFolderInLibraries : IUpgradeStep
{
    public const string Key = "files.workFolder";

    public const string MarkerName = "work-folder-kept.txt";

    public SemanticVersion Version { get; } = new(1, 82, 0);

    public string Description => "Keeping the work folder of the existing libraries in the library (new ones get the server's)";

    public async Task RunAsync(UpgradeContext context, CancellationToken cancellationToken)
    {
        if (context.Settings is not { } settings || context.TenantIds is not { } tenants)
            return;

        var marker = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settings.Server.FilePath))!, MarkerName);
        if (File.Exists(marker))
            return;

        var library = SettingKeys.Flatten(SettingKeys.ToJson(new EditableSettings { Files = new FileSettings { WorkFolder = WorkFolderLocation.Library } }))[Key]!;
        var kept = await settings.KeepPreviousDefaultAsync(Key, library, tenants, cancellationToken);
        if (kept > 0)
            context.Note($"{kept} libraries keep their work folder in the library (Files › Where SFE keeps its work files).");

        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        await File.WriteAllTextAsync(marker,
            $"SFE {Version}: the libraries of the install kept their work folder in the library; new ones get the server's default. Delete this file to run it again.{Environment.NewLine}",
            cancellationToken);
    }
}
