namespace Otools.Net.Sfe.Client;

/// <summary>The algorithm of a file's checksum.</summary>
public enum SfeChecksumAlgorithm
{
    /// <summary>SHA-256 (the server's default).</summary>
    Sha256,

    /// <summary>SHA-1.</summary>
    Sha1,

    /// <summary>MD5.</summary>
    Md5,
}
