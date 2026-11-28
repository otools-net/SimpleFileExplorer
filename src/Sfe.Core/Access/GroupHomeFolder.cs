namespace Otools.Net.Sfe.Core.Access;

/// <summary>The default home folder an account gets from one of its groups.</summary>
/// <param name="Group">The group's name.</param>
/// <param name="Template">The default as the group has it (with <see cref="HomeFolderService.UserPlaceholder"/>).</param>
/// <param name="Path">The default for this account (root-relative); null when its name makes no folder of it.</param>
public sealed record GroupHomeFolder(string Group, string Template, string? Path);
