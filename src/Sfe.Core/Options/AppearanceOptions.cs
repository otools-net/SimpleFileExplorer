namespace Otools.Net.Sfe.Core.Options;

/// <summary>The web client's look.</summary>
public sealed class AppearanceOptions
{
    /// <summary>
    /// Theme of the browsers that have not picked one in Administration › Interface › Appearance (that choice is kept
    /// per browser and wins).
    /// </summary>
    public Theme Theme { get; set; } = Theme.System;
}
