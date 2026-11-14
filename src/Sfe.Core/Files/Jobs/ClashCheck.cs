namespace Sfe.Core.Files;

/// <summary>An item about to take its name in a folder of the library (<see cref="LibraryClashes"/>).</summary>
internal sealed record ClashCheck
{
    /// <summary>The path the item's result is reported under.</summary>
    public required string Path { get; init; }

    /// <summary>The item's full path now.</summary>
    public required string Source { get; init; }

    /// <summary>The item's place in the library, if it has one (an extracted item has none).</summary>
    public string? SourcePath { get; init; }

    public required bool IsFolder { get; init; }

    /// <summary>The folder it goes to, in the library.</summary>
    public required string TargetPath { get; init; }

    /// <summary>The folder it goes to, full path.</summary>
    public required string TargetFull { get; init; }

    /// <summary>The name it would take.</summary>
    public required string Name { get; init; }

    /// <summary>Keep both without asking (a copy in its own folder).</summary>
    public bool KeepBoth { get; init; }
}
