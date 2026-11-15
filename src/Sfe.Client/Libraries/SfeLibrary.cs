namespace Otools.Net.Sfe.Client;

/// <summary>A library (space) of the server: a root folder with its own index, tags and rights.</summary>
public sealed class SfeLibrary
{
    /// <summary>Its id, as <see cref="SfeClientOptions.Library"/> takes it.</summary>
    public int Id { get; init; }

    /// <summary>Its short name in addresses.</summary>
    public string Slug { get; init; } = "";

    /// <summary>Its name, as people see it.</summary>
    public string Name { get; init; } = "";
}
