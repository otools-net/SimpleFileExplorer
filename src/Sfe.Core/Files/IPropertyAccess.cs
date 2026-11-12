namespace Sfe.Core.Files;

/// <summary>The access of the person asking, while a folder is counted.</summary>
public interface IPropertyAccess
{
    /// <summary>The person sees the root-relative folder (folder rules): else it is left out without a word.</summary>
    bool Sees(string path);

    /// <summary>The full path, with no link on the way, is among the server's own files or a work folder.</summary>
    bool IsProtectedReal(string realPath);
}
