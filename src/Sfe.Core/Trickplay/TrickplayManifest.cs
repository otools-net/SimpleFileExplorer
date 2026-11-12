namespace Sfe.Core.Trickplay;

/// <summary>
/// Trickplay of a video: <see cref="Count"/> frames, one every <see cref="Interval"/> seconds, tiled on sheets.
/// </summary>
/// <param name="Interval">Seconds between two frames; frame <c>n</c> shows second <c>n × Interval</c>.</param>
/// <param name="Columns">Frames per row of a sheet, left to right then top to bottom.</param>
/// <param name="TileWidth">Size of one frame in pixels.</param>
/// <param name="Sheets">Sheet images, numbered from 0. The last one can be partly empty.</param>
/// <param name="Version">Changes when the trickplay is generated again: sheet URLs carry it, so browsers may cache
/// them.</param>
public sealed record TrickplayManifest(
    int Interval,
    int Count,
    int Columns,
    int Rows,
    int TileWidth,
    int TileHeight,
    int Sheets,
    long Version
);
