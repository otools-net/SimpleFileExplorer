namespace Sfe.Core.Options;

/// <summary>The reading settings of one kind of text (<see cref="ReadingOptions"/>).</summary>
public sealed class ReadingProfileOptions
{
    public static readonly IReadOnlyList<string> Fonts =
    [
        "literata",
        "atkinson",
        "jetbrains-mono",
        "system-serif",
        "system-sans",
        "system-mono",
        "custom",
    ];
    public static readonly IReadOnlyList<double> LineHeights = [1.2, 1.35, 1.5, 1.65, 1.8, 2];
    public static readonly IReadOnlyList<int> Widths = [0, 60, 72, 80, 100, 120];
    public static readonly IReadOnlyList<int> Paddings = [0, 8, 16, 24, 32, 48, 64];
    public const int MinSize = 10;
    public const int MaxSize = 32;

    /// <summary>
    /// <c>literata</c>, <c>atkinson</c>, <c>jetbrains-mono</c> (bundled), <c>system-serif</c>, <c>system-sans</c>,
    /// <c>system-mono</c> (the device's) or <c>custom</c> (<see cref="ReadingOptions.CustomFont"/>).
    /// </summary>
    public string Font { get; set; } = "system-mono";

    /// <summary>Text size in CSS pixels, 10 to 32.</summary>
    public int Size { get; set; } = 14;

    /// <summary>1.2, 1.35, 1.5, 1.65, 1.8 or 2.</summary>
    public double LineHeight { get; set; } = 1.5;

    /// <summary>Longest line in characters when lines wrap: 0 (the whole width), 60, 72, 80, 100 or 120.</summary>
    public int Width { get; set; }

    /// <summary>Room around the text in CSS pixels: 0, 8, 16, 24, 32, 48 or 64.</summary>
    public int Padding { get; set; } = 8;

    public bool Wrap { get; set; }
    public bool LineNumbers { get; set; } = true;

    public ReadingProfileOptions Copy() => (ReadingProfileOptions)MemberwiseClone();
}
