using Sfe.Core.Data;

namespace Sfe.Core.Access;

/// <summary>Who a folder rule is for: a group of the library, or one account.</summary>
public sealed record FolderPrincipalRef(int? GroupId, int? UserId)
{
    public static FolderPrincipalRef Group(int id) => new(id, null);

    public static FolderPrincipalRef Account(int id) => new(null, id);

    internal bool Owns(FolderRule rule) => rule.GroupId == GroupId && rule.UserId == UserId;
}
