using Microsoft.Extensions.Options;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Core.Files;

/// <summary>
/// Where file management keeps its own files (the trash, uploads not finished yet), per library
/// (agents/docs/features/work-folder.md): a hidden folder at the root (<see cref="Name"/>), so that moving a file
/// there is a rename on the same drive, or a folder beside the thumbnails (<see cref="WorkFolderLocation.Server"/>).
/// The name at the root is reserved whatever the setting: <see cref="LibraryPaths.TryResolve"/> refuses it, listings
/// and scans leave it out, even with hidden items shown.
/// </summary>
public sealed class WorkFolder(LibraryPaths paths, IOptionsMonitor<SfeOptions> options)
{
    public const string Name = LibraryPaths.WorkFolderName;

    /// <summary>The folder in the server's data folder (<see cref="ThumbnailOptions.CacheDirectory"/>), for <see cref="WorkFolderLocation.Server"/>.</summary>
    public const string ServerFolderName = "work";

    public WorkFolderLocation Location => options.CurrentValue.Files.WorkFolder;

    /// <summary>The work folder of the library, where the setting puts it (not created).</summary>
    public string FullPath => Location == WorkFolderLocation.Server ? ServerPath : LibraryPath;

    /// <summary>The work folder at the library's root.</summary>
    public string LibraryPath => Path.Combine(paths.Root, Name);

    /// <summary>The work folder beside the thumbnails.</summary>
    public string ServerPath => Path.Combine(options.CurrentValue.Thumbnails.CacheDirectory, ServerFolderName);

    /// <summary>
    /// The sub-folder <paramref name="part"/> (<c>trash</c>, <c>uploads</c>) of the work folder, created when missing;
    /// the work folder at the root gets the hidden attribute on Windows (on Linux its dot hides it).
    /// </summary>
    public string Get(string part)
    {
        var root = FullPath;
        if (!Directory.Exists(root))
        {
            var created = Directory.CreateDirectory(root);
            if (Location == WorkFolderLocation.Library && OperatingSystem.IsWindows())
                created.Attributes |= FileAttributes.Hidden;
        }
        return Directory.CreateDirectory(Path.Combine(root, part)).FullName;
    }

    /// <summary><paramref name="fullPath"/> is one of the work folders (either location) or inside it.</summary>
    public bool Contains(string fullPath) =>
        LibraryPaths.IsSameOrInside(fullPath, LibraryPath) || LibraryPaths.IsSameOrInside(fullPath, ServerPath);
}
