namespace Otools.Net.Sfe.Api.Security;

/// <summary>What a route answers about one library path.</summary>
public enum PathVerdict
{
    Allowed,

    /// <summary>The subject sees the path but may not do that there (403).</summary>
    Forbidden,

    /// <summary>
    /// Not a path of the library, or one the subject doesn't see: answered as missing (404), never told apart.
    /// </summary>
    NotFound,
}
