namespace Otools.Net.Sfe.Core.Options;

/// <summary>
/// How text files read in the viewer (the reading settings, "Aa"): the library's defaults, each account's own over
/// them, each browser's changes over those. Values outside the lists of the client fall back to its defaults.
/// </summary>
public sealed class ReadingOptions
{
    /// <summary>
    /// Allowed values of <see cref="Theme"/>: as the app (light or dark), or always light, sepia or dark.
    /// </summary>
    public static readonly IReadOnlyList<string> Themes = ["app", "light", "sepia", "dark"];

    /// <summary>Colours of the text: <c>app</c>, <c>light</c>, <c>sepia</c> or <c>dark</c>.</summary>
    public string Theme { get; set; } = "app";

    /// <summary>A font installed on the reader's device, for the font <c>custom</c> (e.g. <c>Fira Code</c>).</summary>
    public string CustomFont { get; set; } = string.Empty;

    /// <summary>Code, logs and settings files: monospace, line numbers, long lines kept whole.</summary>
    public ReadingProfileOptions Code { get; set; } =
        new()
        {
            Font = "system-mono",
            Size = 14,
            LineHeight = 1.5,
            Width = 0,
            Padding = 8,
            Wrap = false,
            LineNumbers = true,
        };

    /// <summary>Notes and documents (<c>.txt</c>, <c>.md</c>, <c>README</c>...): wrapped, in a reading font.</summary>
    public ReadingProfileOptions Prose { get; set; } =
        new()
        {
            Font = "literata",
            Size = 17,
            LineHeight = 1.65,
            Width = 72,
            Padding = 32,
            Wrap = true,
            LineNumbers = false,
        };

    public ReadingOptions Copy() =>
        new()
        {
            Theme = Theme,
            CustomFont = CustomFont,
            Code = Code.Copy(),
            Prose = Prose.Copy(),
        };
}
