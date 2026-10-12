namespace Sfe.Core.Data;

/// <summary>Common columns of indexed directories and files.</summary>
public abstract class IndexEntry
{
    public int Id { get; set; }

    /// <summary>Absolute physical path.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Directory name, or file name without extension.</summary>
    public string Name { get; set; } = string.Empty;

    public DateTime LastWriteTime { get; set; }
    public DateTime LastAccessTime { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime RegisterTime { get; set; } = DateTime.Now;

    public override string ToString() => FullName;
}
