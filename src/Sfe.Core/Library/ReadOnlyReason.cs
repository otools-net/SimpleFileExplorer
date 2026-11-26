namespace Otools.Net.Sfe.Core.Library;

/// <summary>
/// Why the asker can't change the files of a folder listed (agents/docs/features/item-access.md): the first that
/// applies, SFE's own reasons before the disk's.
/// </summary>
public enum ReadOnlyReason
{
    /// <summary>The server switches off every right that changes files (<c>Sfe:Rights:Disabled</c>).</summary>
    Server,

    /// <summary>The space's <b>Files read-only</b> switch (its disabled rights).</summary>
    Space,

    /// <summary>A visitor or a guest account: rights that change files are never given to them.</summary>
    Visitor,

    /// <summary>The asker's groups give no right that changes files.</summary>
    Rights,

    /// <summary>A folder rule lets the asker only read here.</summary>
    Rule,

    /// <summary>Behind a link leaving the library, which the server keeps read-only.</summary>
    Link,

    /// <summary>The volume is read-only (a read-only mount, a disc).</summary>
    DiskReadOnly,

    /// <summary>The server's account may not write in the folder.</summary>
    DiskRefused,
}
